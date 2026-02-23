using CallControl.Api.Application.QueueManagement;
using Microsoft.AspNetCore.SignalR;

namespace CallControl.Api.Hubs;

public sealed class QueueHubSignalrMessagePublisherTransport : IQueueHubMessagePublisherTransport
{
    private readonly IHubContext<QueueHub, IQueueHubClient> _hubContext;

    public QueueHubSignalrMessagePublisherTransport(IHubContext<QueueHub, IQueueHubClient> hubContext)
    {
        _hubContext = hubContext;
    }

    public async Task PublishWaitingListUpdatedAsync(QueueWaitingListUpdatedMessage message, CancellationToken ct)
    {
        await _hubContext.Clients.Group(QueueHubGroupNames.ForQueue(message.QueueId)).QueueWaitingListUpdated(message);
        await _hubContext.Clients.Group(QueueHubGroupNames.Dashboard()).QueueWaitingListUpdated(message);
    }

    public async Task PublishActiveCallsUpdatedAsync(QueueActiveCallsUpdatedMessage message, CancellationToken ct)
    {
        await _hubContext.Clients.Group(QueueHubGroupNames.ForQueue(message.QueueId)).QueueActiveCallsUpdated(message);
        await _hubContext.Clients.Group(QueueHubGroupNames.Dashboard()).QueueActiveCallsUpdated(message);
    }

    public async Task PublishStatsUpdatedAsync(QueueStatsUpdatedMessage message, CancellationToken ct)
    {
        await _hubContext.Clients.Group(QueueHubGroupNames.ForQueue(message.QueueId)).QueueStatsUpdated(message);
        await _hubContext.Clients.Group(QueueHubGroupNames.Dashboard()).QueueStatsUpdated(message);
    }

    public async Task PublishAgentStatusChangedAsync(QueueAgentStatusChangedMessage message, CancellationToken ct)
    {
        if (message.QueueId is not null && message.QueueId.Value > 0)
        {
            await _hubContext.Clients.Group(QueueHubGroupNames.ForQueue(message.QueueId.Value)).QueueAgentStatusChanged(message);
        }

        await _hubContext.Clients.Group(QueueHubGroupNames.Dashboard()).QueueAgentStatusChanged(message);
    }
}

