namespace pbx_demo_backend.Domain.QueueManagement.Contracts;

// Batch 1 contract-freeze domain/persistence entity property definitions only.
// No business methods or behavior in this file.

public enum QueueCallLifecycleStatus
{
    Unknown = 0,
    EnteredQueue = 1,
    Waiting = 2,
    Ringing = 3,
    Answered = 4,
    Transferred = 5,
    Completed = 6,
    Missed = 7,
    Abandoned = 8
}

public enum QueueCallDisposition
{
    Unknown = 0,
    Answered = 1,
    Missed = 2,
    Abandoned = 3,
    Transferred = 4,
    Completed = 5
}

public enum QueueCallEventProcessingStatus
{
    Pending = 0,
    Processing = 1,
    Processed = 2,
    Failed = 3,
    DeadLetter = 4
}

public enum QueueAgentActivityType
{
    Login = 0,
    Logout = 1,
    Offer = 2,
    Answer = 3,
    Missed = 4,
    Transfer = 5,
    WrapUpStart = 6,
    WrapUpEnd = 7,
    TalkingStart = 8,
    TalkingEnd = 9,
    StatusChange = 10
}

public sealed class QueueEntity
{
    public long Id { get; set; }
    public int PbxQueueId { get; set; }
    public string QueueNumber { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool? IsRegistered { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset? LastXapiSyncAtUtc { get; set; }
    public byte[]? LastXapiHash { get; set; }
    public string? RawJson { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public QueueSettingsEntity? Settings { get; set; }
    public List<QueueAgentEntity> Agents { get; set; } = [];
    public List<QueueScheduleEntity> Schedules { get; set; } = [];
    public List<QueueWebhookMappingEntity> WebhookMappings { get; set; } = [];
}

public sealed class QueueSettingsEntity
{
    public long QueueId { get; set; }
    public QueueEntity Queue { get; set; } = null!;

    public bool? AgentAvailabilityMode { get; set; }
    public int? AnnouncementIntervalSec { get; set; }
    public bool? AnnounceQueuePosition { get; set; }
    public int? CallbackEnableTimeSec { get; set; }
    public string? CallbackPrefix { get; set; }
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
    public string? NotifyCodesJson { get; set; }
    public bool? ResetStatisticsScheduleEnabled { get; set; }
    public string? ResetStatsFrequency { get; set; }
    public string? ResetStatsDayOfWeek { get; set; }
    public string? ResetStatsTime { get; set; }
    public string? BreakRouteJson { get; set; }
    public string? HolidaysRouteJson { get; set; }
    public string? OutOfOfficeRouteJson { get; set; }
    public string? ForwardNoAnswerJson { get; set; }
    public string? TranscriptionMode { get; set; }
    public string? ChatOwnershipType { get; set; }
    public bool? CallUsEnableChat { get; set; }
    public bool? CallUsEnablePhone { get; set; }
    public bool? CallUsEnableVideo { get; set; }
    public string? CallUsRequirement { get; set; }
    public string? ClickToCallId { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ExtensionEntity
{
    public long Id { get; set; }
    public int PbxUserId { get; set; }
    public string ExtensionNumber { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? EmailAddress { get; set; }
    public bool? Enabled { get; set; }
    public bool? Internal { get; set; }
    public bool? IsRegistered { get; set; }
    public string? QueueStatus { get; set; }
    public string? RawJson { get; set; }
    public DateTimeOffset? LastXapiSyncAtUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public List<QueueAgentEntity> QueueMemberships { get; set; } = [];
}

public sealed class QueueAgentEntity
{
    public long Id { get; set; }
    public long QueueId { get; set; }
    public QueueEntity Queue { get; set; } = null!;
    public long ExtensionId { get; set; }
    public ExtensionEntity Extension { get; set; } = null!;

    public int? PbxAgentRefId { get; set; }
    public string AgentNumberSnapshot { get; set; } = string.Empty;
    public string? AgentNameSnapshot { get; set; }
    public string? SkillGroup { get; set; }
    public bool IsAgentMember { get; set; } = true;
    public bool IsQueueManager { get; set; }
    public string AssignmentSource { get; set; } = "XapiSync";
    public bool IsDeleted { get; set; }
    public DateTimeOffset? LastXapiSyncAtUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class QueueScheduleEntity
{
    public long Id { get; set; }
    public long QueueId { get; set; }
    public QueueEntity Queue { get; set; } = null!;

    public string ScheduleType { get; set; } = string.Empty;
    public string SourceSystem { get; set; } = "CRM";
    public string TimeZoneId { get; set; } = "UTC";
    public int? DayOfWeek { get; set; }
    public TimeSpan? StartLocalTime { get; set; }
    public TimeSpan? EndLocalTime { get; set; }
    public DateOnly? EffectiveFromDate { get; set; }
    public DateOnly? EffectiveToDate { get; set; }
    public bool IsEnabled { get; set; } = true;
    public int Priority { get; set; }
    public string? RuleJson { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class QueueWebhookMappingEntity
{
    public long Id { get; set; }
    public long QueueId { get; set; }
    public QueueEntity Queue { get; set; } = null!;

    public long? WebhookEndpointId { get; set; }
    public string? EndpointUrl { get; set; }
    public string? SecretRef { get; set; }
    public string EventTypesCsv { get; set; } = string.Empty;
    public string? FilterJson { get; set; }
    public string? RetryPolicyJson { get; set; }
    public bool IsEnabled { get; set; } = true;
    public DateTimeOffset? LastDeliveryAtUtc { get; set; }
    public DateTimeOffset? LastFailureAtUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class QueueCallEntity
{
    public long Id { get; set; }

    public long? QueueId { get; set; }
    public QueueEntity? Queue { get; set; }
    public long? AnsweredByExtensionId { get; set; }
    public ExtensionEntity? AnsweredByExtension { get; set; }
    public long? LastAgentExtensionId { get; set; }
    public ExtensionEntity? LastAgentExtension { get; set; }

    public int? PbxCallId { get; set; }
    public string? CdrId { get; set; }
    public string? CallHistoryId { get; set; }
    public string? MainCallHistoryId { get; set; }
    public int? CurrentSegmentId { get; set; }
    public string CorrelationKey { get; set; } = string.Empty;

    public string? CallerNumber { get; set; }
    public string? CallerName { get; set; }
    public string? CalleeNumber { get; set; }
    public string? CalleeName { get; set; }
    public string? Direction { get; set; }

    public QueueCallLifecycleStatus CurrentStatus { get; set; } = QueueCallLifecycleStatus.Unknown;
    public QueueCallDisposition Disposition { get; set; } = QueueCallDisposition.Unknown;
    public int? WaitOrder { get; set; }
    public int TransferCount { get; set; }
    public int? SlaThresholdSec { get; set; }
    public bool? SlaBreached { get; set; }

    public DateTimeOffset FirstSeenAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? QueuedAtUtc { get; set; }
    public DateTimeOffset? OfferedToAgentAtUtc { get; set; }
    public DateTimeOffset? AnsweredAtUtc { get; set; }
    public DateTimeOffset? EstablishedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public DateTimeOffset? AbandonedAtUtc { get; set; }
    public DateTimeOffset LastSeenAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public long? WaitingMs { get; set; }
    public long? RingingMs { get; set; }
    public long? TalkingMs { get; set; }
    public long? WrapUpMs { get; set; }
    public string? RawCurrentJson { get; set; }
    public long ProjectionVersion { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public List<QueueCallEventEntity> Events { get; set; } = [];
    public List<QueueCallHistoryEntity> HistoryRows { get; set; } = [];
}

public sealed class QueueCallEventEntity
{
    public long Id { get; set; }

    public long? QueueCallId { get; set; }
    public QueueCallEntity? QueueCall { get; set; }
    public long? QueueId { get; set; }
    public QueueEntity? Queue { get; set; }
    public long? ExtensionId { get; set; }
    public ExtensionEntity? Extension { get; set; }

    public string Source { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string? ExternalEventId { get; set; }
    public string OrderingKey { get; set; } = string.Empty;
    public long? SequenceNo { get; set; }
    public DateTimeOffset EventAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ObservedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public string IdempotencyKey { get; set; } = string.Empty;
    public byte[]? PayloadHash { get; set; }
    public string PayloadJson { get; set; } = "{}";
    public QueueCallEventProcessingStatus ProcessingStatus { get; set; } = QueueCallEventProcessingStatus.Pending;
    public int ProcessingAttemptCount { get; set; }
    public DateTimeOffset? LastAttemptAtUtc { get; set; }
    public DateTimeOffset? NextAttemptAtUtc { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class QueueCallHistoryEntity
{
    public long Id { get; set; }

    public long? QueueCallId { get; set; }
    public QueueCallEntity? QueueCall { get; set; }
    public long? QueueId { get; set; }
    public QueueEntity? Queue { get; set; }

    public string SourceRecordType { get; set; } = string.Empty;
    public int? PbxCallId { get; set; }
    public string? CdrId { get; set; }
    public string? CallHistoryId { get; set; }
    public string? MainCallHistoryId { get; set; }
    public int? SegmentId { get; set; }
    public int? SegmentType { get; set; }
    public int? SegmentActionId { get; set; }
    public DateTimeOffset? SegmentStartAtUtc { get; set; }
    public DateTimeOffset? SegmentEndAtUtc { get; set; }
    public bool? CallAnswered { get; set; }
    public long? CallTimeMs { get; set; }
    public long? RingingDurationMs { get; set; }
    public long? TalkingDurationMs { get; set; }
    public string? Direction { get; set; }
    public string? Status { get; set; }
    public string? Reason { get; set; }
    public string? CallType { get; set; }
    public string? SourceDn { get; set; }
    public string? SourceDisplayName { get; set; }
    public string? SourceCallerId { get; set; }
    public int? SourceType { get; set; }
    public string? DestinationDn { get; set; }
    public string? DestinationDisplayName { get; set; }
    public string? DestinationCallerId { get; set; }
    public int? DestinationType { get; set; }
    public string? ActionDn { get; set; }
    public string? ActionDnDisplayName { get; set; }
    public string? ActionDnCallerId { get; set; }
    public int? ActionDnType { get; set; }
    public decimal? CallCost { get; set; }
    public string? RecordingUrl { get; set; }
    public string? Transcription { get; set; }
    public int? SentimentScore { get; set; }
    public string RawJson { get; set; } = "{}";
    public DateTimeOffset ImportedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class QueueAgentActivityEntity
{
    public long Id { get; set; }

    public long? QueueId { get; set; }
    public QueueEntity? Queue { get; set; }
    public long ExtensionId { get; set; }
    public ExtensionEntity Extension { get; set; } = null!;
    public long? QueueCallId { get; set; }
    public QueueCallEntity? QueueCall { get; set; }

    public QueueAgentActivityType ActivityType { get; set; }
    public string? ActivityStatus { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public long? DurationMs { get; set; }
    public string Source { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public string? RawJson { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class QueueWaitingSnapshotEntity
{
    public long Id { get; set; }

    public long QueueId { get; set; }
    public QueueEntity Queue { get; set; } = null!;
    public Guid SnapshotKey { get; set; }
    public DateTimeOffset CapturedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public long? QueueCallId { get; set; }
    public QueueCallEntity? QueueCall { get; set; }
    public int? PbxCallId { get; set; }
    public string? CorrelationKey { get; set; }
    public int WaitOrder { get; set; }
    public long? WaitingMs { get; set; }
    public string? CallerNumber { get; set; }
    public string? CallerName { get; set; }
    public bool EstimatedOrder { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class QueueAnalyticsBucketHourEntity
{
    public long Id { get; set; }
    public long QueueId { get; set; }
    public DateTimeOffset BucketStartUtc { get; set; }
    public string TimeZoneId { get; set; } = "UTC";
    public long TotalCalls { get; set; }
    public long AnsweredCalls { get; set; }
    public long AbandonedCalls { get; set; }
    public long MissedCalls { get; set; }
    public long WaitingMsSum { get; set; }
    public long WaitingMsCount { get; set; }
    public long TalkingMsSum { get; set; }
    public long TalkingMsCount { get; set; }
    public long SlaEligibleCalls { get; set; }
    public long SlaWithinThresholdCalls { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class QueueAnalyticsBucketDayEntity
{
    public long Id { get; set; }
    public long QueueId { get; set; }
    public DateOnly BucketDate { get; set; }
    public string TimeZoneId { get; set; } = "UTC";
    public long TotalCalls { get; set; }
    public long AnsweredCalls { get; set; }
    public long AbandonedCalls { get; set; }
    public long MissedCalls { get; set; }
    public long WaitingMsSum { get; set; }
    public long WaitingMsCount { get; set; }
    public long TalkingMsSum { get; set; }
    public long TalkingMsCount { get; set; }
    public long SlaEligibleCalls { get; set; }
    public long SlaWithinThresholdCalls { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class OutboxMessageEntity
{
    public long Id { get; set; }
    public string Topic { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = "{}";
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? PublishedAtUtc { get; set; }
    public int AttemptCount { get; set; }
    public string? LastError { get; set; }
}

public sealed class XapiSyncCheckpointEntity
{
    public long Id { get; set; }
    public string StreamName { get; set; } = string.Empty;
    public string PartitionKey { get; set; } = string.Empty;
    public string? CursorValue { get; set; }
    public DateTimeOffset? LastSuccessfulSyncAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
