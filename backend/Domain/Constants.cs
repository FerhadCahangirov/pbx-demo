using System.Text.Json.Serialization;

namespace CallControl.Api.Domain;

public static class CallControlConstants
{
    public const string ExtensionType = "Wextension";
    public const string ParticipantEntity = "participants";
    public const string DeviceEntity = "devices";

    public const string ParticipantStatusDialing = "Dialing";
    public const string ParticipantStatusRinging = "Ringing";
    public const string ParticipantStatusConnected = "Connected";

    public const string ParticipantActionAnswer = "answer";
    public const string ParticipantActionDrop = "drop";
    public const string ParticipantActionDivert = "divert";
    public const string ParticipantActionTransferTo = "transferto";

    public const string UnregisteredDeviceId = "not_registered_dev";
}

public static class ClaimTypesEx
{
    public const string SessionId = "sid";
}

public enum ThreeCxEventType
{
    Upset = 0,
    Remove = 1,
    DtmfString = 2,
    PromptPlaybackFinished = 3
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SoftphoneCallDirection
{
    Incoming = 0,
    Outgoing = 1
}
