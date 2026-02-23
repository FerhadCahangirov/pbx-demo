using pbx_demo_backend.Domain.QueueManagement.Contracts;

namespace CallControl.Api.Hubs;

// Batch 1 typed SignalR contracts for queue module.
// No Hub implementation here (contract freeze only).

public interface IQueueHubClient
{
    Task QueueWaitingListUpdated(QueueWaitingListUpdatedMessage message);
    Task QueueActiveCallsUpdated(QueueActiveCallsUpdatedMessage message);
    Task QueueAgentStatusChanged(QueueAgentStatusChangedMessage message);
    Task QueueStatsUpdated(QueueStatsUpdatedMessage message);
}

public sealed class QueueWaitingListUpdatedMessage
{
    public long QueueId { get; set; }
    public DateTimeOffset AsOfUtc { get; set; }
    public long Version { get; set; }
    public List<QueueWaitingCallLiveDto> WaitingCalls { get; set; } = [];
}

public sealed class QueueActiveCallsUpdatedMessage
{
    public long QueueId { get; set; }
    public DateTimeOffset AsOfUtc { get; set; }
    public long Version { get; set; }
    public List<QueueActiveCallLiveDto> ActiveCalls { get; set; } = [];
}

public sealed class QueueAgentStatusChangedMessage
{
    public long? QueueId { get; set; }
    public long AgentId { get; set; }
    public string ExtensionNumber { get; set; } = string.Empty;
    public string QueueStatus { get; set; } = string.Empty;
    public string ActivityType { get; set; } = string.Empty;
    public string? CurrentCallKey { get; set; }
    public DateTimeOffset AtUtc { get; set; }
}

public sealed class QueueStatsUpdatedMessage
{
    public long QueueId { get; set; }
    public DateTimeOffset AsOfUtc { get; set; }
    public QueueStatsSummaryDto Stats { get; set; } = new();
}

public static class QueueHubGroupNames
{
    public static string ForQueue(long queueId) => $"queue:{queueId}";
    public static string Dashboard() => "queue:dashboard";
}
