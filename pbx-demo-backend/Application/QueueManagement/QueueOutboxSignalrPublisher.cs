using System.Text.Json;
using CallControl.Api.Hubs;
using CallControl.Api.Infrastructure.QueueManagement.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using pbx_demo_backend.Domain.QueueManagement.Contracts;

namespace CallControl.Api.Application.QueueManagement;

public interface IQueueOutboxSignalrPublisher
{
    Task<int> ProcessPendingAsync(CancellationToken ct);
    Task<int> ProcessPendingAsync(Guid tenantId, int take, CancellationToken ct);
}

public sealed class QueueOutboxSignalrPublisher : IQueueOutboxSignalrPublisher
{
    private readonly PBXDbContext _db;
    private readonly IOptionsMonitor<QueueApplicationOptions> _optionsMonitor;
    private readonly IQueueLiveStateService _liveStateService;
    private readonly IQueueHubMessagePublisherTransport _transport;
    private readonly ILogger<QueueOutboxSignalrPublisher> _logger;

    public QueueOutboxSignalrPublisher(
        PBXDbContext db,
        IOptionsMonitor<QueueApplicationOptions> optionsMonitor,
        IQueueLiveStateService liveStateService,
        IQueueHubMessagePublisherTransport transport,
        ILogger<QueueOutboxSignalrPublisher> logger)
    {
        _db = db;
        _optionsMonitor = optionsMonitor;
        _liveStateService = liveStateService;
        _transport = transport;
        _logger = logger;
    }

    public Task<int> ProcessPendingAsync(CancellationToken ct)
    {
        var take = Math.Max(1, _optionsMonitor.CurrentValue.OutboxPublishBatchSize);
        return ProcessPendingAsync(Guid.Empty, take, ct);
    }

    public async Task<int> ProcessPendingAsync(Guid tenantId, int take, CancellationToken ct)
    {
        var batch = await _db.OutboxMessages
            .Where(x => x.PublishedAtUtc == null)
            .OrderBy(x => x.Id)
            .Take(Math.Max(1, take))
            .ToListAsync(ct);

        if (batch.Count == 0)
        {
            return 0;
        }

        var snapshotPublishedForQueue = new HashSet<long>();
        var processed = 0;

        foreach (var item in batch)
        {
            ct.ThrowIfCancellationRequested();
            item.AttemptCount++;

            try
            {
                var envelope = ParseEnvelope(item.PayloadJson);
                await DispatchMessageAsync(item.Topic, envelope, snapshotPublishedForQueue, ct);
                item.PublishedAtUtc = DateTimeOffset.UtcNow;
                item.LastError = null;
                processed++;
            }
            catch (Exception ex)
            {
                item.LastError = Truncate(ex.ToString(), 2048);
                _logger.LogError(ex, "Queue outbox publish failed for message {MessageId}, topic {Topic}.", item.Id, item.Topic);
            }

            await _db.SaveChangesAsync(ct);
        }

        return processed;
    }

    private async Task DispatchMessageAsync(
        string topic,
        QueueOutboxEnvelope envelope,
        HashSet<long> snapshotPublishedForQueue,
        CancellationToken ct)
    {
        var normalizedTopic = topic?.Trim() ?? string.Empty;

        if (normalizedTopic.Equals("queue.agent.status.changed", StringComparison.OrdinalIgnoreCase))
        {
            var message = await TryBuildAgentStatusMessageAsync(envelope, ct);
            if (message is not null)
            {
                await _transport.PublishAgentStatusChangedAsync(message, ct);
            }

            if (envelope.QueueId is not null && envelope.QueueId.Value > 0 && snapshotPublishedForQueue.Add(envelope.QueueId.Value))
            {
                await _liveStateService.PublishSnapshotAsync(envelope.QueueId.Value, ct);
            }

            return;
        }

        if (normalizedTopic.StartsWith("queue.call.", StringComparison.OrdinalIgnoreCase)
            || normalizedTopic.Equals("queue.domain.event", StringComparison.OrdinalIgnoreCase))
        {
            if (envelope.QueueId is not null && envelope.QueueId.Value > 0 && snapshotPublishedForQueue.Add(envelope.QueueId.Value))
            {
                await _liveStateService.PublishSnapshotAsync(envelope.QueueId.Value, ct);
            }

            return;
        }

        // Unknown queue outbox topics are considered handled for now to avoid blocking the queue stream.
        _logger.LogDebug("Queue outbox topic {Topic} is not mapped to a publisher action in Batch 6.", normalizedTopic);
    }

    private async Task<QueueAgentStatusChangedMessage?> TryBuildAgentStatusMessageAsync(QueueOutboxEnvelope envelope, CancellationToken ct)
    {
        if (envelope.DomainEvent.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var agentId = TryReadInt64(envelope.DomainEvent, "AgentId");
        if (agentId is null || agentId <= 0)
        {
            return null;
        }

        var extension = await _db.Extensions
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == agentId.Value, ct);

        var currentStatus = TryReadString(envelope.DomainEvent, "CurrentStatus") ?? "Unknown";
        var occurredAtUtc = TryReadDateTimeOffset(envelope.DomainEvent, "OccurredAtUtc") ?? DateTimeOffset.UtcNow;

        return new QueueAgentStatusChangedMessage
        {
            QueueId = envelope.QueueId,
            AgentId = agentId.Value,
            ExtensionNumber = extension?.ExtensionNumber ?? string.Empty,
            QueueStatus = currentStatus,
            ActivityType = "StatusChange",
            CurrentCallKey = envelope.CallKey,
            AtUtc = occurredAtUtc
        };
    }

    private static QueueOutboxEnvelope ParseEnvelope(string payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return new QueueOutboxEnvelope();
        }

        using var doc = JsonDocument.Parse(payloadJson);
        var root = doc.RootElement;

        return new QueueOutboxEnvelope
        {
            QueueId = TryReadInt64(root, "queueId"),
            CallKey = TryReadString(root, "callKey"),
            DomainEventType = TryReadString(root, "domainEventType"),
            DomainEvent = root.TryGetProperty("domainEvent", out var domainEvent)
                ? domainEvent.Clone()
                : default
        };
    }

    private static Guid? TryReadGuid(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var node) || node.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return Guid.TryParse(node.GetString(), out var value) ? value : null;
    }

    private static long? TryReadInt64(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var node))
        {
            return null;
        }

        if (node.ValueKind == JsonValueKind.Number && node.TryGetInt64(out var n))
        {
            return n;
        }

        if (node.ValueKind == JsonValueKind.String && long.TryParse(node.GetString(), out n))
        {
            return n;
        }

        return null;
    }

    private static string? TryReadString(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var node) && node.ValueKind == JsonValueKind.String
            ? node.GetString()
            : null;
    }

    private static DateTimeOffset? TryReadDateTimeOffset(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var node))
        {
            return null;
        }

        if (node.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return DateTimeOffset.TryParse(node.GetString(), out var value) ? value : null;
    }

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];
}

internal sealed class QueueOutboxEnvelope
{
    public long? QueueId { get; set; }
    public string? CallKey { get; set; }
    public string? DomainEventType { get; set; }
    public JsonElement DomainEvent { get; set; }
}
