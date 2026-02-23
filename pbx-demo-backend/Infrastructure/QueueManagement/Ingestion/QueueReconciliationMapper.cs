using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml;
using CallControl.Api.Infrastructure.QueueManagement.Persistence;
using pbx_demo_backend.Domain.QueueManagement.Contracts;

namespace CallControl.Api.Infrastructure.QueueManagement.Ingestion;

public sealed class QueueReconciliationMapper
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly QueueEventIdempotencyKeyFactory _idempotencyKeys;
    private readonly ILogger<QueueReconciliationMapper> _logger;

    public QueueReconciliationMapper(
        QueueEventIdempotencyKeyFactory idempotencyKeys,
        ILogger<QueueReconciliationMapper> logger)
    {
        _idempotencyKeys = idempotencyKeys;
        _logger = logger;
    }

    public QueueInboundEventEnvelope MapActiveCallObservedEnvelope(
        XapiPbxActiveCallDto dto,
        Guid snapshotKey,
        DateTimeOffset observedAtUtc,
        int waitOrder,
        bool estimatedOrder)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var eventAtUtc = dto.LastChangeStatus ?? dto.EstablishedAt ?? dto.ServerNow ?? observedAtUtc;
        var payload = new QueueActiveCallObservedPayload
        {
            PbxCallId = dto.Id,
            CorrelationKey = BuildActiveCallCorrelationKey(dto.Id),
            Status = Trim(dto.Status),
            Caller = Trim(dto.Caller),
            Callee = Trim(dto.Callee),
            EstablishedAtUtc = dto.EstablishedAt,
            LastChangeStatusAtUtc = dto.LastChangeStatus,
            ServerNowUtc = dto.ServerNow,
            ObservedAtUtc = observedAtUtc,
            SnapshotKey = snapshotKey,
            WaitOrder = waitOrder,
            EstimatedOrder = estimatedOrder
        };

        return new QueueInboundEventEnvelope
        {
            Source = QueueIngestionSources.XapiActiveCalls,
            EventType = QueueIngestionEventTypes.ActiveCallObserved,
            EventAtUtc = eventAtUtc,
            OrderingKey = BuildOrderingKey(BuildCallOrderingKey(dto.Id)),
            IdempotencyKey = _idempotencyKeys.CreateActiveCallObserved(dto.Id, dto.Status, eventAtUtc, snapshotKey),
            PayloadJson = Serialize(payload)
        };
    }

    public QueueInboundEventEnvelope MapActiveCallDisappearedEnvelope(
        QueueCallEntity existingCall,
        DateTimeOffset observedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(existingCall);

        var pbxCallId = existingCall.PbxCallId ?? 0;
        var payload = new QueueActiveCallDisappearedPayload
        {
            PbxCallId = pbxCallId <= 0 ? null : pbxCallId,
            CorrelationKey = existingCall.CorrelationKey,
            QueueIdHint = existingCall.QueueId,
            LastKnownStatus = existingCall.CurrentStatus.ToString(),
            LastSeenAtUtc = existingCall.LastSeenAtUtc,
            ObservedAtUtc = observedAtUtc
        };

        return new QueueInboundEventEnvelope
        {
            Source = QueueIngestionSources.XapiActiveCalls,
            EventType = QueueIngestionEventTypes.ActiveCallDisappeared,
            EventAtUtc = observedAtUtc,
            OrderingKey = BuildOrderingKey(pbxCallId > 0
                ? BuildCallOrderingKey(pbxCallId)
                : $"callkey:{existingCall.CorrelationKey}"),
            IdempotencyKey = pbxCallId > 0
                ? _idempotencyKeys.CreateActiveCallDisappeared(pbxCallId, observedAtUtc)
                : _idempotencyKeys.Create("active-disappeared-fallback", existingCall.CorrelationKey, observedAtUtc),
            PayloadJson = Serialize(payload)
        };
    }

    public QueueCallHistoryEntity MapCallHistoryViewRow(
        XapiPbxCallHistoryViewDto row,
        long? queueIdHint,
        DateTimeOffset importedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(row);

        return new QueueCallHistoryEntity
        {
            QueueId = queueIdHint,
            SourceRecordType = QueueHistorySourceTypes.CallHistoryView,
            SegmentId = row.SegmentId,
            SegmentType = row.SegmentType,
            SegmentActionId = row.SegmentActionId,
            SegmentStartAtUtc = row.SegmentStartTime,
            SegmentEndAtUtc = row.SegmentEndTime,
            CallAnswered = row.CallAnswered,
            CallTimeMs = TryParseDurationMs(row.CallTime),
            SourceDn = Trim(row.SrcDn),
            SourceDisplayName = Trim(row.SrcDisplayName) ?? Trim(row.SrcExtendedDisplayName),
            SourceCallerId = Trim(row.SrcCallerNumber),
            SourceType = row.SrcDnType,
            DestinationDn = Trim(row.DstDn),
            DestinationDisplayName = Trim(row.DstDisplayName) ?? Trim(row.DstExtendedDisplayName),
            DestinationCallerId = Trim(row.DstCallerNumber),
            DestinationType = row.DstDnType,
            RawJson = Serialize(row),
            ImportedAtUtc = importedAtUtc
        };
    }

    public QueueInboundEventEnvelope MapCallHistoryViewEnvelope(
        XapiPbxCallHistoryViewDto row,
        long? queueIdHint,
        DateTimeOffset observedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(row);

        var correlationKey = BuildCallHistoryCorrelationKey(row);
        var eventAtUtc = row.SegmentEndTime >= row.SegmentStartTime
            ? row.SegmentEndTime
            : row.SegmentStartTime;

        var payload = new QueueCallHistoryReconciliationPayload
        {
            CorrelationKey = correlationKey,
            QueueIdHint = queueIdHint,
            ObservedAtUtc = observedAtUtc,
            Row = row
        };

        return new QueueInboundEventEnvelope
        {
            Source = QueueIngestionSources.XapiCallHistory,
            EventType = QueueIngestionEventTypes.CallHistorySegmentReconciled,
            EventAtUtc = eventAtUtc,
            OrderingKey = BuildOrderingKey($"hist:{correlationKey}"),
            IdempotencyKey = _idempotencyKeys.CreateCallHistorySegment(
                row.SegmentId,
                row.SrcRecId,
                row.DstRecId,
                eventAtUtc),
            PayloadJson = Serialize(payload)
        };
    }

    public QueueCallHistoryEntity MapCallLogRow(
        XapiPbxCallLogDataDto row,
        long? queueIdHint,
        DateTimeOffset importedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(row);

        return new QueueCallHistoryEntity
        {
            QueueId = queueIdHint,
            SourceRecordType = QueueHistorySourceTypes.ReportCallLogData,
            PbxCallId = row.CallId,
            CdrId = Trim(row.CdrId),
            CallHistoryId = Trim(row.CallHistoryId),
            MainCallHistoryId = Trim(row.MainCallHistoryId),
            SegmentId = row.SegmentId,
            SegmentActionId = row.ActionType,
            SegmentStartAtUtc = row.StartTime,
            CallAnswered = row.Answered,
            RingingDurationMs = TryParseDurationMs(row.RingingDuration),
            TalkingDurationMs = TryParseDurationMs(row.TalkingDuration),
            Direction = Trim(row.Direction),
            Status = Trim(row.Status),
            Reason = Trim(row.Reason),
            CallType = Trim(row.CallType),
            SourceDn = Trim(row.SourceDn),
            SourceDisplayName = Trim(row.SourceDisplayName),
            SourceCallerId = Trim(row.SourceCallerId),
            SourceType = row.SourceType,
            DestinationDn = Trim(row.DestinationDn),
            DestinationDisplayName = Trim(row.DestinationDisplayName),
            DestinationCallerId = Trim(row.DestinationCallerId),
            DestinationType = row.DestinationType,
            ActionDn = Trim(row.ActionDnDn),
            ActionDnDisplayName = Trim(row.ActionDnDisplayName),
            ActionDnCallerId = Trim(row.ActionDnCallerId),
            ActionDnType = row.ActionDnType,
            CallCost = row.CallCost,
            RecordingUrl = Trim(row.RecordingUrl),
            Transcription = Trim(row.Transcription),
            SentimentScore = row.SentimentScore,
            RawJson = Serialize(row),
            ImportedAtUtc = importedAtUtc
        };
    }

    public QueueInboundEventEnvelope MapCallLogEnvelope(
        string functionPath,
        XapiPbxCallLogDataDto row,
        long? queueIdHint,
        long? extensionIdHint,
        DateTimeOffset observedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(row);

        var correlationKey = BuildCallLogCorrelationKey(row);
        var eventAtUtc = row.StartTime ?? observedAtUtc;
        var payload = new QueueCallLogReconciliationPayload
        {
            FunctionPath = functionPath,
            CorrelationKey = correlationKey,
            QueueIdHint = queueIdHint,
            ExtensionIdHint = extensionIdHint,
            ObservedAtUtc = observedAtUtc,
            Row = row
        };

        return new QueueInboundEventEnvelope
        {
            Source = QueueIngestionSources.XapiCallLog,
            EventType = QueueIngestionEventTypes.CallLogRecordReconciled,
            EventAtUtc = eventAtUtc,
            OrderingKey = BuildOrderingKey($"log:{correlationKey}"),
            IdempotencyKey = _idempotencyKeys.CreateCallLogRecord(
                functionPath,
                row.CdrId,
                row.CallHistoryId,
                row.CallId,
                row.SegmentId),
            PayloadJson = Serialize(payload)
        };
    }

    public long? ResolveQueueId(
        XapiPbxCallHistoryViewDto row,
        IReadOnlyDictionary<string, long> queueIdsByNumber)
    {
        return TryLookup(queueIdsByNumber, row.SrcDn)
            ?? TryLookup(queueIdsByNumber, row.DstDn);
    }

    public long? ResolveQueueId(
        XapiPbxCallLogDataDto row,
        IReadOnlyDictionary<string, long> queueIdsByNumber)
    {
        return TryLookup(queueIdsByNumber, row.ActionDnDn)
            ?? TryLookup(queueIdsByNumber, row.DestinationDn)
            ?? TryLookup(queueIdsByNumber, row.SourceDn);
    }

    public long? ResolveExtensionId(
        XapiPbxCallLogDataDto row,
        IReadOnlyDictionary<string, long> extensionIdsByNumber)
    {
        return TryLookup(extensionIdsByNumber, row.ActionDnDn)
            ?? TryLookup(extensionIdsByNumber, row.DestinationDn)
            ?? TryLookup(extensionIdsByNumber, row.SourceDn);
    }

    public bool TryParseCheckpointCursor(string? cursorValue, out DateTimeOffset cursorUtc)
    {
        cursorUtc = default;
        if (string.IsNullOrWhiteSpace(cursorValue))
        {
            return false;
        }

        return DateTimeOffset.TryParse(
            cursorValue,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out cursorUtc);
    }

    public static long? TryParseDurationMs(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();

        try
        {
            var xmlDuration = XmlConvert.ToTimeSpan(trimmed);
            return (long)xmlDuration.TotalMilliseconds;
        }
        catch (FormatException)
        {
            // Fall through to other parsers.
        }

        if (TimeSpan.TryParse(trimmed, CultureInfo.InvariantCulture, out var timeSpan))
        {
            return (long)timeSpan.TotalMilliseconds;
        }

        if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
        {
            return (long)(seconds * 1000d);
        }

        return null;
    }

    public string BuildCallLogCheckpointPartitionKey(string functionPath)
    {
        var trimmed = (functionPath ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return "calllog:empty";
        }

        var hash = _idempotencyKeys.Create("calllog-checkpoint", trimmed);
        return $"calllog:{hash[..Math.Min(16, hash.Length)]}";
    }

    public bool IsLikelyDuplicateCallHistoryRow(PBXDbContext db, XapiPbxCallHistoryViewDto row)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(row);

        return db.QueueCallHistory.Any(x =>
            x.SourceRecordType == QueueHistorySourceTypes.CallHistoryView &&
            x.SegmentId == row.SegmentId &&
            x.SegmentStartAtUtc == row.SegmentStartTime &&
            x.SegmentEndAtUtc == row.SegmentEndTime &&
            x.SourceDn == row.SrcDn &&
            x.DestinationDn == row.DstDn);
    }

    public bool IsLikelyDuplicateCallLogRow(PBXDbContext db, XapiPbxCallLogDataDto row)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(row);

        var cdrId = Trim(row.CdrId);
        var callHistoryId = Trim(row.CallHistoryId);

        return db.QueueCallHistory.Any(x =>
            x.SourceRecordType == QueueHistorySourceTypes.ReportCallLogData &&
            x.PbxCallId == row.CallId &&
            x.CdrId == cdrId &&
            x.CallHistoryId == callHistoryId &&
            x.SegmentId == row.SegmentId);
    }

    private static string BuildActiveCallCorrelationKey(int pbxCallId)
        => $"xapi-activecall:{pbxCallId}";

    private static string BuildCallOrderingKey(int pbxCallId)
        => $"call:{pbxCallId}";

    private static string BuildOrderingKey(string callKey)
        => $"callkey:{callKey}";

    private static string BuildCallHistoryCorrelationKey(XapiPbxCallHistoryViewDto row)
    {
        if (row.SrcRecId is not null || row.DstRecId is not null)
        {
            return $"chv:r{row.SrcRecId?.ToString(CultureInfo.InvariantCulture) ?? "n"}:{row.DstRecId?.ToString(CultureInfo.InvariantCulture) ?? "n"}:s{row.SegmentId}";
        }

        return $"chv:s{row.SegmentId}:sp{row.SrcParticipantId}:dp{row.DstParticipantId}";
    }

    private static string BuildCallLogCorrelationKey(XapiPbxCallLogDataDto row)
    {
        var cdrId = Trim(row.CdrId);
        if (!string.IsNullOrWhiteSpace(cdrId))
        {
            return $"cdr:{cdrId}";
        }

        var callHistoryId = Trim(row.CallHistoryId);
        if (!string.IsNullOrWhiteSpace(callHistoryId))
        {
            return $"ch:{callHistoryId}";
        }

        var mainCallHistoryId = Trim(row.MainCallHistoryId);
        if (!string.IsNullOrWhiteSpace(mainCallHistoryId))
        {
            return $"mch:{mainCallHistoryId}";
        }

        var segment = row.SegmentId?.ToString(CultureInfo.InvariantCulture) ?? "n";
        return $"call:{row.CallId}:seg:{segment}";
    }

    private static string Serialize<T>(T value)
        => JsonSerializer.Serialize(value, JsonOptions);

    private static long? TryLookup(IReadOnlyDictionary<string, long> map, string? dnOrNumber)
    {
        if (map.Count == 0)
        {
            return null;
        }

        var key = Trim(dnOrNumber);
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        return map.TryGetValue(key, out var value) ? value : null;
    }

    private static string? Trim(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

internal static class QueueIngestionSources
{
    public const string XapiActiveCalls = "XapiActiveCalls";
    public const string XapiCallHistory = "XapiCallHistory";
    public const string XapiCallLog = "XapiCallLog";
}

internal static class QueueIngestionEventTypes
{
    public const string ActiveCallObserved = "ActiveCallObserved";
    public const string ActiveCallDisappeared = "ActiveCallDisappeared";
    public const string CallHistorySegmentReconciled = "CallHistorySegmentReconciled";
    public const string CallLogRecordReconciled = "CallLogRecordReconciled";
}

internal static class QueueHistorySourceTypes
{
    public const string CallHistoryView = "CallHistoryView";
    public const string ReportCallLogData = "ReportCallLogData";
}

internal sealed class QueueActiveCallObservedPayload
{
    
    public int PbxCallId { get; set; }
    public string CorrelationKey { get; set; } = string.Empty;
    public string? Status { get; set; }
    public string? Caller { get; set; }
    public string? Callee { get; set; }
    public DateTimeOffset? EstablishedAtUtc { get; set; }
    public DateTimeOffset? LastChangeStatusAtUtc { get; set; }
    public DateTimeOffset? ServerNowUtc { get; set; }
    public DateTimeOffset ObservedAtUtc { get; set; }
    public Guid SnapshotKey { get; set; }
    public int? WaitOrder { get; set; }
    public bool EstimatedOrder { get; set; }
}

internal sealed class QueueActiveCallDisappearedPayload
{
    
    public int? PbxCallId { get; set; }
    public string CorrelationKey { get; set; } = string.Empty;
    public long? QueueIdHint { get; set; }
    public string? LastKnownStatus { get; set; }
    public DateTimeOffset? LastSeenAtUtc { get; set; }
    public DateTimeOffset ObservedAtUtc { get; set; }
}

internal sealed class QueueCallHistoryReconciliationPayload
{
    public string CorrelationKey { get; set; } = string.Empty;
    public long? QueueIdHint { get; set; }
    public DateTimeOffset ObservedAtUtc { get; set; }
    public XapiPbxCallHistoryViewDto Row { get; set; } = new();
}

internal sealed class QueueCallLogReconciliationPayload
{
    
    public string FunctionPath { get; set; } = string.Empty;
    public string CorrelationKey { get; set; } = string.Empty;
    public long? QueueIdHint { get; set; }
    public long? ExtensionIdHint { get; set; }
    public DateTimeOffset ObservedAtUtc { get; set; }
    public XapiPbxCallLogDataDto Row { get; set; } = new();
}
