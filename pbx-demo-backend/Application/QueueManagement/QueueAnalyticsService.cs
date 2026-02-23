using CallControl.Api.Domain;
using CallControl.Api.Infrastructure.QueueManagement.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using pbx_demo_backend.Domain.QueueManagement.Contracts;

namespace CallControl.Api.Application.QueueManagement;

public sealed class QueueAnalyticsService : IQueueAnalyticsService
{
    private readonly PBXDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly IOptionsMonitor<QueueApplicationOptions> _batch6Options;
    private readonly QueueKpiCalculator _kpiCalculator;
    private readonly QueueTimeSeriesAnalyzer _timeSeriesAnalyzer;
    private readonly QueueComparisonEngine _comparisonEngine;
    private readonly QueueAgentRankingEngine _agentRankingEngine;

    public QueueAnalyticsService(
        PBXDbContext db,
        IMemoryCache cache,
        IOptionsMonitor<QueueApplicationOptions> batch6Options,
        QueueKpiCalculator kpiCalculator,
        QueueTimeSeriesAnalyzer timeSeriesAnalyzer,
        QueueComparisonEngine comparisonEngine,
        QueueAgentRankingEngine agentRankingEngine)
    {
        _db = db;
        _cache = cache;
        _batch6Options = batch6Options;
        _kpiCalculator = kpiCalculator;
        _timeSeriesAnalyzer = timeSeriesAnalyzer;
        _comparisonEngine = comparisonEngine;
        _agentRankingEngine = agentRankingEngine;
    }

    public async Task<object> GetQueueAnalyticsAsync(long queueId, QueueAnalyticsQuery query, CancellationToken ct)
    {
        ValidateAnalyticsQuery(query);
        var cacheKey = $"queue-analytics:v2:{queueId}:{NormalizeAnalyticsKey(query)}";

        if (_cache.TryGetValue(cacheKey, out QueueAnalyticsOverviewResult? cached) && cached is not null)
        {
            return cached;
        }

        var queue = await _db.Queues
            .Include(x => x.Settings)
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == queueId, ct);

        if (queue is null)
        {
            throw new NotFoundException($"Queue {queueId} was not found.");
        }

        var calls = await _db.QueueCalls
            .AsNoTracking()
            .Where(x => x.QueueId == queueId)
            .Where(x => x.QueuedAtUtc != null && x.QueuedAtUtc >= query.FromUtc && x.QueuedAtUtc < query.ToUtc)
            .OrderBy(x => x.QueuedAtUtc)
            .ThenBy(x => x.Id)
            .ToListAsync(ct);

        var activities = await _db.QueueAgentActivities
            .AsNoTracking()
            .Where(x => x.QueueId == queueId)
            .Where(x => x.OccurredAtUtc >= query.FromUtc && x.OccurredAtUtc < query.ToUtc)
            .OrderBy(x => x.OccurredAtUtc)
            .ThenBy(x => x.Id)
            .ToListAsync(ct);

        var extensionIds = calls.Select(x => x.AnsweredByExtensionId).Where(x => x is not null).Select(x => x!.Value)
            .Concat(calls.Select(x => x.LastAgentExtensionId).Where(x => x is not null).Select(x => x!.Value))
            .Concat(activities.Select(x => x.ExtensionId))
            .Distinct()
            .ToArray();

        var extensionsById = extensionIds.Length == 0
            ? new Dictionary<long, ExtensionEntity>()
            : await _db.Extensions.AsNoTracking()
                .Where(x => extensionIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, ct);

        var effectiveSla = query.SlaThresholdSec ?? queue.Settings?.SlaTimeSec;
        var kpi = _kpiCalculator.Calculate(queueId, query, calls, effectiveSla);
        var timeSeries = await BuildTimeSeriesAsync(queueId, query, calls, ct);
        var agentRankings = _agentRankingEngine.Build(queueId, query, calls, activities, extensionsById);

        var result = new QueueAnalyticsOverviewResult
        {
            QueueId = queueId,
            Query = query,
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            TotalCalls = kpi.TotalCalls,
            AnsweredCalls = kpi.AnsweredCalls,
            AbandonedCalls = kpi.AbandonedCalls,
            MissedCalls = kpi.MissedCalls,
            AverageWaitingMs = kpi.AverageWaitingMsAll,
            AverageWaitingMsAnswered = kpi.AverageWaitingMsAnswered,
            AverageWaitingMsAll = kpi.AverageWaitingMsAll,
            AverageTalkingMs = kpi.AverageTalkingMs,
            AbandonmentRatePct = kpi.AbandonmentRatePct,
            ShortAbandonCount = kpi.ShortAbandonCount,
            P90WaitingMs = kpi.P90WaitingMs,
            P95WaitingMs = kpi.P95WaitingMs,
            P90TalkingMs = kpi.P90TalkingMs,
            PeakConcurrency = kpi.PeakConcurrency,
            PeakConcurrencyAtUtc = kpi.PeakConcurrencyAtUtc,
            SlaThresholdSec = kpi.SlaThresholdSec,
            SlaEligibleCalls = kpi.SlaEligibleCalls,
            SlaWithinThresholdCalls = kpi.SlaWithinThresholdCalls,
            SlaBreachCalls = kpi.SlaBreachCalls,
            SlaPct = kpi.SlaPct,
            QueueCongestionIndex = kpi.QueueCongestionIndex,
            CongestionSignals = kpi.Signals,
            RealtimeClassification = kpi.RealtimeClassification,
            TimeSeries = timeSeries,
            AgentRankings = agentRankings
        };

        result.Buckets = timeSeries.Cast<object>().ToList();

        SetAnalyticsCache(cacheKey, result);
        return result;
    }

    public async Task<object> GetMultiQueueComparisonAsync(IReadOnlyList<long> queueIds, QueueAnalyticsQuery query, CancellationToken ct)
    {
        ValidateAnalyticsQuery(query);
        queueIds ??= [];

        var distinctQueueIds = queueIds.Where(x => x > 0).Distinct().OrderBy(x => x).ToArray();
        var cacheKey = $"queue-analytics-compare:v2:{string.Join(",", distinctQueueIds)}:{NormalizeAnalyticsKey(query)}";

        if (_cache.TryGetValue(cacheKey, out QueueAnalyticsComparisonResult? cached) && cached is not null)
        {
            return cached;
        }

        if (distinctQueueIds.Length == 0)
        {
            var empty = new QueueAnalyticsComparisonResult
            {
                Query = query,
                GeneratedAtUtc = DateTimeOffset.UtcNow,
                Queues = [],
                Comparison = new QueueComparisonSummary()
            };
            SetAnalyticsCache(cacheKey, empty);
            return empty;
        }

        var queues = await _db.Queues
            .AsNoTracking()
            .Where(x => distinctQueueIds.Contains(x.Id))
            .ToListAsync(ct);

        var calls = await _db.QueueCalls
            .AsNoTracking()
            .Where(x => x.QueueId != null && distinctQueueIds.Contains(x.QueueId.Value))
            .Where(x => x.QueuedAtUtc != null && x.QueuedAtUtc >= query.FromUtc && x.QueuedAtUtc < query.ToUtc)
            .ToListAsync(ct);

        var settingsByQueueId = await _db.QueueSettings
            .AsNoTracking()
            .Where(x => distinctQueueIds.Contains(x.QueueId))
            .ToDictionaryAsync(x => x.QueueId, ct);

        var callsByQueue = calls
            .GroupBy(x => x.QueueId!.Value)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<QueueCallEntity>)g.ToList());

        var queueResults = new List<QueueAnalyticsOverviewResult>(distinctQueueIds.Length);
        foreach (var id in distinctQueueIds)
        {
            var queueCalls = callsByQueue.TryGetValue(id, out var list) ? list : [];
            var effectiveSla = query.SlaThresholdSec
                ?? (settingsByQueueId.TryGetValue(id, out var settings) ? settings.SlaTimeSec : null);

            var kpi = _kpiCalculator.Calculate(id, query, queueCalls, effectiveSla);
            queueResults.Add(new QueueAnalyticsOverviewResult
            {
                QueueId = id,
                Query = query,
                GeneratedAtUtc = DateTimeOffset.UtcNow,
                TotalCalls = kpi.TotalCalls,
                AnsweredCalls = kpi.AnsweredCalls,
                AbandonedCalls = kpi.AbandonedCalls,
                MissedCalls = kpi.MissedCalls,
                AverageWaitingMs = kpi.AverageWaitingMsAll,
                AverageWaitingMsAnswered = kpi.AverageWaitingMsAnswered,
                AverageWaitingMsAll = kpi.AverageWaitingMsAll,
                AverageTalkingMs = kpi.AverageTalkingMs,
                AbandonmentRatePct = kpi.AbandonmentRatePct,
                ShortAbandonCount = kpi.ShortAbandonCount,
                SlaThresholdSec = kpi.SlaThresholdSec,
                SlaEligibleCalls = kpi.SlaEligibleCalls,
                SlaWithinThresholdCalls = kpi.SlaWithinThresholdCalls,
                SlaBreachCalls = kpi.SlaBreachCalls,
                SlaPct = kpi.SlaPct,
                QueueCongestionIndex = kpi.QueueCongestionIndex,
                CongestionSignals = kpi.Signals,
                PeakConcurrency = kpi.PeakConcurrency,
                PeakConcurrencyAtUtc = kpi.PeakConcurrencyAtUtc,
                RealtimeClassification = kpi.RealtimeClassification
            });
        }

        var comparison = _comparisonEngine.Build(queueResults);
        var comparisonByQueueId = comparison.Scores.ToDictionary(x => x.QueueId);
        foreach (var overview in queueResults)
        {
            if (comparisonByQueueId.TryGetValue(overview.QueueId, out var score))
            {
                overview.ComparisonScore = score;
            }
        }

        var result = new QueueAnalyticsComparisonResult
        {
            Query = query,
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            Queues = queueResults.OrderBy(x => x.ComparisonScore?.Rank ?? int.MaxValue).ThenBy(x => x.QueueId).ToList(),
            Comparison = comparison
        };

        SetAnalyticsCache(cacheKey, result);
        return result;
    }

    private async Task<List<QueueTimeSeriesBucketResult>> BuildTimeSeriesAsync(
        long queueId,
        QueueAnalyticsQuery query,
        IReadOnlyList<QueueCallEntity> calls,
        CancellationToken ct)
    {
        var bucket = query.Bucket?.Trim()?.ToLowerInvariant();
        if (bucket is "day" or "daily")
        {
            var rows = await _db.QueueAnalyticsBucketDays
                .AsNoTracking()
                .Where(x => x.QueueId == queueId)
                .Where(x => x.BucketDate >= DateOnly.FromDateTime(query.FromUtc.UtcDateTime.Date)
                         && x.BucketDate <= DateOnly.FromDateTime(query.ToUtc.UtcDateTime.Date))
                .OrderBy(x => x.BucketDate)
                .ToListAsync(ct);

            return rows.Count > 0
                ? _timeSeriesAnalyzer.BuildFromDayBuckets(queueId, query, rows)
                : _timeSeriesAnalyzer.BuildFromCalls(queueId, query, calls);
        }

        if (bucket is "hour" or "hourly" or null or "")
        {
            var rows = await _db.QueueAnalyticsBucketHours
                .AsNoTracking()
                .Where(x => x.QueueId == queueId)
                .Where(x => x.BucketStartUtc >= query.FromUtc.AddHours(-1) && x.BucketStartUtc < query.ToUtc.AddHours(1))
                .OrderBy(x => x.BucketStartUtc)
                .ToListAsync(ct);

            return rows.Count > 0
                ? _timeSeriesAnalyzer.BuildFromHourBuckets(queueId, query, rows)
                : _timeSeriesAnalyzer.BuildFromCalls(queueId, query, calls);
        }

        return _timeSeriesAnalyzer.BuildFromCalls(queueId, query, calls);
    }

    private void SetAnalyticsCache<T>(string cacheKey, T value)
    {
        var ttl = TimeSpan.FromSeconds(Math.Max(0, _batch6Options.CurrentValue.AnalyticsCacheSeconds));
        if (ttl <= TimeSpan.Zero)
        {
            return;
        }

        _cache.Set(cacheKey, value, ttl);
    }

    private static void ValidateAnalyticsQuery(QueueAnalyticsQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.ToUtc <= query.FromUtc)
        {
            throw new BadRequestException("Analytics query ToUtc must be greater than FromUtc.");
        }
    }

    private static string NormalizeAnalyticsKey(QueueAnalyticsQuery query)
    {
        return string.Join("|",
            query.FromUtc.ToUniversalTime().ToString("O"),
            query.ToUtc.ToUniversalTime().ToString("O"),
            query.Bucket?.Trim()?.ToLowerInvariant() ?? "hour",
            query.SlaThresholdSec?.ToString() ?? string.Empty,
            query.TimeZoneId?.Trim() ?? string.Empty);
    }
}

public sealed class QueueAnalyticsOverviewResult
{
    public long QueueId { get; set; }
    public QueueAnalyticsQuery Query { get; set; } = new();
    public DateTimeOffset GeneratedAtUtc { get; set; }
    public long TotalCalls { get; set; }
    public long AnsweredCalls { get; set; }
    public long AbandonedCalls { get; set; }
    public long MissedCalls { get; set; }
    public long ShortAbandonCount { get; set; }
    public decimal? AbandonmentRatePct { get; set; }
    public long? AverageWaitingMs { get; set; } // legacy alias kept for compatibility
    public long? AverageWaitingMsAnswered { get; set; }
    public long? AverageWaitingMsAll { get; set; }
    public long? AverageTalkingMs { get; set; }
    public long? P90WaitingMs { get; set; }
    public long? P95WaitingMs { get; set; }
    public long? P90TalkingMs { get; set; }
    public int? PeakConcurrency { get; set; }
    public DateTimeOffset? PeakConcurrencyAtUtc { get; set; }
    public int? SlaThresholdSec { get; set; }
    public int SlaEligibleCalls { get; set; }
    public int? SlaWithinThresholdCalls { get; set; }
    public int? SlaBreachCalls { get; set; }
    public decimal? SlaPct { get; set; }
    public decimal? QueueCongestionIndex { get; set; }
    public QueueCongestionSignalBreakdown? CongestionSignals { get; set; }
    public string? RealtimeClassification { get; set; }
    public QueueComparisonScoreResult? ComparisonScore { get; set; }
    public List<QueueTimeSeriesBucketResult> TimeSeries { get; set; } = [];
    public List<QueueAgentRankingResult> AgentRankings { get; set; } = [];
    public List<object> Buckets { get; set; } = []; // legacy alias
}

public sealed class QueueAnalyticsComparisonResult
{
    public QueueAnalyticsQuery Query { get; set; } = new();
    public DateTimeOffset GeneratedAtUtc { get; set; }
    public List<QueueAnalyticsOverviewResult> Queues { get; set; } = [];
    public QueueComparisonSummary Comparison { get; set; } = new();
}

