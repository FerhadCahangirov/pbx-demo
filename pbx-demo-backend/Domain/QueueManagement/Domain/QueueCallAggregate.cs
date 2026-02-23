using pbx_demo_backend.Domain.QueueManagement.Contracts;

namespace pbx_demo_backend.Domain.QueueManagement.Domain;

public sealed class QueueCallAggregate : QueueAggregateRoot
{
    public QueueCallKey CallKey { get; private set; }
    public long? QueueId { get; private set; }
    public long? AnsweredByAgentId { get; private set; }
    public long? CurrentAgentId { get; private set; }
    public QueueCorrelationIds CorrelationIds { get; } = new();
    public QueueCallLifecycleStatus Status { get; private set; } = QueueCallLifecycleStatus.Unknown;
    public QueueCallDisposition Disposition { get; private set; } = QueueCallDisposition.Unknown;
    public int TransferCount { get; private set; }
    public int? WaitOrder { get; private set; }
    public QueueSlaThreshold? SlaThreshold { get; private set; }
    public bool? SlaBreached { get; private set; }
    public QueueCallTimeline Timeline { get; } = new();
    public QueueCallDurationTotals Durations { get; } = new();
    public QueueReconciliationMarker Reconciliation { get; } = new();

    public string? CallerNumber { get; private set; }
    public string? CallerName { get; private set; }
    public string? CalleeNumber { get; private set; }
    public string? CalleeName { get; private set; }
    public string? Direction { get; private set; }

    private QueueCallAggregate(QueueCallKey callKey, DateTimeOffset occurredAtUtc)
    {
        CallKey = callKey;
        Timeline.Observe(occurredAtUtc);
        LastModifiedAtUtc = occurredAtUtc;
    }

    public static QueueCallAggregate Create(QueueCallKey callKey, DateTimeOffset occurredAtUtc)
    {
        return new QueueCallAggregate(callKey, occurredAtUtc);
    }

    public void SetQueue(long? queueId)
    {
        if (queueId is not null && queueId.Value <= 0)
        {
            throw new QueueDomainValidationException("QueueId must be greater than zero when specified.");
        }

        QueueId = queueId;
    }

    public void SetSlaThresholdSeconds(int? slaThresholdSec)
    {
        SlaThreshold = slaThresholdSec is null ? null : new QueueSlaThreshold(slaThresholdSec.Value);
    }

    public void SetPartyInfo(
        string? callerNumber,
        string? callerName,
        string? calleeNumber,
        string? calleeName,
        string? direction)
    {
        CallerNumber = Normalize(callerNumber);
        CallerName = Normalize(callerName);
        CalleeNumber = Normalize(calleeNumber);
        CalleeName = Normalize(calleeName);
        Direction = Normalize(direction);
    }

    public void MergeCorrelationIds(
        int? pbxCallId = null,
        string? cdrId = null,
        string? callHistoryId = null,
        string? mainCallHistoryId = null,
        int? segmentId = null)
    {
        CorrelationIds.Merge(pbxCallId, cdrId, callHistoryId, mainCallHistoryId, segmentId);
    }

    public QueueCallTransitionDecision Apply(QueueCallStateMachine stateMachine, QueueCallTransitionCommand command)
    {
        ArgumentNullException.ThrowIfNull(stateMachine);
        ArgumentNullException.ThrowIfNull(command);

        if (command.QueueId is not null)
        {
            SetQueue(command.QueueId);
        }

        var decision = stateMachine.Evaluate(this, command);
        if (!decision.Accepted)
        {
            MarkForReconciliation(decision.ReconciliationReason ?? "State transition rejected.", command.OccurredAtUtc);
            return decision;
        }

        if (decision.IsDuplicate)
        {
            Timeline.Observe(command.OccurredAtUtc);
            if (decision.RequiresReconciliation && decision.ReconciliationReason is not null)
            {
                MarkForReconciliation(decision.ReconciliationReason, command.OccurredAtUtc);
            }
            return decision;
        }

        ApplyTransitionMutation(command, decision.NextStatus);

        if (decision.RequiresReconciliation && decision.ReconciliationReason is not null)
        {
            MarkForReconciliation(decision.ReconciliationReason, command.OccurredAtUtc);
        }

        RaiseLifecycleEvents(command, decision.PreviousStatus, decision.NextStatus);
        RecalculateDurations();
        EvaluateSlaBreach();

        return decision;
    }

    public void SetFinalDurations(long? waitingMs, long? talkingMs, long? ringingMs = null, long? wrapUpMs = null)
    {
        Durations.SetWaiting(waitingMs);
        Durations.SetTalking(talkingMs);
        Durations.SetRinging(ringingMs);
        Durations.SetWrapUp(wrapUpMs);
        EvaluateSlaBreach();
    }

    public void MarkReconciled()
    {
        Reconciliation.Clear();
    }

    private void ApplyTransitionMutation(QueueCallTransitionCommand command, QueueCallLifecycleStatus nextStatus)
    {
        Status = nextStatus;
        Timeline.Observe(command.OccurredAtUtc);

        if (command.WaitOrder is not null)
        {
            if (command.WaitOrder.Value < 0)
            {
                throw new QueueDomainValidationException("WaitOrder cannot be negative.");
            }

            WaitOrder = command.WaitOrder.Value;
        }

        switch (command.TransitionType)
        {
            case QueueCallTransitionType.EnteredQueue:
                Timeline.MarkQueued(command.OccurredAtUtc);
                break;

            case QueueCallTransitionType.Waiting:
                Timeline.MarkQueued(command.OccurredAtUtc);
                break;

            case QueueCallTransitionType.Ringing:
                Timeline.MarkQueued(command.OccurredAtUtc);
                Timeline.MarkOffered(command.OccurredAtUtc);
                CurrentAgentId = command.AgentId ?? CurrentAgentId;
                break;

            case QueueCallTransitionType.Answered:
                Timeline.MarkQueued(command.OccurredAtUtc);
                Timeline.MarkAnswered(command.OccurredAtUtc);
                CurrentAgentId = command.AgentId ?? CurrentAgentId;
                AnsweredByAgentId ??= CurrentAgentId;
                Disposition = QueueCallDisposition.Answered;
                break;

            case QueueCallTransitionType.Transferred:
                TransferCount++;
                CurrentAgentId = command.AgentId ?? CurrentAgentId;
                break;

            case QueueCallTransitionType.Missed:
                Timeline.MarkMissed(command.OccurredAtUtc);
                Disposition = QueueCallDisposition.Missed;
                break;

            case QueueCallTransitionType.Abandoned:
                Timeline.MarkQueued(command.OccurredAtUtc);
                Timeline.MarkAbandoned(command.OccurredAtUtc);
                Disposition = QueueCallDisposition.Abandoned;
                break;

            case QueueCallTransitionType.Completed:
                Timeline.MarkCompleted(command.OccurredAtUtc);
                if (Disposition == QueueCallDisposition.Unknown)
                {
                    Disposition = Timeline.AnsweredAtUtc is not null
                        ? QueueCallDisposition.Answered
                        : QueueCallDisposition.Completed;
                }
                break;
        }
    }

    private void RaiseLifecycleEvents(
        QueueCallTransitionCommand command,
        QueueCallLifecycleStatus previousStatus,
        QueueCallLifecycleStatus nextStatus)
    {
        Raise(new QueueCallLifecycleChangedDomainEvent(
            CallKey,
            QueueId,
            previousStatus.ToString(),
            nextStatus.ToString(),
            command.OccurredAtUtc));

        if (command.TransitionType == QueueCallTransitionType.Answered)
        {
            Raise(new QueueCallAnsweredDomainEvent(CallKey, QueueId, command.AgentId ?? CurrentAgentId, command.OccurredAtUtc));
        }

        if (command.TransitionType == QueueCallTransitionType.Transferred)
        {
            Raise(new QueueCallTransferredDomainEvent(CallKey, QueueId, command.AgentId ?? CurrentAgentId, TransferCount, command.OccurredAtUtc));
        }

        if (command.TransitionType is QueueCallTransitionType.Completed or QueueCallTransitionType.Abandoned)
        {
            Raise(new QueueCallCompletedDomainEvent(
                CallKey,
                QueueId,
                Disposition.ToString(),
                Durations.WaitingMs,
                Durations.TalkingMs,
                command.OccurredAtUtc));
        }
    }

    private void RecalculateDurations()
    {
        var queueStart = Timeline.QueuedAtUtc;
        var waitEnd = Timeline.AnsweredAtUtc ?? Timeline.AbandonedAtUtc;
        if (queueStart is not null && waitEnd is not null && waitEnd >= queueStart)
        {
            Durations.SetWaiting((long)(waitEnd.Value - queueStart.Value).TotalMilliseconds);
        }

        if (Timeline.OfferedAtUtc is not null && Timeline.AnsweredAtUtc is not null && Timeline.AnsweredAtUtc >= Timeline.OfferedAtUtc)
        {
            Durations.SetRinging((long)(Timeline.AnsweredAtUtc.Value - Timeline.OfferedAtUtc.Value).TotalMilliseconds);
        }

        if (Timeline.AnsweredAtUtc is not null && Timeline.CompletedAtUtc is not null && Timeline.CompletedAtUtc >= Timeline.AnsweredAtUtc)
        {
            Durations.SetTalking((long)(Timeline.CompletedAtUtc.Value - Timeline.AnsweredAtUtc.Value).TotalMilliseconds);
        }
    }

    private void EvaluateSlaBreach()
    {
        if (SlaThreshold is null || Durations.WaitingMs is null)
        {
            SlaBreached = null;
            return;
        }

        SlaBreached = Durations.WaitingMs.Value > SlaThreshold.Value.Seconds * 1000L;
    }

    private void MarkForReconciliation(string reason, DateTimeOffset occurredAtUtc)
    {
        Reconciliation.Mark(reason, occurredAtUtc);
        Raise(new QueueCallMarkedForReconciliationDomainEvent(CallKey, reason, occurredAtUtc));
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
