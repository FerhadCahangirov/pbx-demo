using Microsoft.EntityFrameworkCore;
using pbx_demo_backend.Domain.QueueManagement.Contracts;

namespace CallControl.Api.Infrastructure.QueueManagement.Persistence;

/// <summary>
/// Batch 3 persistence-layer query examples and SQL notes.
/// These are reference helpers for later analytics/application batches.
/// </summary>
public static class QueuePersistenceQueryExamples
{
    // SQL Server partitioning guidance (Batch 3 design note):
    //
    // Recommended monthly partitions on:
    // - QueueCallEvent.EventAtUtc
    // - QueueCallHistory.SegmentStartAtUtc
    // - QueueAgentActivity.OccurredAtUtc
    // - QueueWaitingSnapshot.CapturedAtUtc
    //
    // Suggested implementation (manual DBA script / migration customization later):
    // 1. Partition function by month boundary (UTC)
    // 2. Partition scheme on PRIMARY (or dedicated FG per month)
    // 3. Clustered index aligned to partition key for large append-only tables
    // 4. Sliding window maintenance (create next month, switch/archive oldest month)

    public const string SqlSingleQueueSummary = """
SELECT
    COUNT_BIG(*) AS TotalCalls,
    SUM(CASE WHEN [Disposition] = 'Answered' THEN 1 ELSE 0 END) AS AnsweredCalls,
    SUM(CASE WHEN [Disposition] = 'Missed' THEN 1 ELSE 0 END) AS MissedCalls,
    SUM(CASE WHEN [Disposition] = 'Abandoned' THEN 1 ELSE 0 END) AS AbandonedCalls,
    AVG(CASE WHEN [WaitingMs] IS NOT NULL THEN CAST([WaitingMs] AS bigint) END) AS AvgWaitingMs,
    AVG(CASE WHEN [Disposition] = 'Answered' AND [TalkingMs] IS NOT NULL THEN CAST([TalkingMs] AS bigint) END) AS AvgTalkingMs
FROM [QueueCall]
WHERE [QueueId] = @QueueId
  AND [QueuedAtUtc] >= @FromUtc
  AND [QueuedAtUtc] < @ToUtc;
""";

    public const string SqlHourlyBucketsComparison = """
SELECT
    [QueueId],
    SUM([TotalCalls]) AS TotalCalls,
    SUM([AnsweredCalls]) AS AnsweredCalls,
    SUM([AbandonedCalls]) AS AbandonedCalls,
    SUM([WaitingMsSum]) AS WaitingMsSum,
    SUM([WaitingMsCount]) AS WaitingMsCount,
    SUM([TalkingMsSum]) AS TalkingMsSum,
    SUM([TalkingMsCount]) AS TalkingMsCount
FROM [QueueAnalyticsBucketHour]
WHERE [BucketStartUtc] >= @FromUtc
  AND [BucketStartUtc] < @ToUtc
GROUP BY [QueueId];
""";

    public const string SqlQueueHourlyBucketWindow = """
SELECT
    [QueueId],
    [BucketStartUtc],
    [TotalCalls],
    [AnsweredCalls],
    [AbandonedCalls],
    [MissedCalls],
    [WaitingMsSum],
    [WaitingMsCount],
    [TalkingMsSum],
    [TalkingMsCount],
    [SlaEligibleCalls],
    [SlaWithinThresholdCalls]
FROM [QueueAnalyticsBucketHour]
WHERE [QueueId] = @QueueId
  AND [BucketStartUtc] >= @FromUtc
  AND [BucketStartUtc] < @ToUtc
ORDER BY [BucketStartUtc] ASC;
""";

    public const string SqlQueueDayBucketWindow = """
SELECT
    [QueueId],
    [BucketDate],
    [TotalCalls],
    [AnsweredCalls],
    [AbandonedCalls],
    [MissedCalls],
    [WaitingMsSum],
    [WaitingMsCount],
    [TalkingMsSum],
    [TalkingMsCount],
    [SlaEligibleCalls],
    [SlaWithinThresholdCalls]
FROM [QueueAnalyticsBucketDay]
WHERE [QueueId] = @QueueId
  AND [BucketDate] >= @FromDate
  AND [BucketDate] < @ToDate
ORDER BY [BucketDate] ASC;
""";

    public static IQueryable<QueueCallEntity> ActiveQueueCallsQuery(
        this PBXDbContext db,
        Guid tenantId,
        long queueId)
    {
        return db.QueueCalls
            .Where(x => x.QueueId == queueId)
            .Where(x =>
                x.CurrentStatus != QueueCallLifecycleStatus.Completed &&
                x.CurrentStatus != QueueCallLifecycleStatus.Abandoned)
            .OrderBy(x => x.QueuedAtUtc)
            .ThenBy(x => x.Id);
    }

    public static IQueryable<QueueCallEntity> QueueCallsInWindowQuery(
        this PBXDbContext db,
        Guid tenantId,
        long queueId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc)
    {
        return db.QueueCalls
            .Where(x =>
                x.QueueId == queueId &&
                x.QueuedAtUtc != null &&
                x.QueuedAtUtc >= fromUtc &&
                x.QueuedAtUtc < toUtc);
    }

    public static IQueryable<QueueAnalyticsBucketHourEntity> HourBucketsInWindowQuery(
        this PBXDbContext db,
        Guid tenantId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc)
    {
        return db.QueueAnalyticsBucketHours
            .Where(x => x.BucketStartUtc >= fromUtc && x.BucketStartUtc < toUtc)
            .OrderBy(x => x.BucketStartUtc)
            .ThenBy(x => x.QueueId);
    }

    public static IQueryable<QueueAnalyticsBucketHourEntity> HourBucketsForQueueInWindowQuery(
        this PBXDbContext db,
        Guid tenantId,
        long queueId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc)
    {
        return db.QueueAnalyticsBucketHours
            .Where(x => x.QueueId == queueId)
            .Where(x => x.BucketStartUtc >= fromUtc && x.BucketStartUtc < toUtc)
            .OrderBy(x => x.BucketStartUtc);
    }

    public static IQueryable<QueueAnalyticsBucketDayEntity> DayBucketsForQueueInWindowQuery(
        this PBXDbContext db,
        Guid tenantId,
        long queueId,
        DateOnly fromDate,
        DateOnly toDateExclusive)
    {
        return db.QueueAnalyticsBucketDays
            .Where(x => x.QueueId == queueId)
            .Where(x => x.BucketDate >= fromDate && x.BucketDate < toDateExclusive)
            .OrderBy(x => x.BucketDate);
    }

    public static async Task<List<QueueCallEntity>> ExampleActiveCallsReadAsync(
        PBXDbContext db,
        Guid tenantId,
        long queueId,
        CancellationToken ct)
    {
        return await db.ActiveQueueCallsQuery(tenantId, queueId)
            .AsNoTracking()
            .ToListAsync(ct);
    }
}
