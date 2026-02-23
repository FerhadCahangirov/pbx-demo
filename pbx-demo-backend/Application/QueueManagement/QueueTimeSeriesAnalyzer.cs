using pbx_demo_backend.Domain.QueueManagement.Contracts;

namespace CallControl.Api.Application.QueueManagement;

public sealed class QueueTimeSeriesAnalyzer
{
    private readonly QueueKpiCalculator _kpiCalculator;

    public QueueTimeSeriesAnalyzer(QueueKpiCalculator kpiCalculator)
    {
        _kpiCalculator = kpiCalculator;
    }

    public List<QueueTimeSeriesBucketResult> BuildFromCalls(
        long queueId,
        QueueAnalyticsQuery query,
        IReadOnlyList<QueueCallEntity> calls)
    {
        ArgumentNullException.ThrowIfNull(query);
        calls ??= [];

        var granularity = ParseGranularity(query.Bucket);
        var timeZone = ResolveTimeZone(query.TimeZoneId);

        var grouped = calls
            .Where(c => c.QueuedAtUtc is not null)
            .GroupBy(c => CreateBucketKey(c.QueuedAtUtc!.Value, granularity, timeZone))
            .ToDictionary(g => g.Key, g => g.ToList());

        var buckets = new List<QueueTimeSeriesBucketResult>();
        foreach (var key in EnumerateBuckets(query.FromUtc, query.ToUtc, granularity, timeZone))
        {
            grouped.TryGetValue(key, out var bucketCalls);
            bucketCalls ??= [];

            var kpi = _kpiCalculator.Calculate(queueId, query, bucketCalls, query.SlaThresholdSec);
            buckets.Add(new QueueTimeSeriesBucketResult
            {
                QueueId = queueId,
                Bucket = granularity.ToString().ToLowerInvariant(),
                TimeZoneId = timeZone.Id,
                BucketStartUtc = key.BucketStartUtc,
                BucketEndUtc = key.BucketEndUtc,
                BucketLabel = key.LocalLabel,
                TotalCalls = kpi.TotalCalls,
                AnsweredCalls = kpi.AnsweredCalls,
                AbandonedCalls = kpi.AbandonedCalls,
                MissedCalls = kpi.MissedCalls,
                AverageWaitingMs = kpi.AverageWaitingMsAll,
                AverageTalkingMs = kpi.AverageTalkingMs,
                SlaPct = kpi.SlaPct,
                QueueCongestionIndex = kpi.QueueCongestionIndex,
                PeakConcurrency = kpi.PeakConcurrency
            });
        }

        return buckets;
    }

    public List<QueueTimeSeriesBucketResult> BuildFromHourBuckets(
        long queueId,
        QueueAnalyticsQuery query,
        IReadOnlyList<QueueAnalyticsBucketHourEntity> rows)
    {
        ArgumentNullException.ThrowIfNull(query);
        rows ??= [];

        var timeZone = ResolveTimeZone(query.TimeZoneId);
        var granularity = QueueTimeBucketGranularity.Hour;

        var byStart = rows
            .Where(x => x.QueueId == queueId)
            .ToDictionary(x => x.BucketStartUtc);

        var results = new List<QueueTimeSeriesBucketResult>();
        foreach (var key in EnumerateBuckets(query.FromUtc, query.ToUtc, granularity, timeZone))
        {
            byStart.TryGetValue(key.BucketStartUtc, out var row);
            results.Add(ToBucketResult(queueId, granularity, timeZone, key, row));
        }

        return results;
    }

    public List<QueueTimeSeriesBucketResult> BuildFromDayBuckets(
        long queueId,
        QueueAnalyticsQuery query,
        IReadOnlyList<QueueAnalyticsBucketDayEntity> rows)
    {
        ArgumentNullException.ThrowIfNull(query);
        rows ??= [];

        var timeZone = ResolveTimeZone(query.TimeZoneId);
        var granularity = QueueTimeBucketGranularity.Day;

        var byDate = rows
            .Where(x => x.QueueId == queueId)
            .ToDictionary(x => x.BucketDate);

        var results = new List<QueueTimeSeriesBucketResult>();
        foreach (var key in EnumerateBuckets(query.FromUtc, query.ToUtc, granularity, timeZone))
        {
            var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(key.BucketStartUtc, timeZone).DateTime);
            byDate.TryGetValue(localDate, out var row);
            results.Add(ToBucketResult(queueId, granularity, timeZone, key, row));
        }

        return results;
    }

    private static QueueTimeSeriesBucketResult ToBucketResult(
        long queueId,
        QueueTimeBucketGranularity granularity,
        TimeZoneInfo timeZone,
        QueueTimeBucketKey key,
        QueueAnalyticsBucketHourEntity? row)
    {
        return new QueueTimeSeriesBucketResult
        {
            QueueId = queueId,
            Bucket = granularity.ToString().ToLowerInvariant(),
            TimeZoneId = timeZone.Id,
            BucketStartUtc = key.BucketStartUtc,
            BucketEndUtc = key.BucketEndUtc,
            BucketLabel = key.LocalLabel,
            TotalCalls = row?.TotalCalls ?? 0,
            AnsweredCalls = row?.AnsweredCalls ?? 0,
            AbandonedCalls = row?.AbandonedCalls ?? 0,
            MissedCalls = row?.MissedCalls ?? 0,
            AverageWaitingMs = row is null || row.WaitingMsCount == 0 ? null : (long?)(row.WaitingMsSum / row.WaitingMsCount),
            AverageTalkingMs = row is null || row.TalkingMsCount == 0 ? null : (long?)(row.TalkingMsSum / row.TalkingMsCount),
            SlaPct = row is null || row.SlaEligibleCalls == 0
                ? null
                : Math.Round((decimal)row.SlaWithinThresholdCalls * 100m / row.SlaEligibleCalls, 2, MidpointRounding.AwayFromZero),
            QueueCongestionIndex = null,
            PeakConcurrency = null
        };
    }

    private static QueueTimeSeriesBucketResult ToBucketResult(
        long queueId,
        QueueTimeBucketGranularity granularity,
        TimeZoneInfo timeZone,
        QueueTimeBucketKey key,
        QueueAnalyticsBucketDayEntity? row)
    {
        return new QueueTimeSeriesBucketResult
        {
            QueueId = queueId,
            Bucket = granularity.ToString().ToLowerInvariant(),
            TimeZoneId = timeZone.Id,
            BucketStartUtc = key.BucketStartUtc,
            BucketEndUtc = key.BucketEndUtc,
            BucketLabel = key.LocalLabel,
            TotalCalls = row?.TotalCalls ?? 0,
            AnsweredCalls = row?.AnsweredCalls ?? 0,
            AbandonedCalls = row?.AbandonedCalls ?? 0,
            MissedCalls = row?.MissedCalls ?? 0,
            AverageWaitingMs = row is null || row.WaitingMsCount == 0 ? null : (long?)(row.WaitingMsSum / row.WaitingMsCount),
            AverageTalkingMs = row is null || row.TalkingMsCount == 0 ? null : (long?)(row.TalkingMsSum / row.TalkingMsCount),
            SlaPct = row is null || row.SlaEligibleCalls == 0
                ? null
                : Math.Round((decimal)row.SlaWithinThresholdCalls * 100m / row.SlaEligibleCalls, 2, MidpointRounding.AwayFromZero),
            QueueCongestionIndex = null,
            PeakConcurrency = null
        };
    }

    private static QueueTimeBucketGranularity ParseGranularity(string? bucket)
    {
        var normalized = bucket?.Trim().ToLowerInvariant();
        return normalized switch
        {
            "day" or "daily" => QueueTimeBucketGranularity.Day,
            "month" or "monthly" => QueueTimeBucketGranularity.Month,
            _ => QueueTimeBucketGranularity.Hour
        };
    }

    private static TimeZoneInfo ResolveTimeZone(string? timeZoneId)
    {
        if (!string.IsNullOrWhiteSpace(timeZoneId))
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId.Trim());
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

    private static QueueTimeBucketKey CreateBucketKey(
        DateTimeOffset atUtc,
        QueueTimeBucketGranularity granularity,
        TimeZoneInfo timeZone)
    {
        var local = TimeZoneInfo.ConvertTime(atUtc, timeZone);
        DateTime localStart;
        DateTime localEnd;

        switch (granularity)
        {
            case QueueTimeBucketGranularity.Day:
                localStart = new DateTime(local.Year, local.Month, local.Day, 0, 0, 0, DateTimeKind.Unspecified);
                localEnd = localStart.AddDays(1);
                break;
            case QueueTimeBucketGranularity.Month:
                localStart = new DateTime(local.Year, local.Month, 1, 0, 0, 0, DateTimeKind.Unspecified);
                localEnd = localStart.AddMonths(1);
                break;
            default:
                localStart = new DateTime(local.Year, local.Month, local.Day, local.Hour, 0, 0, DateTimeKind.Unspecified);
                localEnd = localStart.AddHours(1);
                break;
        }

        var bucketStartUtc = ToUtc(localStart, timeZone);
        var bucketEndUtc = ToUtc(localEnd, timeZone);
        return new QueueTimeBucketKey
        {
            BucketStartUtc = bucketStartUtc,
            BucketEndUtc = bucketEndUtc,
            LocalLabel = granularity switch
            {
                QueueTimeBucketGranularity.Month => localStart.ToString("yyyy-MM"),
                QueueTimeBucketGranularity.Day => localStart.ToString("yyyy-MM-dd"),
                _ => localStart.ToString("yyyy-MM-dd HH:00")
            }
        };
    }

    private static IEnumerable<QueueTimeBucketKey> EnumerateBuckets(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        QueueTimeBucketGranularity granularity,
        TimeZoneInfo timeZone)
    {
        if (toUtc <= fromUtc)
        {
            yield break;
        }

        var startKey = CreateBucketKey(fromUtc, granularity, timeZone);
        var localCursor = TimeZoneInfo.ConvertTime(startKey.BucketStartUtc, timeZone);
        var localCursorDateTime = DateTime.SpecifyKind(localCursor.DateTime, DateTimeKind.Unspecified);

        while (true)
        {
            DateTime localStart = granularity switch
            {
                QueueTimeBucketGranularity.Day => new DateTime(localCursorDateTime.Year, localCursorDateTime.Month, localCursorDateTime.Day, 0, 0, 0, DateTimeKind.Unspecified),
                QueueTimeBucketGranularity.Month => new DateTime(localCursorDateTime.Year, localCursorDateTime.Month, 1, 0, 0, 0, DateTimeKind.Unspecified),
                _ => new DateTime(localCursorDateTime.Year, localCursorDateTime.Month, localCursorDateTime.Day, localCursorDateTime.Hour, 0, 0, DateTimeKind.Unspecified)
            };

            DateTime localEnd = granularity switch
            {
                QueueTimeBucketGranularity.Day => localStart.AddDays(1),
                QueueTimeBucketGranularity.Month => localStart.AddMonths(1),
                _ => localStart.AddHours(1)
            };

            var bucketStartUtc = ToUtc(localStart, timeZone);
            if (bucketStartUtc >= toUtc)
            {
                yield break;
            }

            var bucketEndUtc = ToUtc(localEnd, timeZone);
            yield return new QueueTimeBucketKey
            {
                BucketStartUtc = bucketStartUtc,
                BucketEndUtc = bucketEndUtc,
                LocalLabel = granularity switch
                {
                    QueueTimeBucketGranularity.Month => localStart.ToString("yyyy-MM"),
                    QueueTimeBucketGranularity.Day => localStart.ToString("yyyy-MM-dd"),
                    _ => localStart.ToString("yyyy-MM-dd HH:00")
                }
            };

            localCursorDateTime = localEnd;
        }
    }

    private static DateTimeOffset ToUtc(DateTime localUnspecified, TimeZoneInfo timeZone)
    {
        var utc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(localUnspecified, DateTimeKind.Unspecified), timeZone);
        return new DateTimeOffset(utc, TimeSpan.Zero);
    }
}

public sealed class QueueTimeSeriesBucketResult
{
    public long QueueId { get; set; }
    public string Bucket { get; set; } = "hour";
    public string TimeZoneId { get; set; } = "UTC";
    public DateTimeOffset BucketStartUtc { get; set; }
    public DateTimeOffset BucketEndUtc { get; set; }
    public string BucketLabel { get; set; } = string.Empty;
    public long TotalCalls { get; set; }
    public long AnsweredCalls { get; set; }
    public long AbandonedCalls { get; set; }
    public long MissedCalls { get; set; }
    public long? AverageWaitingMs { get; set; }
    public long? AverageTalkingMs { get; set; }
    public decimal? SlaPct { get; set; }
    public decimal? QueueCongestionIndex { get; set; }
    public int? PeakConcurrency { get; set; }
}

internal enum QueueTimeBucketGranularity
{
    Hour = 0,
    Day = 1,
    Month = 2
}

internal sealed class QueueTimeBucketKey
{
    public DateTimeOffset BucketStartUtc { get; set; }
    public DateTimeOffset BucketEndUtc { get; set; }
    public string LocalLabel { get; set; } = string.Empty;
}

