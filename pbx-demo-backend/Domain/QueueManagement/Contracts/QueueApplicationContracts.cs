namespace pbx_demo_backend.Domain.QueueManagement.Contracts;

// Batch 1 application-facing contracts only (no implementations).

public sealed class QueueODataQuery
{
    public int? Top { get; set; }
    public int? Skip { get; set; }
    public string? Search { get; set; }
    public string? Filter { get; set; }
    public bool? Count { get; set; }
    public List<string> OrderBy { get; set; } = [];
    public List<string> Select { get; set; } = [];
    public List<string> Expand { get; set; } = [];
}

public sealed class QueueListQuery
{
    public string? Search { get; set; }
    public bool? IsRegistered { get; set; }
    public string? QueueNumber { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string? SortBy { get; set; }
    public bool SortDescending { get; set; }
}

public sealed class QueueCallHistoryQuery
{
    public DateTimeOffset? FromUtc { get; set; }
    public DateTimeOffset? ToUtc { get; set; }
    public string? Disposition { get; set; }
    public long? AgentId { get; set; }
    public string? Search { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

public sealed class QueueAnalyticsQuery
{
    public DateTimeOffset FromUtc { get; set; }
    public DateTimeOffset ToUtc { get; set; }
    public string Bucket { get; set; } = "hour";
    public int? SlaThresholdSec { get; set; }
    public string? TimeZoneId { get; set; }
}

public sealed class QueuePagedResult<T>
{
    public int? TotalCount { get; set; }
    public List<T> Items { get; set; } = [];
}

public sealed class QueueAgentAssignmentDto
{
    public long? ExtensionId { get; set; }
    public string ExtensionNumber { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? SkillGroup { get; set; }
}

public sealed class QueueManagerAssignmentDto
{
    public long? ExtensionId { get; set; }
    public string ExtensionNumber { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
}

public sealed class QueueResetStatisticsScheduleDto
{
    public string? Frequency { get; set; }
    public string? DayOfWeek { get; set; }
    public string? Time { get; set; }
}

public sealed class QueueDestinationDto
{
    public string? To { get; set; }
    public string? Number { get; set; }
    public string? External { get; set; }
    public string? Name { get; set; }
    public string? Type { get; set; }
    public List<string>? Tags { get; set; }
}

public sealed class QueueRouteDto
{
    public bool? IsPromptEnabled { get; set; }
    public string? Prompt { get; set; }
    public QueueDestinationDto? Route { get; set; }
}

public sealed class QueueSettingsDto
{
    public bool? AgentAvailabilityMode { get; set; }
    public int? AnnouncementIntervalSec { get; set; }
    public bool? AnnounceQueuePosition { get; set; }
    public int? CallbackEnableTimeSec { get; set; }
    public string? CallbackPrefix { get; set; }
    public bool? CallUsEnableChat { get; set; }
    public bool? CallUsEnablePhone { get; set; }
    public bool? CallUsEnableVideo { get; set; }
    public string? CallUsRequirement { get; set; }
    public string? ClickToCallId { get; set; }
    public bool? EnableIntro { get; set; }
    public string? GreetingFile { get; set; }
    public string? IntroFile { get; set; }
    public string? OnHoldFile { get; set; }
    public string? PromptSet { get; set; }
    public bool? PlayFullPrompt { get; set; }
    public bool? PriorityQueue { get; set; }
    public int? RingTimeoutSec { get; set; }
    public int? MasterTimeoutSec { get; set; }
    public int? MaxCallersInQueue { get; set; }
    public int? SlaTimeSec { get; set; }
    public int? WrapUpTimeSec { get; set; }
    public string? PollingStrategy { get; set; }
    public string? RecordingMode { get; set; }
    public List<string> NotifyCodes { get; set; } = [];
    public bool? ResetStatisticsScheduleEnabled { get; set; }
    public QueueResetStatisticsScheduleDto? ResetQueueStatisticsSchedule { get; set; }
    public string? TranscriptionMode { get; set; }
    public string? TypeOfChatOwnershipType { get; set; }
    public QueueRouteDto? BreakRoute { get; set; }
    public QueueRouteDto? HolidaysRoute { get; set; }
    public QueueRouteDto? OutOfOfficeRoute { get; set; }
    public QueueDestinationDto? ForwardNoAnswer { get; set; }
}

public sealed class QueueDto
{
    public long Id { get; set; }
    public int PbxQueueId { get; set; }
    public string QueueNumber { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool? IsRegistered { get; set; }
    public QueueSettingsDto Settings { get; set; } = new();
    public List<QueueAgentAssignmentDto> Agents { get; set; } = [];
    public List<QueueManagerAssignmentDto> Managers { get; set; } = [];
}

public sealed class CreateQueueRequest
{
    public string Number { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public QueueSettingsDto Settings { get; set; } = new();
    public List<QueueAgentAssignmentDto> Agents { get; set; } = [];
    public List<QueueManagerAssignmentDto> Managers { get; set; } = [];
}

public sealed class UpdateQueueRequest
{
    public string? Name { get; set; }
    public QueueSettingsDto? Settings { get; set; }
    public List<QueueAgentAssignmentDto>? Agents { get; set; }
    public List<QueueManagerAssignmentDto>? Managers { get; set; }
    public bool ReplaceAgents { get; set; }
    public bool ReplaceManagers { get; set; }
}

public sealed class QueueWaitingCallLiveDto
{
    public string CallKey { get; set; } = string.Empty;
    public long? QueueCallId { get; set; }
    public int? PbxCallId { get; set; }
    public string? CallerNumber { get; set; }
    public string? CallerName { get; set; }
    public int WaitOrder { get; set; }
    public long? WaitingMs { get; set; }
    public bool EstimatedOrder { get; set; }
}

public sealed class QueueActiveCallLiveDto
{
    public string CallKey { get; set; } = string.Empty;
    public long? QueueCallId { get; set; }
    public int? PbxCallId { get; set; }
    public string Status { get; set; } = string.Empty;
    public long? AgentId { get; set; }
    public string? AgentExtension { get; set; }
    public long? TalkingMs { get; set; }
}

public sealed class QueueAgentLiveStatusDto
{
    public long AgentId { get; set; }
    public string ExtensionNumber { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string QueueStatus { get; set; } = string.Empty;
    public string ActivityType { get; set; } = string.Empty;
    public string? CurrentCallKey { get; set; }
    public DateTimeOffset AtUtc { get; set; }
}

public sealed class QueueStatsSummaryDto
{
    public long QueueId { get; set; }
    public DateTimeOffset AsOfUtc { get; set; }
    public int WaitingCount { get; set; }
    public int ActiveCount { get; set; }
    public int LoggedInAgents { get; set; }
    public int AvailableAgents { get; set; }
    public long? AverageWaitingMs { get; set; }
    public decimal? SlaPct { get; set; }
    public long AnsweredCount { get; set; }
    public long AbandonedCount { get; set; }
}

public sealed class QueueLiveSnapshotDto
{
    public long QueueId { get; set; }
    public DateTimeOffset AsOfUtc { get; set; }
    public long Version { get; set; }
    public List<QueueWaitingCallLiveDto> WaitingCalls { get; set; } = [];
    public List<QueueActiveCallLiveDto> ActiveCalls { get; set; } = [];
    public List<QueueAgentLiveStatusDto> AgentStatuses { get; set; } = [];
    public QueueStatsSummaryDto Stats { get; set; } = new();
}

public sealed class QueueInboundEventEnvelope
{
    public string Source { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public DateTimeOffset EventAtUtc { get; set; }
    public string OrderingKey { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = "{}";
}

public interface IQueueXapiClient
{
    Task<XapiODataCollectionResponse<XapiPbxQueueDto>> ListQueuesAsync(QueueODataQuery query, CancellationToken ct);
    Task<XapiPbxQueueDto?> GetQueueAsync(int queueId, IEnumerable<string>? select, IEnumerable<string>? expand, CancellationToken ct);
    Task<XapiPbxQueueDto> CreateQueueAsync(XapiPbxQueueDto request, CancellationToken ct);
    Task UpdateQueueAsync(int queueId, XapiPbxQueueDto request, CancellationToken ct);
    Task DeleteQueueAsync(int queueId, string? ifMatch, CancellationToken ct);

    Task<XapiODataCollectionResponse<XapiPbxQueueAgentDto>> ListQueueAgentsAsync(int queueId, QueueODataQuery query, CancellationToken ct);
    Task<XapiODataCollectionResponse<XapiPbxQueueManagerDto>> ListQueueManagersAsync(int queueId, QueueODataQuery query, CancellationToken ct);
    Task ResetQueueStatisticsAsync(int queueId, CancellationToken ct);

    Task<XapiODataCollectionResponse<XapiPbxActiveCallDto>> ListActiveCallsAsync(QueueODataQuery query, CancellationToken ct);
    Task<XapiODataCollectionResponse<XapiPbxCallHistoryViewDto>> ListCallHistoryViewAsync(QueueODataQuery query, CancellationToken ct);
    Task<XapiODataCollectionResponse<XapiPbxCallLogDataDto>> GetCallLogDataAsync(string relativeFunctionPath, QueueODataQuery query, CancellationToken ct);
}

public interface IQueueService
{
    Task<QueuePagedResult<QueueDto>> GetQueuesAsync(QueueListQuery query, CancellationToken ct);
    Task<QueueDto> GetQueueAsync(long queueId, CancellationToken ct);
    Task<QueueDto> CreateQueueAsync(CreateQueueRequest request, CancellationToken ct);
    Task<QueueDto> UpdateQueueAsync(long queueId, UpdateQueueRequest request, CancellationToken ct);
    Task DeleteQueueAsync(long queueId, CancellationToken ct);
}

public interface IQueueEventProcessor
{
    Task ProcessAsync(QueueInboundEventEnvelope envelope, CancellationToken ct);
    Task ProcessBatchAsync(IReadOnlyList<QueueInboundEventEnvelope> batch, CancellationToken ct);
}

public interface IQueueLiveStateService
{
    Task<QueueLiveSnapshotDto> GetSnapshotAsync(long queueId, CancellationToken ct);
    Task PublishSnapshotAsync(long queueId, CancellationToken ct);
}

public interface IQueueAnalyticsService
{
    Task<object> GetQueueAnalyticsAsync(long queueId, QueueAnalyticsQuery query, CancellationToken ct);
    Task<object> GetMultiQueueComparisonAsync(IReadOnlyList<long> queueIds, QueueAnalyticsQuery query, CancellationToken ct);
}
