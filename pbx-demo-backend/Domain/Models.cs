using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using CallControl.Api.Services;

namespace CallControl.Api.Domain;

public sealed class SoftphoneOptions
{
    public const string SectionName = "Softphone";

    public string JwtIssuer { get; set; } = "CallControl.Api";
    public string JwtAudience { get; set; } = "CallControl.WebClient";
    public string JwtSigningKey { get; set; } = "CHANGE_ME_TO_A_LONG_RANDOM_STRING";
    public int TokenLifetimeMinutes { get; set; } = 720;
    public int MaxWsReconnectAttempts { get; set; } = 8;
    public int WsReconnectDelaySeconds { get; set; } = 3;
    public ThreeCxApiOptions ThreeCx { get; set; } = new();
    public SoftphoneSipWebRtcOptions SipWebRtc { get; set; } = new();
    public List<SoftphoneUserCredential> Users { get; set; } = [];
}

public sealed class SoftphoneUserCredential
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string EmailAddress { get; set; } = string.Empty;
    public string OwnedExtension { get; set; } = string.Empty;
    public string? ControlDn { get; set; }
    public bool IsSupervisor { get; set; }
    public string Language { get; set; } = "EN";
    public string? PromptSet { get; set; }
    public string VmEmailOptions { get; set; } = "Notification";
    public bool SendEmailMissedCalls { get; set; } = true;
    public bool Require2Fa { get; set; }
    public bool CallUsEnableChat { get; set; } = true;
    public string? ClickToCallId { get; set; }
    public string? WebMeetingFriendlyName { get; set; }
    public SoftphoneSipUserCredential Sip { get; set; } = new();
}

public sealed class ThreeCxApiOptions
{
    public string PbxBase { get; set; } = string.Empty;
    public string AppId { get; set; } = string.Empty;
    public string AppSecret { get; set; } = string.Empty;
}

public sealed class SoftphoneSipWebRtcOptions
{
    public bool Enabled { get; set; }
    public string WebSocketUrl { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    public List<string> IceServers { get; set; } = [];
}

public sealed class SoftphoneSipUserCredential
{
    public string Username { get; set; } = string.Empty;
    public string AuthId { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}

public sealed class LoginRequest
{
    [JsonPropertyName("username")]
    public string Username { get; set; } = string.Empty;

    [JsonPropertyName("password")]
    public string Password { get; set; } = string.Empty;
}

public sealed class LoginResponse
{
    [JsonPropertyName("userId")]
    public int UserId { get; init; }

    [JsonPropertyName("accessToken")]
    public string AccessToken { get; init; } = string.Empty;

    [JsonPropertyName("expiresAtUtc")]
    public DateTimeOffset ExpiresAtUtc { get; init; }

    [JsonPropertyName("sessionId")]
    public string SessionId { get; init; } = string.Empty;

    [JsonPropertyName("username")]
    public string Username { get; init; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; init; } = string.Empty;

    [JsonPropertyName("role")]
    public string Role { get; init; } = AppUserRoles.User;

    [JsonPropertyName("hasSoftphoneAccess")]
    public bool HasSoftphoneAccess { get; init; }

    [JsonPropertyName("ownedExtensionDn")]
    public string OwnedExtensionDn { get; init; } = string.Empty;

    [JsonPropertyName("pbxBase")]
    public string PbxBase { get; init; } = string.Empty;
}

public sealed class SipRegistrationConfigResponse
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; }

    [JsonPropertyName("webSocketUrl")]
    public string WebSocketUrl { get; init; } = string.Empty;

    [JsonPropertyName("domain")]
    public string Domain { get; init; } = string.Empty;

    [JsonPropertyName("aor")]
    public string Aor { get; init; } = string.Empty;

    [JsonPropertyName("authorizationUsername")]
    public string AuthorizationUsername { get; init; } = string.Empty;

    [JsonPropertyName("authorizationPassword")]
    public string AuthorizationPassword { get; init; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; init; } = string.Empty;

    [JsonPropertyName("iceServers")]
    public List<string> IceServers { get; init; } = [];
}

public sealed class SelectExtensionRequest
{
    [JsonPropertyName("extensionDn")]
    public string ExtensionDn { get; set; } = string.Empty;
}

public sealed class SetActiveDeviceRequest
{
    [JsonPropertyName("deviceId")]
    public string DeviceId { get; set; } = string.Empty;
}

public sealed class OutgoingCallRequest
{
    [JsonPropertyName("destination")]
    public string Destination { get; set; } = string.Empty;
}

public sealed class TransferRequest
{
    [JsonPropertyName("destination")]
    public string Destination { get; set; } = string.Empty;
}

public sealed class SessionSnapshotResponse
{
    [JsonPropertyName("connected")]
    public bool Connected { get; set; }

    [JsonPropertyName("username")]
    public string Username { get; set; } = string.Empty;

    [JsonPropertyName("sessionId")]
    public string SessionId { get; set; } = string.Empty;

    [JsonPropertyName("selectedExtensionDn")]
    public string? SelectedExtensionDn { get; set; }

    [JsonPropertyName("ownedExtensionDn")]
    public string OwnedExtensionDn { get; set; } = string.Empty;

    [JsonPropertyName("controlDn")]
    public string? ControlDn { get; set; }

    [JsonPropertyName("devices")]
    public List<SoftphoneDeviceView> Devices { get; set; } = [];

    [JsonPropertyName("activeDeviceId")]
    public string? ActiveDeviceId { get; set; }

    [JsonPropertyName("calls")]
    public List<SoftphoneCallView> Calls { get; set; } = [];

    [JsonPropertyName("wsConnected")]
    public bool WsConnected { get; set; }

    [JsonPropertyName("lastUpdatedUtc")]
    public DateTimeOffset LastUpdatedUtc { get; set; }
}

public sealed class SoftphoneDeviceView
{
    [JsonPropertyName("dn")]
    public string? Dn { get; set; }

    [JsonPropertyName("deviceId")]
    public string? DeviceId { get; set; }

    [JsonPropertyName("userAgent")]
    public string? UserAgent { get; set; }
}

public sealed class SoftphoneCallView
{
    [JsonPropertyName("participantId")]
    public long ParticipantId { get; set; }

    [JsonPropertyName("dn")]
    public string? Dn { get; set; }

    [JsonPropertyName("partyDnType")]
    public string? PartyDnType { get; set; }

    [JsonPropertyName("callId")]
    public long? CallId { get; set; }

    [JsonPropertyName("legId")]
    public long? LegId { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("remoteParty")]
    public string? RemoteParty { get; set; }

    [JsonPropertyName("remoteName")]
    public string? RemoteName { get; set; }

    [JsonPropertyName("direction")]
    public SoftphoneCallDirection Direction { get; set; }

    [JsonPropertyName("directControl")]
    public bool DirectControl { get; set; }

    [JsonPropertyName("answerable")]
    public bool Answerable { get; set; }

    [JsonPropertyName("connectedAtUtc")]
    public DateTimeOffset? ConnectedAtUtc { get; set; }
}

public sealed class SoftphoneEventEnvelope
{
    [JsonPropertyName("eventType")]
    public string EventType { get; set; } = string.Empty;

    [JsonPropertyName("occurredAtUtc")]
    public DateTimeOffset OccurredAtUtc { get; set; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("payload")]
    public object Payload { get; set; } = new();
}

public sealed class ThreeCxConnectSettings
{
    public string PbxBase { get; init; } = string.Empty;
    public string AppId { get; init; } = string.Empty;
    public string AppSecret { get; init; } = string.Empty;
}

public sealed class SoftphoneSession : IAsyncDisposable
{
    public required string SessionId { get; init; }
    public required int AppUserId { get; init; }
    public required string Username { get; init; }
    public required string OwnedExtensionDn { get; init; }
    public required ThreeCxConnectSettings ConnectionSettings { get; init; }
    public required ThreeCxCallControlClient ThreeCxClient { get; init; }

    public ConcurrentDictionary<string, ThreeCxDnInfoModel> TopologyByDn { get; } = new(StringComparer.Ordinal);
    public ConcurrentDictionary<string, ThreeCxDevice> Devices { get; } = new(StringComparer.Ordinal);
    public ConcurrentDictionary<long, ThreeCxParticipant> Participants { get; } = new();
    public ConcurrentDictionary<long, DateTimeOffset> ConnectedAtByParticipant { get; } = new();
    public ConcurrentDictionary<long, SoftphoneCallDirection> DirectionByParticipant { get; } = new();

    public string? SelectedExtensionDn { get; set; }
    public string? ControlDn { get; set; }
    public string? ActiveDeviceId { get; set; }
    public bool WsConnected { get; set; }

    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastUpdatedUtc { get; set; } = DateTimeOffset.UtcNow;

    public SemaphoreSlim Gate { get; } = new(1, 1);

    public async ValueTask DisposeAsync()
    {
        Gate.Dispose();
        await ThreeCxClient.DisposeAsync();
    }
}

public sealed class ThreeCxDnInfo
{
    [JsonPropertyName("dn")]
    public string? Dn { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("devices")]
    public List<ThreeCxDevice>? Devices { get; set; }

    [JsonPropertyName("participants")]
    public List<ThreeCxParticipant>? Participants { get; set; }
}

public sealed class ThreeCxDnInfoModel
{
    public string? Dn { get; set; }
    public string? Type { get; set; }
    public Dictionary<string, ThreeCxDevice> Devices { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<long, ThreeCxParticipant> Participants { get; set; } = new();
}

public sealed class ThreeCxDevice
{
    [JsonPropertyName("dn")]
    public string? Dn { get; set; }

    [JsonPropertyName("device_id")]
    public string? DeviceId { get; set; }

    [JsonPropertyName("user_agent")]
    public string? UserAgent { get; set; }
}

public sealed class ThreeCxParticipant
{
    [JsonPropertyName("id")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public long? Id { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("party_caller_name")]
    public string? PartyCallerName { get; set; }

    [JsonPropertyName("party_dn")]
    public string? PartyDn { get; set; }

    [JsonPropertyName("party_caller_id")]
    public string? PartyCallerId { get; set; }

    [JsonPropertyName("device_id")]
    public string? DeviceId { get; set; }

    [JsonPropertyName("party_dn_type")]
    public string? PartyDnType { get; set; }

    [JsonPropertyName("direct_control")]
    public bool? DirectControl { get; set; }

    [JsonPropertyName("callid")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public long? CallId { get; set; }

    [JsonPropertyName("legid")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public long? LegId { get; set; }

    [JsonPropertyName("dn")]
    public string? Dn { get; set; }
}

public sealed class ThreeCxCallControlResult
{
    [JsonPropertyName("finalstatus")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public long? FinalStatus { get; set; }

    [JsonPropertyName("reason")]
    public string? Reason { get; set; }

    [JsonPropertyName("reasontext")]
    public string? ReasonText { get; set; }

    [JsonPropertyName("result")]
    public ThreeCxParticipant? Result { get; set; }
}

public sealed class ThreeCxWsEvent
{
    [JsonPropertyName("sequence")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public long Sequence { get; set; }

    [JsonPropertyName("event")]
    public ThreeCxWsEventBody? Event { get; set; }
}

public sealed class ThreeCxWsEventBody
{
    [JsonPropertyName("event_type")]
    public ThreeCxEventType EventType { get; set; }

    [JsonPropertyName("entity")]
    public string Entity { get; set; } = string.Empty;

    [JsonPropertyName("attached_data")]
    public JsonElement? AttachedData { get; set; }
}
