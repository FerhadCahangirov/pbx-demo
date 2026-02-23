namespace pbx_demo_backend.Domain.QueueManagement.Domain;

public readonly record struct QueueCallKey
{
    public string Value { get; }

    public QueueCallKey(string value)
    {
        Value = QueueDomainGuard.Required(value, nameof(value));
    }

    public override string ToString() => Value;
}

public readonly record struct QueueSlaThreshold
{
    public int Seconds { get; }

    public QueueSlaThreshold(int seconds)
    {
        if (seconds <= 0)
        {
            throw new QueueDomainValidationException("SLA threshold must be greater than zero.");
        }

        Seconds = seconds;
    }

    public override string ToString() => Seconds.ToString();
}

public sealed class QueueCorrelationIds
{
    public int? PbxCallId { get; private set; }
    public string? CdrId { get; private set; }
    public string? CallHistoryId { get; private set; }
    public string? MainCallHistoryId { get; private set; }
    public int? SegmentId { get; private set; }

    public QueueCorrelationIds(
        int? pbxCallId = null,
        string? cdrId = null,
        string? callHistoryId = null,
        string? mainCallHistoryId = null,
        int? segmentId = null)
    {
        PbxCallId = pbxCallId;
        CdrId = cdrId;
        CallHistoryId = callHistoryId;
        MainCallHistoryId = mainCallHistoryId;
        SegmentId = segmentId;
    }

    public void Merge(
        int? pbxCallId = null,
        string? cdrId = null,
        string? callHistoryId = null,
        string? mainCallHistoryId = null,
        int? segmentId = null)
    {
        PbxCallId ??= pbxCallId;
        CdrId ??= string.IsNullOrWhiteSpace(cdrId) ? null : cdrId;
        CallHistoryId ??= string.IsNullOrWhiteSpace(callHistoryId) ? null : callHistoryId;
        MainCallHistoryId ??= string.IsNullOrWhiteSpace(mainCallHistoryId) ? null : mainCallHistoryId;
        SegmentId ??= segmentId;
    }
}

public sealed class QueueReconciliationMarker
{
    private readonly HashSet<string> _reasons = new(StringComparer.OrdinalIgnoreCase);

    public bool IsRequired => _reasons.Count > 0;
    public DateTimeOffset? FirstMarkedAtUtc { get; private set; }
    public DateTimeOffset? LastMarkedAtUtc { get; private set; }
    public IReadOnlyCollection<string> Reasons => _reasons;

    public void Mark(string reason, DateTimeOffset atUtc)
    {
        var normalized = QueueDomainGuard.Required(reason, nameof(reason));
        _reasons.Add(normalized);
        FirstMarkedAtUtc ??= atUtc;
        LastMarkedAtUtc = atUtc;
    }

    public void Clear()
    {
        _reasons.Clear();
        FirstMarkedAtUtc = null;
        LastMarkedAtUtc = null;
    }
}

public sealed class QueueCallTimeline
{
    public DateTimeOffset? QueuedAtUtc { get; private set; }
    public DateTimeOffset? OfferedAtUtc { get; private set; }
    public DateTimeOffset? AnsweredAtUtc { get; private set; }
    public DateTimeOffset? AbandonedAtUtc { get; private set; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }
    public DateTimeOffset? LastMissedAtUtc { get; private set; }
    public DateTimeOffset LastObservedAtUtc { get; private set; }

    public void Observe(DateTimeOffset occurredAtUtc)
    {
        if (occurredAtUtc > LastObservedAtUtc)
        {
            LastObservedAtUtc = occurredAtUtc;
        }
    }

    public void MarkQueued(DateTimeOffset atUtc)
    {
        QueuedAtUtc ??= atUtc;
        Observe(atUtc);
    }

    public void MarkOffered(DateTimeOffset atUtc)
    {
        OfferedAtUtc ??= atUtc;
        Observe(atUtc);
    }

    public void MarkAnswered(DateTimeOffset atUtc)
    {
        AnsweredAtUtc ??= atUtc;
        Observe(atUtc);
    }

    public void MarkMissed(DateTimeOffset atUtc)
    {
        LastMissedAtUtc = atUtc;
        Observe(atUtc);
    }

    public void MarkAbandoned(DateTimeOffset atUtc)
    {
        AbandonedAtUtc ??= atUtc;
        Observe(atUtc);
    }

    public void MarkCompleted(DateTimeOffset atUtc)
    {
        CompletedAtUtc ??= atUtc;
        Observe(atUtc);
    }
}

public sealed class QueueCallDurationTotals
{
    public long? WaitingMs { get; private set; }
    public long? RingingMs { get; private set; }
    public long? TalkingMs { get; private set; }
    public long? WrapUpMs { get; private set; }

    public void SetWaiting(long? value)
    {
        WaitingMs = value is null ? null : QueueDomainGuard.NonNegative(value.Value, nameof(WaitingMs));
    }

    public void SetRinging(long? value)
    {
        RingingMs = value is null ? null : QueueDomainGuard.NonNegative(value.Value, nameof(RingingMs));
    }

    public void SetTalking(long? value)
    {
        TalkingMs = value is null ? null : QueueDomainGuard.NonNegative(value.Value, nameof(TalkingMs));
    }

    public void SetWrapUp(long? value)
    {
        WrapUpMs = value is null ? null : QueueDomainGuard.NonNegative(value.Value, nameof(WrapUpMs));
    }
}
