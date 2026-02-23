using System.Text.Json.Serialization;

namespace pbx_demo_backend.Domain.QueueManagement.Contracts;

// Batch 1 contract-freeze models for documented 3CX XAPI queue-related schemas.
// These are external DTO contracts (XAPI-facing), not domain entities.

public sealed class XapiODataCollectionResponse<TItem>
{
    [JsonPropertyName("@odata.count")]
    public int? ODataCount { get; set; }

    [JsonPropertyName("value")]
    public List<TItem> Value { get; set; } = [];
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum XapiPbxResetQueueStatisticsFrequency
{
    Disabled,
    Daily,
    Weekly,
    Monthly
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum XapiPbxQueueStatusType
{
    LoggedOut,
    LoggedIn
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum XapiPbxQueueRecording
{
    Disabled,
    AllowToOptOut,
    AskToOptIn
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum XapiPbxQueueNotifyCode
{
    Callback,
    CallbackFail,
    SLATimeBreached,
    CallLost
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum XapiPbxPollingStrategyType
{
    Hunt,
    RingAll,
    HuntRandomStart,
    NextAgent,
    LongestWaiting,
    LeastTalkTime,
    FewestAnswered,
    HuntBy3s,
    First3Available,
    SkillBasedRouting_RingAll,
    SkillBasedRouting_HuntRandomStart,
    SkillBasedRouting_RoundRobin,
    SkillBasedRouting_FewestAnswered
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum XapiPbxAuthentication
{
    Both,
    Name,
    Email,
    None
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum XapiPbxDayOfWeek
{
    Sunday,
    Monday,
    Tuesday,
    Wednesday,
    Thursday,
    Friday,
    Saturday
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum XapiPbxTranscriptionType
{
    Nothing,
    Voicemail,
    Recordings,
    Both,
    Inherit
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum XapiPbxTypeOfChatOwnershipType
{
    TakeManually,
    AutoAssign
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum XapiPbxDestinationType
{
    None,
    VoiceMail,
    Extension,
    Queue,
    RingGroup,
    IVR,
    External,
    Fax,
    Boomerang,
    Deflect,
    VoiceMailOfDestination,
    Callback,
    RoutePoint,
    ProceedWithNoExceptions
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum XapiPbxPeerType
{
    None,
    Extension,
    Queue,
    RingGroup,
    IVR,
    Fax,
    Conference,
    SpecialMenu,
    Parking,
    ExternalLine,
    Group,
    RoutePoint
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum XapiPbxUserTag
{
    MS,
    Teams,
    Google,
    WakeUp,
    FaxServer,
    Principal,
    WeakID,
    WeakPass,
    WM
}

public sealed class XapiPbxFirstAvailableNumberDto
{
    public string? Number { get; set; }
}

public sealed class XapiPbxQueueAgentDto
{
    public int? Id { get; set; }
    public string? Name { get; set; }
    public string Number { get; set; } = string.Empty;
    public string? SkillGroup { get; set; }
}

public sealed class XapiPbxQueueManagerDto
{
    public int? Id { get; set; }
    public string? Name { get; set; }
    public string Number { get; set; } = string.Empty;
}

public sealed class XapiPbxUserGroupProjectionDto
{
    public int? Id { get; set; }
    public string? Name { get; set; }
    public string? Number { get; set; }
    public List<XapiPbxUserTag>? Tags { get; set; }
}

public sealed class XapiPbxDestinationDto
{
    public string? External { get; set; }
    public string? Name { get; set; }
    public string? Number { get; set; }
    public List<XapiPbxUserTag>? Tags { get; set; }
    public XapiPbxDestinationType? To { get; set; }
    public XapiPbxPeerType? Type { get; set; }
}

public sealed class XapiPbxRouteDto
{
    public bool? IsPromptEnabled { get; set; }
    public string? Prompt { get; set; }
    public XapiPbxDestinationDto? Route { get; set; }
}

public sealed class XapiPbxResetQueueStatisticsScheduleDto
{
    public XapiPbxDayOfWeek? Day { get; set; }
    public XapiPbxResetQueueStatisticsFrequency? Frequency { get; set; }
    public string? Time { get; set; } // OpenAPI format=time
}

public sealed class XapiPbxQueueDto
{
    public bool? AgentAvailabilityMode { get; set; }
    public List<XapiPbxQueueAgentDto>? Agents { get; set; }
    public int? AnnouncementInterval { get; set; }
    public bool? AnnounceQueuePosition { get; set; }
    public XapiPbxRouteDto? BreakRoute { get; set; }
    public int? CallbackEnableTime { get; set; }
    public string? CallbackPrefix { get; set; }
    public bool? CallUsEnableChat { get; set; }
    public bool? CallUsEnablePhone { get; set; }
    public bool? CallUsEnableVideo { get; set; }
    public XapiPbxAuthentication? CallUsRequirement { get; set; }
    public string? ClickToCallId { get; set; }
    public bool? EnableIntro { get; set; }
    public XapiPbxDestinationDto? ForwardNoAnswer { get; set; }
    public string? GreetingFile { get; set; }
    public List<XapiPbxUserGroupProjectionDto>? Groups { get; set; }
    public XapiPbxRouteDto? HolidaysRoute { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int Id { get; set; }
    public string? IntroFile { get; set; }
    public bool? IsRegistered { get; set; }
    public List<XapiPbxQueueManagerDto>? Managers { get; set; }
    public int? MasterTimeout { get; set; }
    public int? MaxCallersInQueue { get; set; }
    public string? Name { get; set; }
    public List<XapiPbxQueueNotifyCode>? NotifyCodes { get; set; }
    public string? Number { get; set; }
    public string? OnHoldFile { get; set; }
    public XapiPbxRouteDto? OutOfOfficeRoute { get; set; }
    public bool? PlayFullPrompt { get; set; }
    public XapiPbxPollingStrategyType? PollingStrategy { get; set; }
    public bool? PriorityQueue { get; set; }
    public string? PromptSet { get; set; }
    public XapiPbxQueueRecording? Recording { get; set; }
    public XapiPbxResetQueueStatisticsScheduleDto? ResetQueueStatisticsSchedule { get; set; }
    public bool? ResetStatisticsScheduleEnabled { get; set; }
    public int? RingTimeout { get; set; }
    public int? SLATime { get; set; }
    public XapiPbxTranscriptionType? TranscriptionMode { get; set; }
    public XapiPbxTypeOfChatOwnershipType? TypeOfChatOwnershipType { get; set; }
    public int? WrapUpTime { get; set; }
}

public sealed class XapiPbxActiveCallDto
{
    public string? Callee { get; set; }
    public string? Caller { get; set; }
    public DateTimeOffset? EstablishedAt { get; set; }
    public int Id { get; set; }
    public DateTimeOffset? LastChangeStatus { get; set; }
    public DateTimeOffset? ServerNow { get; set; }
    public string? Status { get; set; }
}

public sealed class XapiPbxCallHistoryViewDto
{
    public bool? CallAnswered { get; set; }
    public string CallTime { get; set; } = string.Empty; // OpenAPI format=duration
    public string? DstCallerNumber { get; set; }
    public string? DstDisplayName { get; set; }
    public string? DstDn { get; set; }
    public int DstDnType { get; set; }
    public string? DstExtendedDisplayName { get; set; }
    public bool DstExternal { get; set; }
    public int DstId { get; set; }
    public bool DstInternal { get; set; }
    public int DstParticipantId { get; set; }
    public int? DstRecId { get; set; }
    public int SegmentActionId { get; set; }
    public DateTimeOffset SegmentEndTime { get; set; }
    public int SegmentId { get; set; }
    public DateTimeOffset SegmentStartTime { get; set; }
    public int SegmentType { get; set; }
    public string? SrcCallerNumber { get; set; }
    public string? SrcDisplayName { get; set; }
    public string? SrcDn { get; set; }
    public int SrcDnType { get; set; }
    public string? SrcExtendedDisplayName { get; set; }
    public bool SrcExternal { get; set; }
    public int SrcId { get; set; }
    public bool SrcInternal { get; set; }
    public int SrcParticipantId { get; set; }
    public int? SrcRecId { get; set; }
}

public sealed class XapiPbxCallLogDataDto
{
    public string? ActionDnCallerId { get; set; }
    public string? ActionDnDisplayName { get; set; }

    [JsonPropertyName("actionDnDn")]
    public string? ActionDnDn { get; set; }

    public int? ActionDnType { get; set; }
    public int? ActionType { get; set; }
    public bool? Answered { get; set; }
    public decimal? CallCost { get; set; }
    public string? CallHistoryId { get; set; }
    public int CallId { get; set; }
    public string? CallType { get; set; }
    public string CdrId { get; set; } = string.Empty;
    public string? DestinationCallerId { get; set; }
    public string? DestinationDisplayName { get; set; }
    public string? DestinationDn { get; set; }
    public int? DestinationType { get; set; }
    public string? Direction { get; set; }
    public int? DstRecId { get; set; }
    public int? Indent { get; set; }
    public string? MainCallHistoryId { get; set; }
    public bool? QualityReport { get; set; }
    public string? Reason { get; set; }
    public string? RecordingUrl { get; set; }
    public string? RingingDuration { get; set; } // OpenAPI format=duration
    public int? SegmentId { get; set; }
    public int? SentimentScore { get; set; }
    public string? SourceCallerId { get; set; }
    public string? SourceDisplayName { get; set; }
    public string? SourceDn { get; set; }
    public int? SourceType { get; set; }
    public int? SrcRecId { get; set; }
    public DateTimeOffset? StartTime { get; set; }
    public string? Status { get; set; }
    public int? SubrowDescNumber { get; set; }
    public string? Summary { get; set; }
    public string? TalkingDuration { get; set; } // OpenAPI format=duration
    public string? Transcription { get; set; }
}

// Queue module uses a projected subset of Pbx.User via OData $select.
public sealed class XapiPbxUserQueueProjectionDto
{
    public int Id { get; set; }
    public string? Number { get; set; }
    public string? DisplayName { get; set; }
    public string? EmailAddress { get; set; }
    public bool? Enabled { get; set; }
    public bool? Internal { get; set; }
    public bool? IsRegistered { get; set; }
    public XapiPbxQueueStatusType? QueueStatus { get; set; }
}

public sealed class XapiPbxActivityLogEventDto
{
    public long Index { get; set; }
    public string? Message { get; set; }
    public DateTimeOffset? TimeStamp { get; set; }
}

public sealed class XapiPbxEventLogDto
{
    public int? EventId { get; set; }
    public string? Group { get; set; }
    public string? GroupName { get; set; }
    public int Id { get; set; }
    public string? Message { get; set; }
    public List<string>? Params { get; set; }
    public string? Source { get; set; }
    public DateTimeOffset? TimeGenerated { get; set; }
    public string? Type { get; set; }
}

public sealed class XapiPbxExtensionFilterDto
{
    public List<string?>? CallIds { get; set; }
    public string? Number { get; set; }
}

public sealed class XapiPbxActivityLogsFilterDto
{
    public List<XapiPbxExtensionFilterDto>? Extensions { get; set; }
}
