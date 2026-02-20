using System.Text.Json.Serialization;

namespace CallControl.Api.Domain;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AppUserRole
{
    User = 0,
    Supervisor = 1
}

public static class AppUserRoles
{
    public const string User = nameof(AppUserRole.User);
    public const string Supervisor = nameof(AppUserRole.Supervisor);
}

public sealed class AppUserEntity
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string EmailAddress { get; set; } = string.Empty;
    public string OwnedExtension { get; set; } = string.Empty;
    public string? ControlDn { get; set; }
    public AppUserRole Role { get; set; } = AppUserRole.User;
    public string Language { get; set; } = "EN";
    public string? PromptSet { get; set; }
    public string VmEmailOptions { get; set; } = "Notification";
    public bool SendEmailMissedCalls { get; set; } = true;
    public bool Require2Fa { get; set; }
    public bool CallUsEnableChat { get; set; } = true;
    public string? ClickToCallId { get; set; }
    public string? WebMeetingFriendlyName { get; set; }
    public string? SipUsername { get; set; }
    public string? SipAuthId { get; set; }
    public string? SipPassword { get; set; }
    public string? SipDisplayName { get; set; }
    public int? ThreeCxUserId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public List<AppDepartmentMembershipEntity> DepartmentMemberships { get; set; } = [];
    public List<AppCallCdrEntity> CallCdrs { get; set; } = [];
}

public sealed class AppDepartmentEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int ThreeCxGroupId { get; set; }
    public string? ThreeCxGroupNumber { get; set; }
    public string Language { get; set; } = "EN";
    public string TimeZoneId { get; set; } = "51";
    public string? PromptSet { get; set; }
    public bool DisableCustomPrompt { get; set; } = true;
    public string PropsJson { get; set; } = "{}";
    public string? RoutingJson { get; set; }
    public string? LiveChatLink { get; set; }
    public string? LiveChatWebsite { get; set; }
    public int? ThreeCxWebsiteLinkId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public List<AppDepartmentMembershipEntity> UserMemberships { get; set; } = [];
}

public sealed class AppDepartmentMembershipEntity
{
    public int Id { get; set; }
    public int AppUserId { get; set; }
    public AppUserEntity AppUser { get; set; } = null!;
    public int AppDepartmentId { get; set; }
    public AppDepartmentEntity AppDepartment { get; set; } = null!;
    public string ThreeCxRoleName { get; set; } = "users";
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public enum AppCallSource
{
    Pbx = 0,
    Browser = 1
}

public sealed class AppCallCdrEntity
{
    public long Id { get; set; }
    public AppCallSource Source { get; set; } = AppCallSource.Pbx;
    public int OperatorUserId { get; set; }
    public AppUserEntity OperatorUser { get; set; } = null!;
    public string OperatorUsername { get; set; } = string.Empty;
    public string OperatorExtension { get; set; } = string.Empty;
    public string TrackingKey { get; set; } = string.Empty;
    public string? CallScopeId { get; set; }
    public long? ParticipantId { get; set; }
    public long? PbxCallId { get; set; }
    public long? PbxLegId { get; set; }
    public SoftphoneCallDirection Direction { get; set; } = SoftphoneCallDirection.Outgoing;
    public string Status { get; set; } = string.Empty;
    public string? RemoteParty { get; set; }
    public string? RemoteName { get; set; }
    public string? EndReason { get; set; }
    public DateTimeOffset StartedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? AnsweredAtUtc { get; set; }
    public DateTimeOffset? EndedAtUtc { get; set; }
    public DateTimeOffset LastStatusAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public List<AppCallCdrStatusHistoryEntity> StatusHistory { get; set; } = [];
}

public sealed class AppCallCdrStatusHistoryEntity
{
    public long Id { get; set; }
    public long CallCdrId { get; set; }
    public AppCallCdrEntity CallCdr { get; set; } = null!;
    public string Status { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string? EventReason { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class AppUserRecord
{
    public int Id { get; init; }
    public string Username { get; init; } = string.Empty;
    public string PasswordHash { get; init; } = string.Empty;
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string EmailAddress { get; init; } = string.Empty;
    public string OwnedExtension { get; init; } = string.Empty;
    public string? ControlDn { get; init; }
    public AppUserRole Role { get; init; }
    public string Language { get; init; } = "EN";
    public string? PromptSet { get; init; }
    public string VmEmailOptions { get; init; } = "Notification";
    public bool SendEmailMissedCalls { get; init; }
    public bool Require2Fa { get; init; }
    public bool CallUsEnableChat { get; init; }
    public string? ClickToCallId { get; init; }
    public string? WebMeetingFriendlyName { get; init; }
    public string? SipUsername { get; init; }
    public string? SipAuthId { get; init; }
    public string? SipPassword { get; init; }
    public string? SipDisplayName { get; init; }
    public int? ThreeCxUserId { get; init; }
    public bool IsActive { get; init; }
}

public static class AppUserEntityMapper
{
    public static AppUserRecord ToRecord(this AppUserEntity entity)
    {
        return new AppUserRecord
        {
            Id = entity.Id,
            Username = entity.Username,
            PasswordHash = entity.PasswordHash,
            FirstName = entity.FirstName,
            LastName = entity.LastName,
            EmailAddress = entity.EmailAddress,
            OwnedExtension = entity.OwnedExtension,
            ControlDn = entity.ControlDn,
            Role = entity.Role,
            Language = entity.Language,
            PromptSet = entity.PromptSet,
            VmEmailOptions = entity.VmEmailOptions,
            SendEmailMissedCalls = entity.SendEmailMissedCalls,
            Require2Fa = entity.Require2Fa,
            CallUsEnableChat = entity.CallUsEnableChat,
            ClickToCallId = entity.ClickToCallId,
            WebMeetingFriendlyName = entity.WebMeetingFriendlyName,
            SipUsername = entity.SipUsername,
            SipAuthId = entity.SipAuthId,
            SipPassword = entity.SipPassword,
            SipDisplayName = entity.SipDisplayName,
            ThreeCxUserId = entity.ThreeCxUserId,
            IsActive = entity.IsActive
        };
    }
}
