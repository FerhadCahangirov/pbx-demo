using CallControl.Api.Domain;
using CallControl.Api.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace CallControl.Api.Services;

public sealed class EventDispatcher
{
    private readonly IHubContext<SoftphoneHub, ISoftphoneHubClient> _hubContext;

    public EventDispatcher(IHubContext<SoftphoneHub, ISoftphoneHubClient> hubContext)
    {
        _hubContext = hubContext;
    }

    public Task PublishSnapshotAsync(string sessionId, SessionSnapshotResponse snapshot, CancellationToken cancellationToken = default)
    {
        return _hubContext.Clients.Group(HubGroupName.ForSession(sessionId)).SessionSnapshot(snapshot);
    }

    public Task PublishEventAsync(string sessionId, SoftphoneEventEnvelope envelope, CancellationToken cancellationToken = default)
    {
        return _hubContext.Clients.Group(HubGroupName.ForSession(sessionId)).SoftphoneEvent(envelope);
    }
}
