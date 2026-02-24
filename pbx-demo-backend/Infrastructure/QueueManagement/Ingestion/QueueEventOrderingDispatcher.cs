using CallControl.Api.Infrastructure.QueueManagement.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using pbx_demo_backend.Domain.QueueManagement.Contracts;

namespace CallControl.Api.Infrastructure.QueueManagement.Ingestion;

public sealed class QueueEventOrderingDispatcher
{
    private readonly PBXDbContext _db;
    private readonly QueueCallLifecycleManager _lifecycleManager;
    private readonly IOptionsMonitor<QueueIngestionOptions> _optionsMonitor;
    private readonly ILogger<QueueEventOrderingDispatcher> _logger;

    public QueueEventOrderingDispatcher(
        PBXDbContext db,
        QueueCallLifecycleManager lifecycleManager,
        IOptionsMonitor<QueueIngestionOptions> optionsMonitor,
        ILogger<QueueEventOrderingDispatcher> logger)
    {
        _db = db;
        _lifecycleManager = lifecycleManager;
        _optionsMonitor = optionsMonitor;
        _logger = logger;
    }

    public async Task<int> DispatchNextBatchAsync(CancellationToken ct)
    {
        var options = _optionsMonitor.CurrentValue;
        var nowUtc = DateTimeOffset.UtcNow;
        var leaseTimeoutUtc = nowUtc.AddSeconds(-Math.Max(10, options.InboxProcessingLeaseTimeoutSeconds));
        var take = Math.Max(1, options.InboxBatchSize);

        var pending = await _db.QueueCallEvents
            .Where(x =>
                x.ProcessingStatus == QueueCallEventProcessingStatus.Pending
                || (x.ProcessingStatus == QueueCallEventProcessingStatus.Failed && x.NextAttemptAtUtc != null && x.NextAttemptAtUtc <= nowUtc)
                || (x.ProcessingStatus == QueueCallEventProcessingStatus.Processing && x.LastAttemptAtUtc != null && x.LastAttemptAtUtc <= leaseTimeoutUtc))
            .OrderBy(x => x.EventAtUtc)
            .ThenBy(x => x.Id)
            .Take(take)
            .ToListAsync(ct);

        if (pending.Count == 0)
        {
            return 0;
        }

        var groups = pending
            .GroupBy(x => x.OrderingKey, StringComparer.Ordinal)
            .OrderBy(g => g.Min(x => x.EventAtUtc))
            .ThenBy(g => g.Min(x => x.Id));

        var processedCount = 0;
        foreach (var group in groups)
        {
            var stopPartition = false;
            foreach (var item in group.OrderBy(x => x.EventAtUtc).ThenBy(x => x.Id))
            {
                if (stopPartition)
                {
                    continue;
                }

                ct.ThrowIfCancellationRequested();
                var success = await ProcessSingleEventAsync(item, options, nowUtc, ct);
                processedCount++;

                if (!success)
                {
                    stopPartition = true;
                }
            }
        }

        return processedCount;
    }

    private async Task<bool> ProcessSingleEventAsync(
        QueueCallEventEntity item,
        QueueIngestionOptions options,
        DateTimeOffset nowUtc,
        CancellationToken ct)
    {
        item.ProcessingStatus = QueueCallEventProcessingStatus.Processing;
        item.ProcessingAttemptCount = Math.Max(0, item.ProcessingAttemptCount) + 1;
        item.LastAttemptAtUtc = nowUtc;
        item.NextAttemptAtUtc = null;
        item.LastError = null;

        await _db.SaveChangesAsync(ct);

        try
        {
            await _lifecycleManager.ApplyAsync(item, ct);
            item.ProcessingStatus = QueueCallEventProcessingStatus.Processed;
            item.NextAttemptAtUtc = null;
            item.LastError = null;
            await _db.SaveChangesAsync(ct);
            return true;
        }
        catch (Exception ex)
        {
            var maxAttempts = Math.Max(1, options.InboxMaxAttempts);
            var isDeadLetter = item.ProcessingAttemptCount >= maxAttempts;
            item.ProcessingStatus = isDeadLetter
                ? QueueCallEventProcessingStatus.DeadLetter
                : QueueCallEventProcessingStatus.Failed;
            item.LastError = Truncate(ex.ToString(), 2048);

            if (!isDeadLetter)
            {
                var baseSeconds = Math.Max(1, options.InboxRetryBaseDelaySeconds);
                var exponent = Math.Min(6, Math.Max(0, item.ProcessingAttemptCount - 1));
                var delaySeconds = baseSeconds * Math.Pow(2, exponent);
                item.NextAttemptAtUtc = nowUtc.AddSeconds(delaySeconds);
            }
            else
            {
                item.NextAttemptAtUtc = null;
            }

            await _db.SaveChangesAsync(ct);

            if (isDeadLetter)
            {
                _logger.LogError(
                    ex,
                    "Queue event moved to dead-letter. EventId={EventId}, OrderingKey={OrderingKey}, Attempts={Attempts}.",
                    item.Id,
                    item.OrderingKey,
                    item.ProcessingAttemptCount);
            }
            else
            {
                _logger.LogWarning(
                    ex,
                    "Queue event processing failed. EventId={EventId}, OrderingKey={OrderingKey}, Attempt={Attempt}, NextAttemptAtUtc={NextAttemptAtUtc}.",
                    item.Id,
                    item.OrderingKey,
                    item.ProcessingAttemptCount,
                    item.NextAttemptAtUtc);
            }

            return false;
        }
    }

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];
}
