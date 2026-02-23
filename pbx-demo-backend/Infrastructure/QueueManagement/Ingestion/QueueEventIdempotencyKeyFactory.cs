using System.Security.Cryptography;
using System.Text;
using pbx_demo_backend.Domain.QueueManagement.Contracts;

namespace CallControl.Api.Infrastructure.QueueManagement.Ingestion;

public sealed class QueueEventIdempotencyKeyFactory
{
    public string CreateFromEnvelope(QueueInboundEventEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        if (!string.IsNullOrWhiteSpace(envelope.IdempotencyKey))
        {
            return NormalizeUserSuppliedKey(envelope.IdempotencyKey);
        }

        return Create(
            "inbox",
            envelope.Source,
            envelope.EventType,
            envelope.OrderingKey,
            envelope.EventAtUtc == default ? null : envelope.EventAtUtc.ToUniversalTime().ToString("O"),
            envelope.PayloadJson);
    }

    public string CreateActiveCallObserved(
        int pbxCallId,
        string? status,
        DateTimeOffset eventAtUtc,
        Guid snapshotKey)
    {
        return Create("active-observed", pbxCallId, status, eventAtUtc.ToUniversalTime().ToString("O"), snapshotKey);
    }

    public string CreateActiveCallDisappeared(int pbxCallId, DateTimeOffset observedAtUtc)
    {
        return Create("active-disappeared", pbxCallId, observedAtUtc.ToUniversalTime().ToString("O"));
    }

    public string CreateCallHistorySegment(int segmentId, int? srcRecId, int? dstRecId, DateTimeOffset segmentEndAtUtc)
    {
        return Create("callhistory-segment", segmentId, srcRecId, dstRecId, segmentEndAtUtc.ToUniversalTime().ToString("O"));
    }

    public string CreateCallLogRecord(
        string functionPath,
        string? cdrId,
        string? callHistoryId,
        int callId,
        int? segmentId)
    {
        return Create("calllog-record", functionPath, cdrId, callHistoryId, callId, segmentId);
    }

    public string CreateAgentActivity(QueueAgentActivityType activityType, long extensionId, long? queueCallId, string eventIdempotencyKey)
    {
        return Create("agent-activity", activityType, extensionId, queueCallId, eventIdempotencyKey);
    }

    public string CreateOutbox(string topic, string eventIdempotencyKey, string discriminator)
    {
        return Create("outbox", topic, eventIdempotencyKey, discriminator);
    }

    public string Create(params object?[] parts)
    {
        ArgumentNullException.ThrowIfNull(parts);

        var normalized = string.Join('\u001F', parts.Select(FormatPart));
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string NormalizeUserSuppliedKey(string key)
    {
        var normalized = key.Trim();
        if (normalized.Length <= 120)
        {
            return normalized;
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string FormatPart(object? value)
    {
        return value switch
        {
            null => string.Empty,
            DateTimeOffset dto => dto.ToUniversalTime().ToString("O"),
            DateTime dt => dt.ToUniversalTime().ToString("O"),
            Guid guid => guid.ToString("N"),
            bool flag => flag ? "1" : "0",
            _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty
        };
    }
}
