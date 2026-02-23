using CallControl.Api.Hubs;

namespace CallControl.Api.Application.QueueManagement;

public interface IQueueHubMessagePublisherTransport
{
    Task PublishWaitingListUpdatedAsync(QueueWaitingListUpdatedMessage message, CancellationToken ct);
    Task PublishActiveCallsUpdatedAsync(QueueActiveCallsUpdatedMessage message, CancellationToken ct);
    Task PublishStatsUpdatedAsync(QueueStatsUpdatedMessage message, CancellationToken ct);
    Task PublishAgentStatusChangedAsync(QueueAgentStatusChangedMessage message, CancellationToken ct);
}

public sealed class QueueHubMessagePublisherTransportPlaceholder : IQueueHubMessagePublisherTransport
{
    private readonly ILogger<QueueHubMessagePublisherTransportPlaceholder> _logger;

    public QueueHubMessagePublisherTransportPlaceholder(ILogger<QueueHubMessagePublisherTransportPlaceholder> logger)
    {
        _logger = logger;
    }

    public Task PublishWaitingListUpdatedAsync(QueueWaitingListUpdatedMessage message, CancellationToken ct)
        => LogNoHubAsync("QueueWaitingListUpdated", message.QueueId, ct);

    public Task PublishActiveCallsUpdatedAsync(QueueActiveCallsUpdatedMessage message, CancellationToken ct)
        => LogNoHubAsync("QueueActiveCallsUpdated", message.QueueId, ct);

    public Task PublishStatsUpdatedAsync(QueueStatsUpdatedMessage message, CancellationToken ct)
        => LogNoHubAsync("QueueStatsUpdated", message.QueueId, ct);

    public Task PublishAgentStatusChangedAsync(QueueAgentStatusChangedMessage message, CancellationToken ct)
        => LogNoHubAsync("QueueAgentStatusChanged", message.QueueId, ct);

    private Task LogNoHubAsync(string method, long? queueId, CancellationToken ct)
    {
        _logger.LogDebug(
            "Queue hub transport placeholder dropped {Method} for queue {QueueId}. QueueHub implementation is deferred to Batch 7.",
            method,
            queueId);
        return Task.CompletedTask;
    }
}

