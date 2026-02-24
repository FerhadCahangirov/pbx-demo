using System.Text.Json.Serialization;

namespace CallControl.Api.Domain;

/// <summary>
/// Defines the authorization level assigned to an application user in the CRM domain.
/// </summary>
/// <remarks>
/// The role is used by API authorization policies, UI feature toggles, and data-scoping rules
/// (for example, whether the user can see only their own calls or department-wide calls).
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AppUserRole
{
    /// <summary>
    /// Standard operator role with day-to-day calling and self-service capabilities.
    /// </summary>
    User = 0,

    /// <summary>
    /// Elevated role for supervisors who manage teams, monitor activity, and access broader reporting.
    /// </summary>
    Supervisor = 1
}

/// <summary>
/// Provides canonical string constants for <see cref="AppUserRole"/> values.
/// </summary>
/// <remarks>
/// Use these constants when role names are needed as strings (for example in claims, policy setup,
/// or JSON payload comparisons) to avoid hard-coded literals and typo-prone checks.
/// </remarks>
public static class AppUserRoles
{
    /// <summary>
    /// String representation of <see cref="AppUserRole.User"/>.
    /// </summary>
    public const string User = nameof(AppUserRole.User);

    /// <summary>
    /// String representation of <see cref="AppUserRole.Supervisor"/>.
    /// </summary>
    public const string Supervisor = nameof(AppUserRole.Supervisor);
}

/// <summary>
/// Persistence model for a CRM application user.
/// </summary>
/// <remarks>
/// This entity contains identity, profile, PBX mapping, telephony preferences, and lifecycle fields.
/// It is the primary record used to authenticate users and associate them with calls and departments.
/// </remarks>
public sealed class AppUserEntity
{
    /// <summary>
    /// Internal numeric primary key of the user record.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Login name used by the user when authenticating to the application.
    /// </summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// Hashed password value stored for local authentication.
    /// </summary>
    /// <remarks>
    /// This value should never contain plaintext passwords and should only be produced by a secure
    /// password-hashing algorithm.
    /// </remarks>
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>
    /// User's given name, used in display labels and audit output.
    /// </summary>
    public string FirstName { get; set; } = string.Empty;

    /// <summary>
    /// User's family name, used in display labels and search criteria.
    /// </summary>
    public string LastName { get; set; } = string.Empty;

    /// <summary>
    /// Primary email address used for notifications and account communication.
    /// </summary>
    public string EmailAddress { get; set; } = string.Empty;

    /// <summary>
    /// Default PBX extension/DN owned by this user.
    /// </summary>
    /// <remarks>
    /// This extension is typically the baseline for call origination, inbound routing, and session context.
    /// </remarks>
    public string OwnedExtension { get; set; } = string.Empty;

    /// <summary>
    /// Optional direct control DN used when call control should target a DN different from the owned extension.
    /// </summary>
    public string? ControlDn { get; set; }

    /// <summary>
    /// Authorization role that defines the user's permission scope.
    /// </summary>
    public AppUserRole Role { get; set; } = AppUserRole.User;

    /// <summary>
    /// Preferred language code used for UI and prompt localization.
    /// </summary>
    public string Language { get; set; } = "EN";

    /// <summary>
    /// Optional named prompt set assigned to the user for voice/media customization.
    /// </summary>
    public string? PromptSet { get; set; }

    /// <summary>
    /// Voicemail email behavior option (for example notification-only vs. attachment delivery).
    /// </summary>
    public string VmEmailOptions { get; set; } = "Notification";

    /// <summary>
    /// Indicates whether missed-call notifications should be emailed to the user.
    /// </summary>
    public bool SendEmailMissedCalls { get; set; } = true;

    /// <summary>
    /// Indicates whether two-factor authentication is required for this account.
    /// </summary>
    public bool Require2Fa { get; set; }

    /// <summary>
    /// Enables chat capabilities for call-us/live-chat integrations associated with this user.
    /// </summary>
    public bool CallUsEnableChat { get; set; } = true;

    /// <summary>
    /// Optional identifier used for click-to-call integrations in external systems.
    /// </summary>
    public string? ClickToCallId { get; set; }

    /// <summary>
    /// Optional friendly name presented for web meetings created by this user.
    /// </summary>
    public string? WebMeetingFriendlyName { get; set; }

    /// <summary>
    /// Optional SIP username used for WebRTC/SIP registration.
    /// </summary>
    public string? SipUsername { get; set; }

    /// <summary>
    /// Optional SIP authentication identifier, which may differ from <see cref="SipUsername"/>.
    /// </summary>
    public string? SipAuthId { get; set; }

    /// <summary>
    /// Optional SIP credential secret for client registration.
    /// </summary>
    /// <remarks>
    /// Treat this as sensitive data and avoid exposing it in logs or non-secure channels.
    /// </remarks>
    public string? SipPassword { get; set; }

    /// <summary>
    /// Optional display name shown in SIP endpoints and call presentation.
    /// </summary>
    public string? SipDisplayName { get; set; }

    /// <summary>
    /// Optional mapped numeric identifier of the corresponding user in 3CX.
    /// </summary>
    public int? ThreeCxUserId { get; set; }

    /// <summary>
    /// Indicates whether this account is active and allowed to authenticate/use telephony services.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// UTC timestamp when the user record was created.
    /// </summary>
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// UTC timestamp when the user record was last updated.
    /// </summary>
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Department memberships that define the user's organization and routing context.
    /// </summary>
    public List<AppDepartmentMembershipEntity> DepartmentMemberships { get; set; } = [];

    /// <summary>
    /// Call CDR entries attributed to this user as the operator.
    /// </summary>
    public List<AppCallCdrEntity> CallCdrs { get; set; } = [];
}

/// <summary>
/// Persistence model for a CRM department/team.
/// </summary>
/// <remarks>
/// Departments group users, encapsulate language/routing settings, and map to PBX entities such as 3CX groups.
/// </remarks>
public sealed class AppDepartmentEntity
{
    /// <summary>
    /// Internal numeric primary key of the department record.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Human-readable department name shown in administration and assignment views.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Required identifier of the corresponding 3CX group.
    /// </summary>
    public int ThreeCxGroupId { get; set; }

    /// <summary>
    /// Optional 3CX group number/code for environments that address groups by number.
    /// </summary>
    public string? ThreeCxGroupNumber { get; set; }

    /// <summary>
    /// Department default language code for prompts and localization behavior.
    /// </summary>
    public string Language { get; set; } = "EN";

    /// <summary>
    /// PBX time-zone identifier used for schedule-aware routing and prompt behavior.
    /// </summary>
    public string TimeZoneId { get; set; } = "51";

    /// <summary>
    /// Optional prompt-set override applied at the department level.
    /// </summary>
    public string? PromptSet { get; set; }

    /// <summary>
    /// Indicates whether custom prompts are disabled for this department.
    /// </summary>
    public bool DisableCustomPrompt { get; set; } = true;

    /// <summary>
    /// JSON blob containing department-level miscellaneous properties.
    /// </summary>
    public string PropsJson { get; set; } = "{}";

    /// <summary>
    /// Optional JSON payload describing routing rules and strategy for the department.
    /// </summary>
    public string? RoutingJson { get; set; }

    /// <summary>
    /// Optional direct link used by customers to start live chat with this department.
    /// </summary>
    public string? LiveChatLink { get; set; }

    /// <summary>
    /// Optional website URL where department live chat widget/integration is hosted.
    /// </summary>
    public string? LiveChatWebsite { get; set; }

    /// <summary>
    /// Optional mapped identifier of an associated 3CX website/chat link record.
    /// </summary>
    public int? ThreeCxWebsiteLinkId { get; set; }

    /// <summary>
    /// UTC timestamp when the department record was created.
    /// </summary>
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// UTC timestamp when the department record was last updated.
    /// </summary>
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// User membership rows that associate users with this department.
    /// </summary>
    public List<AppDepartmentMembershipEntity> UserMemberships { get; set; } = [];
}

/// <summary>
/// Join entity that links users to departments and stores per-membership role metadata.
/// </summary>
public sealed class AppDepartmentMembershipEntity
{
    /// <summary>
    /// Internal numeric primary key of the membership row.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Foreign key to the associated <see cref="AppUserEntity"/>.
    /// </summary>
    public int AppUserId { get; set; }

    /// <summary>
    /// Navigation reference to the associated user.
    /// </summary>
    public AppUserEntity AppUser { get; set; } = null!;

    /// <summary>
    /// Foreign key to the associated <see cref="AppDepartmentEntity"/>.
    /// </summary>
    public int AppDepartmentId { get; set; }

    /// <summary>
    /// Navigation reference to the associated department.
    /// </summary>
    public AppDepartmentEntity AppDepartment { get; set; } = null!;

    /// <summary>
    /// Role name to apply in 3CX for this user within the department (for example <c>users</c>).
    /// </summary>
    public string ThreeCxRoleName { get; set; } = "users";

    /// <summary>
    /// UTC timestamp when the membership row was created.
    /// </summary>
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Identifies which subsystem generated a call detail record.
/// </summary>
public enum AppCallSource
{
    /// <summary>
    /// The call event originated from PBX-side feeds/APIs.
    /// </summary>
    Pbx = 0,

    /// <summary>
    /// The call event originated from browser/WebRTC-side signaling.
    /// </summary>
    Browser = 1
}

/// <summary>
/// Persistent call detail record (CDR) used to track call lifecycle and reporting metadata.
/// </summary>
public sealed class AppCallCdrEntity
{
    /// <summary>
    /// Internal numeric primary key of the CDR row.
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// Source subsystem that produced this CDR.
    /// </summary>
    public AppCallSource Source { get; set; } = AppCallSource.Pbx;

    /// <summary>
    /// Foreign key to the operator user that handled or initiated the call.
    /// </summary>
    public int OperatorUserId { get; set; }

    /// <summary>
    /// Navigation reference to the operator user.
    /// </summary>
    public AppUserEntity OperatorUser { get; set; } = null!;

    /// <summary>
    /// Snapshot of the operator username at call time for historical readability.
    /// </summary>
    public string OperatorUsername { get; set; } = string.Empty;

    /// <summary>
    /// Snapshot of the operator extension/DN used for this call.
    /// </summary>
    public string OperatorExtension { get; set; } = string.Empty;

    /// <summary>
    /// Correlation key used to group multi-event updates for the same logical call.
    /// </summary>
    public string TrackingKey { get; set; } = string.Empty;

    /// <summary>
    /// Optional higher-level scope identifier to correlate this call with a session/workflow.
    /// </summary>
    public string? CallScopeId { get; set; }

    /// <summary>
    /// Optional participant identifier from 3CX call-control events.
    /// </summary>
    public long? ParticipantId { get; set; }

    /// <summary>
    /// Optional PBX call identifier.
    /// </summary>
    public long? PbxCallId { get; set; }

    /// <summary>
    /// Optional PBX leg identifier for calls with multiple legs.
    /// </summary>
    public long? PbxLegId { get; set; }

    /// <summary>
    /// Call direction (incoming/outgoing) captured for analytics and UI behavior.
    /// </summary>
    public SoftphoneCallDirection Direction { get; set; } = SoftphoneCallDirection.Outgoing;

    /// <summary>
    /// Current call status (ringing, connected, ended, etc.).
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// Remote party number/address involved in this call.
    /// </summary>
    public string? RemoteParty { get; set; }

    /// <summary>
    /// Remote party display name if available.
    /// </summary>
    public string? RemoteName { get; set; }

    /// <summary>
    /// Optional terminal reason describing why the call ended.
    /// </summary>
    public string? EndReason { get; set; }

    /// <summary>
    /// UTC timestamp when the call started.
    /// </summary>
    public DateTimeOffset StartedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// UTC timestamp when the call was answered/connected, if reached.
    /// </summary>
    public DateTimeOffset? AnsweredAtUtc { get; set; }

    /// <summary>
    /// UTC timestamp when the call ended, if completed.
    /// </summary>
    public DateTimeOffset? EndedAtUtc { get; set; }

    /// <summary>
    /// UTC timestamp of the latest status update received for this call.
    /// </summary>
    public DateTimeOffset LastStatusAtUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Indicates whether the call is still considered active/open.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// UTC timestamp when this CDR row was created.
    /// </summary>
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// UTC timestamp when this CDR row was last updated.
    /// </summary>
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Ordered history of status transitions associated with this call.
    /// </summary>
    public List<AppCallCdrStatusHistoryEntity> StatusHistory { get; set; } = [];
}

/// <summary>
/// Stores individual status transition events for a call CDR.
/// </summary>
public sealed class AppCallCdrStatusHistoryEntity
{
    /// <summary>
    /// Internal numeric primary key of the status-history row.
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// Foreign key to the parent <see cref="AppCallCdrEntity"/>.
    /// </summary>
    public long CallCdrId { get; set; }

    /// <summary>
    /// Navigation reference to the parent call CDR.
    /// </summary>
    public AppCallCdrEntity CallCdr { get; set; } = null!;

    /// <summary>
    /// Status value applied at this event point.
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// Event category/source label that produced this status update.
    /// </summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>
    /// Optional reason payload provided by upstream event data.
    /// </summary>
    public string? EventReason { get; set; }

    /// <summary>
    /// UTC timestamp when the event occurred in the source system.
    /// </summary>
    public DateTimeOffset OccurredAtUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// UTC timestamp when this history row was persisted.
    /// </summary>
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Immutable projection of <see cref="AppUserEntity"/> used by services that require a lightweight value object.
/// </summary>
public sealed class AppUserRecord
{
    /// <summary>
    /// Internal numeric primary key of the user.
    /// </summary>
    public int Id { get; init; }

    /// <summary>
    /// Login name of the user.
    /// </summary>
    public string Username { get; init; } = string.Empty;

    /// <summary>
    /// Password hash captured from persistent user storage.
    /// </summary>
    public string PasswordHash { get; init; } = string.Empty;

    /// <summary>
    /// User's given name.
    /// </summary>
    public string FirstName { get; init; } = string.Empty;

    /// <summary>
    /// User's family name.
    /// </summary>
    public string LastName { get; init; } = string.Empty;

    /// <summary>
    /// Primary email address.
    /// </summary>
    public string EmailAddress { get; init; } = string.Empty;

    /// <summary>
    /// Default owned extension/DN.
    /// </summary>
    public string OwnedExtension { get; init; } = string.Empty;

    /// <summary>
    /// Optional control DN override.
    /// </summary>
    public string? ControlDn { get; init; }

    /// <summary>
    /// User authorization role.
    /// </summary>
    public AppUserRole Role { get; init; }

    /// <summary>
    /// Preferred language code.
    /// </summary>
    public string Language { get; init; } = "EN";

    /// <summary>
    /// Optional prompt-set assignment.
    /// </summary>
    public string? PromptSet { get; init; }

    /// <summary>
    /// Voicemail email option.
    /// </summary>
    public string VmEmailOptions { get; init; } = "Notification";

    /// <summary>
    /// Indicates whether missed-call emails are enabled.
    /// </summary>
    public bool SendEmailMissedCalls { get; init; }

    /// <summary>
    /// Indicates whether two-factor authentication is required.
    /// </summary>
    public bool Require2Fa { get; init; }

    /// <summary>
    /// Indicates whether chat integration is enabled.
    /// </summary>
    public bool CallUsEnableChat { get; init; }

    /// <summary>
    /// Optional click-to-call integration identifier.
    /// </summary>
    public string? ClickToCallId { get; init; }

    /// <summary>
    /// Optional web-meeting friendly display name.
    /// </summary>
    public string? WebMeetingFriendlyName { get; init; }

    /// <summary>
    /// Optional SIP username.
    /// </summary>
    public string? SipUsername { get; init; }

    /// <summary>
    /// Optional SIP authentication identifier.
    /// </summary>
    public string? SipAuthId { get; init; }

    /// <summary>
    /// Optional SIP password.
    /// </summary>
    public string? SipPassword { get; init; }

    /// <summary>
    /// Optional SIP display name.
    /// </summary>
    public string? SipDisplayName { get; init; }

    /// <summary>
    /// Optional mapped 3CX user identifier.
    /// </summary>
    public int? ThreeCxUserId { get; init; }

    /// <summary>
    /// Indicates whether the user is active.
    /// </summary>
    public bool IsActive { get; init; }
}

/// <summary>
/// Mapping helpers for converting persistence entities to immutable record-style models.
/// </summary>
public static class AppUserEntityMapper
{
    /// <summary>
    /// Creates an <see cref="AppUserRecord"/> snapshot from a mutable <see cref="AppUserEntity"/> instance.
    /// </summary>
    /// <param name="entity">Source entity to convert.</param>
    /// <returns>
    /// A new immutable record containing the selected user profile, security, and telephony fields.
    /// </returns>
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
