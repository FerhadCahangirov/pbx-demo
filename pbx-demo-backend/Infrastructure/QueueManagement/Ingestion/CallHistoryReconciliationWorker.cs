using CallControl.Api.Infrastructure.QueueManagement.Persistence;
using CallControl.Api.Infrastructure.QueueManagement.Xapi;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using pbx_demo_backend.Domain.QueueManagement.Contracts;

namespace CallControl.Api.Infrastructure.QueueManagement.Ingestion;

public sealed class CallHistoryReconciliationWorker : BackgroundService
{
    private const string CheckpointStreamName = "Queue.CallHistoryViewReconciliation";
    private const string CheckpointPartitionKey = "global";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<QueueIngestionOptions> _optionsMonitor;
    private readonly ILogger<CallHistoryReconciliationWorker> _logger;

    public CallHistoryReconciliationWorker(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<QueueIngestionOptions> optionsMonitor,
        ILogger<CallHistoryReconciliationWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _optionsMonitor = optionsMonitor;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var startupDelay = _optionsMonitor.CurrentValue.GetWorkerStartupDelay();
        if (startupDelay > TimeSpan.Zero)
        {
            await Task.Delay(startupDelay, stoppingToken);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var options = _optionsMonitor.CurrentValue;
            var interval = options.GetCallHistoryReconciliationInterval();

            if (!options.EnableCallHistoryReconciliation)
            {
                await Task.Delay(interval, stoppingToken);
                continue;
            }

            try
            {
                await ReconcileAsync(options, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CallHistory reconciliation failed.");
            }

            await Task.Delay(interval, stoppingToken);
        }
    }

    private async Task ReconcileAsync(QueueIngestionOptions options, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();

        var xapiClient = scope.ServiceProvider.GetRequiredService<IQueueXapiClient>();
        var mapper = scope.ServiceProvider.GetRequiredService<QueueReconciliationMapper>();
        var eventProcessor = scope.ServiceProvider.GetRequiredService<IQueueEventProcessor>();
        var db = scope.ServiceProvider.GetRequiredService<PBXDbContext>();

        var checkpoint = await db.XapiSyncCheckpoints
            .FirstOrDefaultAsync(x =>
                x.StreamName == CheckpointStreamName &&
                x.PartitionKey == CheckpointPartitionKey, ct);

        var nowUtc = DateTimeOffset.UtcNow;
        var lookback = options.GetReconciliationLookback();

        var checkpointCursorUtc = mapper.TryParseCheckpointCursor(checkpoint?.CursorValue, out var parsedCursorUtc)
            ? parsedCursorUtc
            : nowUtc.Subtract(lookback);

        // Overlap the previous cursor to tolerate out-of-order upstream data arrival.
        var fromUtc = checkpointCursorUtc.Subtract(lookback);
        var toUtc = nowUtc;

        var pageSize = Math.Max(1, options.ReconciliationPageSize);
        var skip = 0;
        var maxObservedCursorUtc = checkpointCursorUtc;
        var queues = await db.Queues
            .IgnoreQueryFilters()
            .AsNoTracking()
            .ToListAsync(ct);
        var queueIdsByNumber = queues.ToDictionary(x => x.QueueNumber, x => x.Id, StringComparer.OrdinalIgnoreCase);

        while (!ct.IsCancellationRequested)
        {
            var query = new QueueODataQuery
            {
                Top = pageSize,
                Skip = skip,
                OrderBy = ["SegmentEndTime asc", "SegmentId asc"],
                Filter = BuildCallHistoryWindowFilter(fromUtc, toUtc)
            };

            var response = await xapiClient.ListCallHistoryViewAsync(query, ct);
            var rows = (response.Value ?? [])
                .OrderBy(x => x.SegmentEndTime)
                .ThenBy(x => x.SegmentId)
                .ToList();

            if (rows.Count == 0)
            {
                break;
            }

            var observedAtUtc = DateTimeOffset.UtcNow;
            var envelopes = new List<QueueInboundEventEnvelope>(rows.Count);

            foreach (var row in rows)
            {
                var queueIdHint = mapper.ResolveQueueId(row, queueIdsByNumber);

                if (!mapper.IsLikelyDuplicateCallHistoryRow(db, row))
                {
                    db.QueueCallHistory.Add(mapper.MapCallHistoryViewRow(row, queueIdHint, observedAtUtc));
                }

                envelopes.Add(mapper.MapCallHistoryViewEnvelope(row, queueIdHint, observedAtUtc));
                if (row.SegmentEndTime > maxObservedCursorUtc)
                {
                    maxObservedCursorUtc = row.SegmentEndTime;
                }
            }

            if (envelopes.Count > 0)
            {
                await eventProcessor.ProcessBatchAsync(envelopes, ct);
            }

            skip += rows.Count;
            if (rows.Count < pageSize)
            {
                break;
            }
        }

        checkpoint ??= new XapiSyncCheckpointEntity
        {
            StreamName = CheckpointStreamName,
            PartitionKey = CheckpointPartitionKey
        };

        if (checkpoint.Id == 0)
        {
            db.XapiSyncCheckpoints.Add(checkpoint);
        }

        checkpoint.CursorValue = maxObservedCursorUtc.ToUniversalTime().ToString("O");
        checkpoint.LastSuccessfulSyncAtUtc = nowUtc;
        checkpoint.UpdatedAtUtc = nowUtc;

        await db.SaveChangesAsync(ct);
    }

    private static string BuildCallHistoryWindowFilter(DateTimeOffset fromUtc, DateTimeOffset toUtc)
    {
        var from = QueueXapiODataQueryBuilder.FormatDateTimeOffsetUtc(fromUtc);
        var to = QueueXapiODataQueryBuilder.FormatDateTimeOffsetUtc(toUtc);
        return $"SegmentEndTime ge {from} and SegmentEndTime lt {to}";
    }
}
