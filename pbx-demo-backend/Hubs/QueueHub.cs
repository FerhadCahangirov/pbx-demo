using CallControl.Api.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using pbx_demo_backend.Domain.QueueManagement.Contracts;

namespace CallControl.Api.Hubs;

[Authorize]
public sealed class QueueHub : Hub<IQueueHubClient>
{
    private readonly IQueueLiveStateService _queueLiveStateService;
    private readonly ILogger<QueueHub> _logger;

    public QueueHub(
        IQueueLiveStateService queueLiveStateService,
        ILogger<QueueHub> logger)
    {
        _queueLiveStateService = queueLiveStateService;
        _logger = logger;
    }

    public async Task SubscribeQueue(long queueId)
    {
        await ExecuteHubActionAsync(nameof(SubscribeQueue), async () =>
        {
            if (queueId <= 0)
            {
                throw new BadRequestException("queueId must be greater than zero.");
            }

            await Groups.AddToGroupAsync(Context.ConnectionId, QueueHubGroupNames.ForQueue(queueId));
            await SendSnapshotToCallerAsync(queueId, Context.ConnectionAborted);
        });
    }

    public Task UnsubscribeQueue(long queueId)
    {
        return ExecuteHubActionAsync(nameof(UnsubscribeQueue), async () =>
        {
            if (queueId <= 0)
            {
                throw new BadRequestException("queueId must be greater than zero.");
            }

            await Groups.RemoveFromGroupAsync(Context.ConnectionId, QueueHubGroupNames.ForQueue(queueId));
        });
    }

    public Task SubscribeDashboard()
        => ExecuteHubActionAsync(nameof(SubscribeDashboard), () =>
            Groups.AddToGroupAsync(Context.ConnectionId, QueueHubGroupNames.Dashboard()));

    public Task UnsubscribeDashboard()
        => ExecuteHubActionAsync(nameof(UnsubscribeDashboard), () =>
            Groups.RemoveFromGroupAsync(Context.ConnectionId, QueueHubGroupNames.Dashboard()));

    public Task RequestQueueSnapshot(long queueId)
    {
        return ExecuteHubActionAsync(nameof(RequestQueueSnapshot), async () =>
        {
            if (queueId <= 0)
            {
                throw new BadRequestException("queueId must be greater than zero.");
            }

            await SendSnapshotToCallerAsync(queueId, Context.ConnectionAborted);
        });
    }

    public Task PublishQueueSnapshot(long queueId)
    {
        return ExecuteHubActionAsync(nameof(PublishQueueSnapshot), async () =>
        {
            if (queueId <= 0)
            {
                throw new BadRequestException("queueId must be greater than zero.");
            }

            await _queueLiveStateService.PublishSnapshotAsync(queueId, Context.ConnectionAborted);
        });
    }

    private async Task SendSnapshotToCallerAsync(long queueId, CancellationToken ct)
    {
        var snapshot = await _queueLiveStateService.GetSnapshotAsync(queueId, ct);

        await Clients.Caller.QueueWaitingListUpdated(new QueueWaitingListUpdatedMessage
        {
            QueueId = snapshot.QueueId,
            AsOfUtc = snapshot.AsOfUtc,
            Version = snapshot.Version,
            WaitingCalls = snapshot.WaitingCalls
        });

        await Clients.Caller.QueueActiveCallsUpdated(new QueueActiveCallsUpdatedMessage
        {
            QueueId = snapshot.QueueId,
            AsOfUtc = snapshot.AsOfUtc,
            Version = snapshot.Version,
            ActiveCalls = snapshot.ActiveCalls
        });

        foreach (var agent in snapshot.AgentStatuses)
        {
            await Clients.Caller.QueueAgentStatusChanged(new QueueAgentStatusChangedMessage
            {
                QueueId = queueId,
                AgentId = agent.AgentId,
                ExtensionNumber = agent.ExtensionNumber,
                QueueStatus = agent.QueueStatus,
                ActivityType = agent.ActivityType,
                CurrentCallKey = agent.CurrentCallKey,
                AtUtc = agent.AtUtc
            });
        }

        await Clients.Caller.QueueStatsUpdated(new QueueStatsUpdatedMessage
        {
            QueueId = snapshot.QueueId,
            AsOfUtc = snapshot.AsOfUtc,
            Stats = snapshot.Stats
        });
    }

    private async Task ExecuteHubActionAsync(string methodName, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (AppException ex)
        {
            throw new HubException(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "QueueHub method {MethodName} failed. ConnectionId={ConnectionId}", methodName, Context.ConnectionId);
            throw;
        }
    }
}

