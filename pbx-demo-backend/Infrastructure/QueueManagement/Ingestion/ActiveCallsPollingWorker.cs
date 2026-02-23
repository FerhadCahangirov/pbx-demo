using System.Text.Json;
using CallControl.Api.Infrastructure.QueueManagement.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using pbx_demo_backend.Domain.QueueManagement.Contracts;

namespace CallControl.Api.Infrastructure.QueueManagement.Ingestion;

public sealed class ActiveCallsPollingWorker : BackgroundService
{
    private const string CheckpointStreamName = "Queue.ActiveCallsPolling";
    private const string CheckpointPartitionKey = "global";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<QueueIngestionOptions> _optionsMonitor;
    private readonly ILogger<ActiveCallsPollingWorker> _logger;

    public ActiveCallsPollingWorker(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<QueueIngestionOptions> optionsMonitor,
        ILogger<ActiveCallsPollingWorker> logger)
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
            var interval = options.GetActiveCallsPollingInterval();

            if (!options.EnableActiveCallsPolling)
            {
                await Task.Delay(interval, stoppingToken);
                continue;
            }

            try
            {
                await PollAsync(options, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ActiveCalls polling failed.");
            }

            await Task.Delay(interval, stoppingToken);
        }
    }

    private async Task PollAsync(QueueIngestionOptions options, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();

        var xapiClient = scope.ServiceProvider.GetRequiredService<IQueueXapiClient>();
        var mapper = scope.ServiceProvider.GetRequiredService<QueueReconciliationMapper>();
        var eventProcessor = scope.ServiceProvider.GetRequiredService<IQueueEventProcessor>();
        var db = scope.ServiceProvider.GetRequiredService<PBXDbContext>();

        var query = new QueueODataQuery
        {
            Top = Math.Max(1, options.ActiveCallsTop),
            OrderBy = ["Id asc"]
        };

        var response = await xapiClient.ListActiveCallsAsync(query, ct);
        var rows = (response.Value ?? [])
            .OrderBy(x => x.Id)
            .ToList();

        var observedAtUtc = rows.FirstOrDefault(x => x.ServerNow is not null)?.ServerNow ?? DateTimeOffset.UtcNow;
        var snapshotKey = Guid.NewGuid();
        var envelopes = new List<QueueInboundEventEnvelope>(rows.Count);

        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            envelopes.Add(mapper.MapActiveCallObservedEnvelope(
                row,
                snapshotKey,
                observedAtUtc,
                waitOrder: i + 1,
                estimatedOrder: true));
        }

        if (envelopes.Count > 0)
        {
            await eventProcessor.ProcessBatchAsync(envelopes, ct);
        }

        var checkpoint = await db.XapiSyncCheckpoints
            .FirstOrDefaultAsync(x =>
                x.StreamName == CheckpointStreamName &&
                x.PartitionKey == CheckpointPartitionKey, ct);

        if (checkpoint is null)
        {
            checkpoint = new XapiSyncCheckpointEntity
            {
                StreamName = CheckpointStreamName,
                PartitionKey = CheckpointPartitionKey
            };
            db.XapiSyncCheckpoints.Add(checkpoint);
        }

        checkpoint.CursorValue = JsonSerializer.Serialize(new
        {
            observedAtUtc,
            snapshotKey,
            count = rows.Count
        });
        checkpoint.LastSuccessfulSyncAtUtc = observedAtUtc;
        checkpoint.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
    }
}
