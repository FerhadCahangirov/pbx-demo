using System.Text.Json.Serialization;

namespace CallControl.Api.Domain;

public sealed class CrmUserDepartmentRoleRequest
{
    [JsonPropertyName("appDepartmentId")]
    public int AppDepartmentId { get; set; }

    [JsonPropertyName("roleName")]
    public string RoleName { get; set; } = "users";
}

public sealed class CrmCreateUserRequest
{
    [JsonPropertyName("username")]
    public string Username { get; set; } = string.Empty;

    [JsonPropertyName("password")]
    public string Password { get; set; } = string.Empty;

    [JsonPropertyName("firstName")]
    public string FirstName { get; set; } = string.Empty;

    [JsonPropertyName("lastName")]
    public string LastName { get; set; } = string.Empty;

    [JsonPropertyName("emailAddress")]
    public string EmailAddress { get; set; } = string.Empty;

    [JsonPropertyName("ownedExtension")]
    public string OwnedExtension { get; set; } = string.Empty;

    [JsonPropertyName("controlDn")]
    public string? ControlDn { get; set; }

    [JsonPropertyName("role")]
    public AppUserRole Role { get; set; } = AppUserRole.User;

    [JsonPropertyName("language")]
    public string Language { get; set; } = "EN";

    [JsonPropertyName("promptSet")]
    public string? PromptSet { get; set; }

    [JsonPropertyName("vmEmailOptions")]
    public string VmEmailOptions { get; set; } = "Notification";

    [JsonPropertyName("sendEmailMissedCalls")]
    public bool SendEmailMissedCalls { get; set; } = true;

    [JsonPropertyName("require2Fa")]
    public bool Require2Fa { get; set; }

    [JsonPropertyName("callUsEnableChat")]
    public bool CallUsEnableChat { get; set; } = true;

    [JsonPropertyName("clickToCallId")]
    public string? ClickToCallId { get; set; }

    [JsonPropertyName("webMeetingFriendlyName")]
    public string? WebMeetingFriendlyName { get; set; }

    [JsonPropertyName("sipUsername")]
    public string? SipUsername { get; set; }

    [JsonPropertyName("sipAuthId")]
    public string? SipAuthId { get; set; }

    [JsonPropertyName("sipPassword")]
    public string? SipPassword { get; set; }

    [JsonPropertyName("sipDisplayName")]
    public string? SipDisplayName { get; set; }

    [JsonPropertyName("threeCxAccessPassword")]
    public string? ThreeCxAccessPassword { get; set; }

    [JsonPropertyName("departmentRoles")]
    public List<CrmUserDepartmentRoleRequest> DepartmentRoles { get; set; } = [];
}

public sealed class CrmUpdateUserRequest
{
    [JsonPropertyName("firstName")]
    public string FirstName { get; set; } = string.Empty;

    [JsonPropertyName("lastName")]
    public string LastName { get; set; } = string.Empty;

    [JsonPropertyName("emailAddress")]
    public string EmailAddress { get; set; } = string.Empty;

    [JsonPropertyName("ownedExtension")]
    public string OwnedExtension { get; set; } = string.Empty;

    [JsonPropertyName("controlDn")]
    public string? ControlDn { get; set; }

    [JsonPropertyName("role")]
    public AppUserRole Role { get; set; } = AppUserRole.User;

    [JsonPropertyName("language")]
    public string Language { get; set; } = "EN";

    [JsonPropertyName("promptSet")]
    public string? PromptSet { get; set; }

    [JsonPropertyName("vmEmailOptions")]
    public string VmEmailOptions { get; set; } = "Notification";

    [JsonPropertyName("sendEmailMissedCalls")]
    public bool SendEmailMissedCalls { get; set; } = true;

    [JsonPropertyName("require2Fa")]
    public bool Require2Fa { get; set; }

    [JsonPropertyName("callUsEnableChat")]
    public bool CallUsEnableChat { get; set; } = true;

    [JsonPropertyName("clickToCallId")]
    public string? ClickToCallId { get; set; }

    [JsonPropertyName("webMeetingFriendlyName")]
    public string? WebMeetingFriendlyName { get; set; }

    [JsonPropertyName("sipUsername")]
    public string? SipUsername { get; set; }

    [JsonPropertyName("sipAuthId")]
    public string? SipAuthId { get; set; }

    [JsonPropertyName("sipPassword")]
    public string? SipPassword { get; set; }

    [JsonPropertyName("sipDisplayName")]
    public string? SipDisplayName { get; set; }

    [JsonPropertyName("isActive")]
    public bool IsActive { get; set; } = true;

    [JsonPropertyName("newPassword")]
    public string? NewPassword { get; set; }

    [JsonPropertyName("departmentRoles")]
    public List<CrmUserDepartmentRoleRequest> DepartmentRoles { get; set; } = [];
}

public sealed class CrmDepartmentPropsDto
{
    [JsonPropertyName("liveChatMaxCount")]
    public int LiveChatMaxCount { get; set; } = 20;

    [JsonPropertyName("personalContactsMaxCount")]
    public int PersonalContactsMaxCount { get; set; } = 500;

    [JsonPropertyName("promptsMaxCount")]
    public int PromptsMaxCount { get; set; } = 10;

    [JsonPropertyName("sbcMaxCount")]
    public int SbcMaxCount { get; set; } = 20;

    [JsonPropertyName("systemNumberFrom")]
    public string? SystemNumberFrom { get; set; }

    [JsonPropertyName("systemNumberTo")]
    public string? SystemNumberTo { get; set; }

    [JsonPropertyName("trunkNumberFrom")]
    public string? TrunkNumberFrom { get; set; }

    [JsonPropertyName("trunkNumberTo")]
    public string? TrunkNumberTo { get; set; }

    [JsonPropertyName("userNumberFrom")]
    public string? UserNumberFrom { get; set; }

    [JsonPropertyName("userNumberTo")]
    public string? UserNumberTo { get; set; }
}

public sealed class CrmDepartmentRouteTargetDto
{
    [JsonPropertyName("to")]
    public string To { get; set; } = "VoiceMail";

    [JsonPropertyName("number")]
    public string Number { get; set; } = string.Empty;

    [JsonPropertyName("external")]
    public string External { get; set; } = string.Empty;
}

public sealed class CrmDepartmentRouteDto
{
    [JsonPropertyName("isPromptEnabled")]
    public bool IsPromptEnabled { get; set; }

    [JsonPropertyName("route")]
    public CrmDepartmentRouteTargetDto Route { get; set; } = new();
}

public sealed class CrmDepartmentRoutingDto
{
    [JsonPropertyName("officeRoute")]
    public CrmDepartmentRouteDto OfficeRoute { get; set; } = new();

    [JsonPropertyName("outOfOfficeRoute")]
    public CrmDepartmentRouteDto OutOfOfficeRoute { get; set; } = new();

    [JsonPropertyName("breakRoute")]
    public CrmDepartmentRouteDto BreakRoute { get; set; } = new();

    [JsonPropertyName("holidaysRoute")]
    public CrmDepartmentRouteDto HolidaysRoute { get; set; } = new();
}

public sealed class CrmCreateDepartmentRequest
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("language")]
    public string Language { get; set; } = "EN";

    [JsonPropertyName("timeZoneId")]
    public string TimeZoneId { get; set; } = "51";

    [JsonPropertyName("promptSet")]
    public string? PromptSet { get; set; }

    [JsonPropertyName("disableCustomPrompt")]
    public bool DisableCustomPrompt { get; set; } = true;

    [JsonPropertyName("allowCallService")]
    public bool AllowCallService { get; set; } = true;

    [JsonPropertyName("props")]
    public CrmDepartmentPropsDto Props { get; set; } = new();

    [JsonPropertyName("liveChatLink")]
    public string? LiveChatLink { get; set; }

    [JsonPropertyName("liveChatWebsite")]
    public string? LiveChatWebsite { get; set; }

    [JsonPropertyName("routing")]
    public CrmDepartmentRoutingDto? Routing { get; set; }
}

public sealed class CrmUpdateDepartmentRequest
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("language")]
    public string Language { get; set; } = "EN";

    [JsonPropertyName("timeZoneId")]
    public string TimeZoneId { get; set; } = "51";

    [JsonPropertyName("promptSet")]
    public string? PromptSet { get; set; }

    [JsonPropertyName("disableCustomPrompt")]
    public bool DisableCustomPrompt { get; set; } = true;

    [JsonPropertyName("allowCallService")]
    public bool AllowCallService { get; set; } = true;

    [JsonPropertyName("props")]
    public CrmDepartmentPropsDto Props { get; set; } = new();

    [JsonPropertyName("liveChatLink")]
    public string? LiveChatLink { get; set; }

    [JsonPropertyName("liveChatWebsite")]
    public string? LiveChatWebsite { get; set; }

    [JsonPropertyName("routing")]
    public CrmDepartmentRoutingDto? Routing { get; set; }
}

public sealed class CrmDepartmentRoleResponse
{
    [JsonPropertyName("appDepartmentId")]
    public int AppDepartmentId { get; set; }

    [JsonPropertyName("threeCxGroupId")]
    public int ThreeCxGroupId { get; set; }

    [JsonPropertyName("departmentName")]
    public string DepartmentName { get; set; } = string.Empty;

    [JsonPropertyName("roleName")]
    public string RoleName { get; set; } = string.Empty;
}

public sealed class CrmUserResponse
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("username")]
    public string Username { get; set; } = string.Empty;

    [JsonPropertyName("firstName")]
    public string FirstName { get; set; } = string.Empty;

    [JsonPropertyName("lastName")]
    public string LastName { get; set; } = string.Empty;

    [JsonPropertyName("emailAddress")]
    public string EmailAddress { get; set; } = string.Empty;

    [JsonPropertyName("ownedExtension")]
    public string OwnedExtension { get; set; } = string.Empty;

    [JsonPropertyName("controlDn")]
    public string? ControlDn { get; set; }

    [JsonPropertyName("role")]
    public AppUserRole Role { get; set; }

    [JsonPropertyName("language")]
    public string Language { get; set; } = "EN";

    [JsonPropertyName("promptSet")]
    public string? PromptSet { get; set; }

    [JsonPropertyName("vmEmailOptions")]
    public string VmEmailOptions { get; set; } = "Notification";

    [JsonPropertyName("sendEmailMissedCalls")]
    public bool SendEmailMissedCalls { get; set; }

    [JsonPropertyName("require2Fa")]
    public bool Require2Fa { get; set; }

    [JsonPropertyName("callUsEnableChat")]
    public bool CallUsEnableChat { get; set; }

    [JsonPropertyName("clickToCallId")]
    public string? ClickToCallId { get; set; }

    [JsonPropertyName("webMeetingFriendlyName")]
    public string? WebMeetingFriendlyName { get; set; }

    [JsonPropertyName("threeCxUserId")]
    public int? ThreeCxUserId { get; set; }

    [JsonPropertyName("isActive")]
    public bool IsActive { get; set; }

    [JsonPropertyName("createdAtUtc")]
    public DateTimeOffset CreatedAtUtc { get; set; }

    [JsonPropertyName("updatedAtUtc")]
    public DateTimeOffset UpdatedAtUtc { get; set; }

    [JsonPropertyName("departmentRoles")]
    public List<CrmDepartmentRoleResponse> DepartmentRoles { get; set; } = [];
}

public sealed class CrmDepartmentResponse
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("threeCxGroupId")]
    public int ThreeCxGroupId { get; set; }

    [JsonPropertyName("threeCxGroupNumber")]
    public string? ThreeCxGroupNumber { get; set; }

    [JsonPropertyName("language")]
    public string Language { get; set; } = "EN";

    [JsonPropertyName("timeZoneId")]
    public string TimeZoneId { get; set; } = "51";

    [JsonPropertyName("promptSet")]
    public string? PromptSet { get; set; }

    [JsonPropertyName("disableCustomPrompt")]
    public bool DisableCustomPrompt { get; set; } = true;

    [JsonPropertyName("props")]
    public CrmDepartmentPropsDto Props { get; set; } = new();

    [JsonPropertyName("routing")]
    public CrmDepartmentRoutingDto? Routing { get; set; }

    [JsonPropertyName("liveChatLink")]
    public string? LiveChatLink { get; set; }

    [JsonPropertyName("liveChatWebsite")]
    public string? LiveChatWebsite { get; set; }

    [JsonPropertyName("threeCxWebsiteLinkId")]
    public int? ThreeCxWebsiteLinkId { get; set; }

    [JsonPropertyName("createdAtUtc")]
    public DateTimeOffset CreatedAtUtc { get; set; }

    [JsonPropertyName("updatedAtUtc")]
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public sealed class CrmValidateFriendlyNameRequest
{
    [JsonPropertyName("friendlyName")]
    public string FriendlyName { get; set; } = string.Empty;

    [JsonPropertyName("pair")]
    public string Pair { get; set; } = string.Empty;
}

public sealed class CrmUpdateFriendlyNameRequest
{
    [JsonPropertyName("callUsEnableChat")]
    public bool CallUsEnableChat { get; set; } = true;

    [JsonPropertyName("clickToCallId")]
    public string ClickToCallId { get; set; } = string.Empty;

    [JsonPropertyName("webMeetingFriendlyName")]
    public string WebMeetingFriendlyName { get; set; } = string.Empty;
}

public sealed class CrmCreateSharedParkingRequest
{
    [JsonPropertyName("groupIds")]
    public List<int> GroupIds { get; set; } = [];
}

public sealed class CrmSharedParkingResponse
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("number")]
    public string Number { get; set; } = string.Empty;
}

public sealed class CrmVersionResponse
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;
}

public sealed class CrmCallStatusHistoryItemResponse
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("eventType")]
    public string EventType { get; set; } = string.Empty;

    [JsonPropertyName("eventReason")]
    public string? EventReason { get; set; }

    [JsonPropertyName("occurredAtUtc")]
    public DateTimeOffset OccurredAtUtc { get; set; }
}

public sealed class CrmCallHistoryItemResponse
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;

    [JsonPropertyName("operatorUserId")]
    public int OperatorUserId { get; set; }

    [JsonPropertyName("operatorUsername")]
    public string OperatorUsername { get; set; } = string.Empty;

    [JsonPropertyName("operatorDisplayName")]
    public string OperatorDisplayName { get; set; } = string.Empty;

    [JsonPropertyName("operatorExtension")]
    public string OperatorExtension { get; set; } = string.Empty;

    [JsonPropertyName("trackingKey")]
    public string TrackingKey { get; set; } = string.Empty;

    [JsonPropertyName("callScopeId")]
    public string? CallScopeId { get; set; }

    [JsonPropertyName("participantId")]
    public long? ParticipantId { get; set; }

    [JsonPropertyName("pbxCallId")]
    public long? PbxCallId { get; set; }

    [JsonPropertyName("pbxLegId")]
    public long? PbxLegId { get; set; }

    [JsonPropertyName("direction")]
    public string Direction { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("remoteParty")]
    public string? RemoteParty { get; set; }

    [JsonPropertyName("remoteName")]
    public string? RemoteName { get; set; }

    [JsonPropertyName("endReason")]
    public string? EndReason { get; set; }

    [JsonPropertyName("startedAtUtc")]
    public DateTimeOffset StartedAtUtc { get; set; }

    [JsonPropertyName("answeredAtUtc")]
    public DateTimeOffset? AnsweredAtUtc { get; set; }

    [JsonPropertyName("endedAtUtc")]
    public DateTimeOffset? EndedAtUtc { get; set; }

    [JsonPropertyName("isActive")]
    public bool IsActive { get; set; }

    [JsonPropertyName("talkDurationSeconds")]
    public long? TalkDurationSeconds { get; set; }

    [JsonPropertyName("totalDurationSeconds")]
    public long? TotalDurationSeconds { get; set; }

    [JsonPropertyName("statusHistory")]
    public List<CrmCallStatusHistoryItemResponse> StatusHistory { get; set; } = [];
}

public sealed class CrmCallHistoryResponse
{
    [JsonPropertyName("totalCount")]
    public int TotalCount { get; set; }

    [JsonPropertyName("take")]
    public int Take { get; set; }

    [JsonPropertyName("skip")]
    public int Skip { get; set; }

    [JsonPropertyName("items")]
    public List<CrmCallHistoryItemResponse> Items { get; set; } = [];
}

public sealed class CrmOperatorCallKpiResponse
{
    [JsonPropertyName("operatorUserId")]
    public int OperatorUserId { get; set; }

    [JsonPropertyName("operatorUsername")]
    public string OperatorUsername { get; set; } = string.Empty;

    [JsonPropertyName("operatorDisplayName")]
    public string OperatorDisplayName { get; set; } = string.Empty;

    [JsonPropertyName("operatorExtension")]
    public string OperatorExtension { get; set; } = string.Empty;

    [JsonPropertyName("totalCalls")]
    public int TotalCalls { get; set; }

    [JsonPropertyName("activeCalls")]
    public int ActiveCalls { get; set; }

    [JsonPropertyName("answeredCalls")]
    public int AnsweredCalls { get; set; }

    [JsonPropertyName("missedCalls")]
    public int MissedCalls { get; set; }

    [JsonPropertyName("failedCalls")]
    public int FailedCalls { get; set; }

    [JsonPropertyName("totalTalkSeconds")]
    public long TotalTalkSeconds { get; set; }

    [JsonPropertyName("averageTalkSeconds")]
    public double AverageTalkSeconds { get; set; }

    [JsonPropertyName("lastCallAtUtc")]
    public DateTimeOffset? LastCallAtUtc { get; set; }
}

public sealed class CrmCallAnalyticsResponse
{
    [JsonPropertyName("periodStartUtc")]
    public DateTimeOffset PeriodStartUtc { get; set; }

    [JsonPropertyName("periodEndUtc")]
    public DateTimeOffset PeriodEndUtc { get; set; }

    [JsonPropertyName("generatedAtUtc")]
    public DateTimeOffset GeneratedAtUtc { get; set; }

    [JsonPropertyName("totalCalls")]
    public int TotalCalls { get; set; }

    [JsonPropertyName("activeCalls")]
    public int ActiveCalls { get; set; }

    [JsonPropertyName("answeredCalls")]
    public int AnsweredCalls { get; set; }

    [JsonPropertyName("missedCalls")]
    public int MissedCalls { get; set; }

    [JsonPropertyName("failedCalls")]
    public int FailedCalls { get; set; }

    [JsonPropertyName("totalTalkSeconds")]
    public long TotalTalkSeconds { get; set; }

    [JsonPropertyName("averageTalkSeconds")]
    public double AverageTalkSeconds { get; set; }

    [JsonPropertyName("totalOperators")]
    public int TotalOperators { get; set; }

    [JsonPropertyName("activeOperators")]
    public int ActiveOperators { get; set; }

    [JsonPropertyName("operatorKpis")]
    public List<CrmOperatorCallKpiResponse> OperatorKpis { get; set; } = [];
}
