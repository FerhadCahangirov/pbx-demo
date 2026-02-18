using System.Text.Json.Serialization;

namespace CallControl.Api.Domain;

public sealed class BrowserCallView
{
    [JsonPropertyName("callId")]
    public string CallId { get; init; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    [JsonPropertyName("localExtension")]
    public string LocalExtension { get; init; } = string.Empty;

    [JsonPropertyName("remoteExtension")]
    public string RemoteExtension { get; init; } = string.Empty;

    [JsonPropertyName("remoteUsername")]
    public string RemoteUsername { get; init; } = string.Empty;

    [JsonPropertyName("isIncoming")]
    public bool IsIncoming { get; init; }

    [JsonPropertyName("createdAtUtc")]
    public DateTimeOffset CreatedAtUtc { get; init; }

    [JsonPropertyName("answeredAtUtc")]
    public DateTimeOffset? AnsweredAtUtc { get; init; }

    [JsonPropertyName("endedAtUtc")]
    public DateTimeOffset? EndedAtUtc { get; init; }

    [JsonPropertyName("endReason")]
    public string? EndReason { get; init; }
}

public sealed class WebRtcSignalRequest
{
    [JsonPropertyName("callId")]
    public string CallId { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("sdp")]
    public string? Sdp { get; set; }

    [JsonPropertyName("candidate")]
    public string? Candidate { get; set; }

    [JsonPropertyName("sdpMid")]
    public string? SdpMid { get; set; }

    [JsonPropertyName("sdpMLineIndex")]
    public int? SdpMLineIndex { get; set; }
}

public sealed class WebRtcSignalMessage
{
    [JsonPropertyName("callId")]
    public string CallId { get; init; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    [JsonPropertyName("sdp")]
    public string? Sdp { get; init; }

    [JsonPropertyName("candidate")]
    public string? Candidate { get; init; }

    [JsonPropertyName("sdpMid")]
    public string? SdpMid { get; init; }

    [JsonPropertyName("sdpMLineIndex")]
    public int? SdpMLineIndex { get; init; }

    [JsonPropertyName("fromExtension")]
    public string FromExtension { get; init; } = string.Empty;

    [JsonPropertyName("toExtension")]
    public string ToExtension { get; init; } = string.Empty;

    [JsonPropertyName("sentAtUtc")]
    public DateTimeOffset SentAtUtc { get; init; } = DateTimeOffset.UtcNow;
}
