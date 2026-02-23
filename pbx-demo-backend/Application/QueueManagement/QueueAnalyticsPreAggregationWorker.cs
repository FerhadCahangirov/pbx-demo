using CallControl.Api.Infrastructure.QueueManagement.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using pbx_demo_backend.Domain.QueueManagement.Contracts;

namespace CallControl.Api.Application.QueueManagement;

public sealed class QueueAnalyticsPreAggregationWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<QueueAnalyticsOptions> _optionsMonitor;
    private readonly IOptionsMonitor<QueueApplicationOptions> _batch6OptionsMonitor;
    private readonly ILogger<QueueAnalyticsPreAggregationWorker> _logger;

    public QueueAnalyticsPreAggregationWorker(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<QueueAnalyticsOptions> optionsMonitor,
        IOptionsMonitor<QueueApplicationOptions> batch6OptionsMonitor,
        ILogger<QueueAnalyticsPreAggregationWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _optionsMonitor = optionsMonitor;
        _batch6OptionsMonitor = batch6OptionsMonitor;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var startupDelay = TimeSpan.FromSeconds(Math.Max(0, _optionsMonitor.CurrentValue.WorkerStartupDelaySeconds));
        if (startupDelay > TimeSpan.Zero)
        {
            await Task.Delay(startupDelay, stoppingToken);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var options = _optionsMonitor.CurrentValue;
            var delay = TimeSpan.FromSeconds(Math.Max(10, options.PreAggregationIntervalSeconds));

            if (!options.EnablePreAggregationWorker)
            {
                await Task.Delay(delay, stoppingToken);
                continue;
            }
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var aggregator = scope.ServiceProvider.GetRequiredService<QueueAnalyticsBucketAggregator>();
                    var affected = await aggregator.AggregateRecentAsync(stoppingToken);
                    _logger.LogDebug("Queue analytics pre-aggregation completed. Upserts={Affected}.", affected);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Queue analytics pre-aggregation failed.");
                }

            await Task.Delay(delay, stoppingToken);
        }
    }

    public sealed class QueueAnalyticsBucketAggregator
    {
        private readonly PBXDbContext _db;
        private readonly QueueKpiCalculator _kpiCalculator;
        private readonly IOptionsMonitor<QueueAnalyticsOptions> _optionsMonitor;

        public QueueAnalyticsBucketAggregator(
            PBXDbContext db,
            QueueKpiCalculator kpiCalculator,
            IOptionsMonitor<QueueAnalyticsOptions> optionsMonitor)
        {
            _db = db;
            _kpiCalculator = kpiCalculator;
            _optionsMonitor = optionsMonitor;
        }

        public async Task<int> AggregateRecentAsync(CancellationToken ct)
        {
            var options = _optionsMonitor.CurrentValue;
            var timeZone = ResolveTimeZone(options.BucketTimeZoneId);
            var nowUtc = DateTimeOffset.UtcNow;
            var lookback = TimeSpan.FromHours(Math.Max(1, options.PreAggregationLookbackHours));
            var fromUtc = nowUtc.Subtract(lookback);

            var calls = await _db.QueueCalls
                .AsNoTracking()
                .Where(x => x.QueueId != null && x.QueuedAtUtc != null)
                .Where(x => x.QueuedAtUtc >= fromUtc)
                .ToListAsync(ct);

            if (calls.Count == 0)
            {
                return 0;
            }

            var queueIds = calls.Select(c => c.QueueId!.Value).Distinct().ToArray();
            var queueSettingsByQueueId = queueIds.Length == 0
                ? new Dictionary<long, QueueSettingsEntity>()
                : await _db.QueueSettings
                .AsNoTracking()
                .Where(x => queueIds.Contains(x.QueueId))
                .ToDictionaryAsync(x => x.QueueId, ct);

            var affected = 0;

            if (options.AggregateHourlyBuckets)
            {
                affected += await UpsertHourBucketsAsync(timeZone, calls, queueSettingsByQueueId, fromUtc, nowUtc, ct);
            }

            if (options.AggregateDailyBuckets)
            {
                affected += await UpsertDayBucketsAsync(timeZone, calls, queueSettingsByQueueId, fromUtc, nowUtc, ct);
            }

            if (affected > 0)
            {
                await _db.SaveChangesAsync(ct);
            }

            return affected;
        }

        private async Task<int> UpsertHourBucketsAsync(
            TimeZoneInfo timeZone,
            IReadOnlyList<QueueCallEntity> calls,
            IReadOnlyDictionary<long, QueueSettingsEntity> queueSettingsByQueueId,
            DateTimeOffset fromUtc,
            DateTimeOffset toUtc,
            CancellationToken ct)
        {
            var groups = calls
                .Where(c => c.QueueId is not null && c.QueuedAtUtc is not null)
                .GroupBy(c =>
                {
                    var key = CreateHourBucketKey(c.QueuedAtUtc!.Value, timeZone);
                    return (QueueId: c.QueueId!.Value, key.BucketStartUtc);
                })
                .ToList();

            if (groups.Count == 0)
            {
                return 0;
            }

            var minBucket = groups.Min(g => g.Key.BucketStartUtc);
            var maxBucket = groups.Max(g => g.Key.BucketStartUtc).AddHours(1);

            var existing = await _db.QueueAnalyticsBucketHours
                .Where(x => x.BucketStartUtc >= minBucket && x.BucketStartUtc < maxBucket)
                .ToListAsync(ct);
            var existingByKey = existing.ToDictionary(x => (x.QueueId, x.BucketStartUtc));

            var affected = 0;
            foreach (var group in groups)
            {
                var queueId = group.Key.QueueId;
                var bucketStartUtc = group.Key.BucketStartUtc;
                var slaThreshold = queueSettingsByQueueId.TryGetValue(queueId, out var settings) ? settings.SlaTimeSec : null;
                var aggregate = _kpiCalculator.ComputeBucketAggregate(group.ToList(), slaThreshold);

                if (!existingByKey.TryGetValue((queueId, bucketStartUtc), out var row))
                {
                    row = new QueueAnalyticsBucketHourEntity
                    {
                        QueueId = queueId,
                        BucketStartUtc = bucketStartUtc,
                        TimeZoneId = timeZone.Id
                    };
                    _db.QueueAnalyticsBucketHours.Add(row);
                }

                row.TimeZoneId = timeZone.Id;
                row.TotalCalls = aggregate.TotalCalls;
                row.AnsweredCalls = aggregate.AnsweredCalls;
                row.AbandonedCalls = aggregate.AbandonedCalls;
                row.MissedCalls = aggregate.MissedCalls;
                row.WaitingMsSum = aggregate.WaitingMsSum;
                row.WaitingMsCount = aggregate.WaitingMsCount;
                row.TalkingMsSum = aggregate.TalkingMsSum;
                row.TalkingMsCount = aggregate.TalkingMsCount;
                row.SlaEligibleCalls = aggregate.SlaEligibleCalls;
                row.SlaWithinThresholdCalls = aggregate.SlaWithinThresholdCalls;
                row.UpdatedAtUtc = DateTimeOffset.UtcNow;
                affected++;
            }

            return affected;
        }

        private async Task<int> UpsertDayBucketsAsync(
            TimeZoneInfo timeZone,
            IReadOnlyList<QueueCallEntity> calls,
            IReadOnlyDictionary<long, QueueSettingsEntity> queueSettingsByQueueId,
            DateTimeOffset fromUtc,
            DateTimeOffset toUtc,
            CancellationToken ct)
        {
            var groups = calls
                .Where(c => c.QueueId is not null && c.QueuedAtUtc is not null)
                .GroupBy(c =>
                {
                    var local = TimeZoneInfo.ConvertTime(c.QueuedAtUtc!.Value, timeZone);
                    return (QueueId: c.QueueId!.Value, BucketDate: DateOnly.FromDateTime(local.DateTime));
                })
                .ToList();

            if (groups.Count == 0)
            {
                return 0;
            }

            var minDate = groups.Min(g => g.Key.BucketDate);
            var maxDate = groups.Max(g => g.Key.BucketDate);

            var existing = await _db.QueueAnalyticsBucketDays
                .Where(x => x.BucketDate >= minDate && x.BucketDate <= maxDate)
                .ToListAsync(ct);
            var existingByKey = existing.ToDictionary(x => (x.QueueId, x.BucketDate));

            var affected = 0;
            foreach (var group in groups)
            {
                var queueId = group.Key.QueueId;
                var bucketDate = group.Key.BucketDate;
                var slaThreshold = queueSettingsByQueueId.TryGetValue(queueId, out var settings) ? settings.SlaTimeSec : null;
                var aggregate = _kpiCalculator.ComputeBucketAggregate(group.ToList(), slaThreshold);

                if (!existingByKey.TryGetValue((queueId, bucketDate), out var row))
                {
                    row = new QueueAnalyticsBucketDayEntity
                    {
                        QueueId = queueId,
                        BucketDate = bucketDate,
                        TimeZoneId = timeZone.Id
                    };
                    _db.QueueAnalyticsBucketDays.Add(row);
                }

                row.TimeZoneId = timeZone.Id;
                row.TotalCalls = aggregate.TotalCalls;
                row.AnsweredCalls = aggregate.AnsweredCalls;
                row.AbandonedCalls = aggregate.AbandonedCalls;
                row.MissedCalls = aggregate.MissedCalls;
                row.WaitingMsSum = aggregate.WaitingMsSum;
                row.WaitingMsCount = aggregate.WaitingMsCount;
                row.TalkingMsSum = aggregate.TalkingMsSum;
                row.TalkingMsCount = aggregate.TalkingMsCount;
                row.SlaEligibleCalls = aggregate.SlaEligibleCalls;
                row.SlaWithinThresholdCalls = aggregate.SlaWithinThresholdCalls;
                row.UpdatedAtUtc = DateTimeOffset.UtcNow;
                affected++;
            }

            return affected;
        }

        private static (DateTimeOffset BucketStartUtc, string Label) CreateHourBucketKey(DateTimeOffset atUtc, TimeZoneInfo timeZone)
        {
            var local = TimeZoneInfo.ConvertTime(atUtc, timeZone);
            var localStart = new DateTime(local.Year, local.Month, local.Day, local.Hour, 0, 0, DateTimeKind.Unspecified);
            var bucketStartUtc = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(localStart, timeZone), TimeSpan.Zero);
            return (bucketStartUtc, localStart.ToString("yyyy-MM-dd HH:00"));
        }

        private static TimeZoneInfo ResolveTimeZone(string? id)
        {
            if (!string.IsNullOrWhiteSpace(id))
            {
                try
                {
                    return TimeZoneInfo.FindSystemTimeZoneById(id.Trim());
                }
                catch (TimeZoneNotFoundException)
                {
                }
                catch (InvalidTimeZoneException)
                {
                }
            }

            return TimeZoneInfo.Utc;
        }
    }

    public sealed class QueueAnalyticsOptions
    {
        public const string SectionName = "QueueManagement:Batch8Analytics";

        public bool EnablePreAggregationWorker { get; set; } = true;
        public int WorkerStartupDelaySeconds { get; set; } = 10;
        public int PreAggregationIntervalSeconds { get; set; } = 300;
        public int PreAggregationLookbackHours { get; set; } = 48;
        public string BucketTimeZoneId { get; set; } = "UTC";
        public bool AggregateHourlyBuckets { get; set; } = true;
        public bool AggregateDailyBuckets { get; set; } = true;
    }
}