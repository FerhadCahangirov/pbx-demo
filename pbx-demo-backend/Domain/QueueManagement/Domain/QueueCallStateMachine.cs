using pbx_demo_backend.Domain.QueueManagement.Contracts;

namespace pbx_demo_backend.Domain.QueueManagement.Domain;

public enum QueueCallTransitionType
{
    EnteredQueue = 0,
    Waiting = 1,
    Ringing = 2,
    Answered = 3,
    Transferred = 4,
    Missed = 5,
    Abandoned = 6,
    Completed = 7
}

public sealed class QueueCallTransitionCommand
{
    public QueueCallTransitionType TransitionType { get; init; }
    public DateTimeOffset OccurredAtUtc { get; init; }
    public long? QueueId { get; init; }
    public long? AgentId { get; init; }
    public int? WaitOrder { get; init; }
    public string Source { get; init; } = string.Empty;
    public long? SequenceNo { get; init; }
    public string? ExternalEventId { get; init; }
}

public sealed class QueueCallTransitionDecision
{
    public bool Accepted { get; init; }
    public bool IsDuplicate { get; init; }
    public bool RequiresReconciliation { get; init; }
    public string? ReconciliationReason { get; init; }
    public QueueCallLifecycleStatus PreviousStatus { get; init; }
    public QueueCallLifecycleStatus NextStatus { get; init; }
}

public sealed class QueueCallStateMachine
{
    private static readonly HashSet<QueueCallLifecycleStatus> TerminalStates =
    [
        QueueCallLifecycleStatus.Completed,
        QueueCallLifecycleStatus.Abandoned
    ];

    public QueueCallTransitionDecision Evaluate(QueueCallAggregate aggregate, QueueCallTransitionCommand command)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        ArgumentNullException.ThrowIfNull(command);

        if (command.OccurredAtUtc == default)
        {
            throw new QueueDomainValidationException("Transition command OccurredAtUtc is required.");
        }

        var previous = aggregate.Status;

        // Duplicate state observation.
        if (Map(command.TransitionType) == previous)
        {
            return new QueueCallTransitionDecision
            {
                Accepted = true,
                IsDuplicate = true,
                PreviousStatus = previous,
                NextStatus = previous,
                RequiresReconciliation = command.OccurredAtUtc < aggregate.Timeline.LastObservedAtUtc,
                ReconciliationReason = command.OccurredAtUtc < aggregate.Timeline.LastObservedAtUtc
                    ? "Duplicate state observed out-of-order."
                    : null
            };
        }

        var isOutOfOrder = command.OccurredAtUtc < aggregate.Timeline.LastObservedAtUtc;
        var next = ResolveNextStatus(previous, command.TransitionType);

        if (next is null)
        {
            return new QueueCallTransitionDecision
            {
                Accepted = false,
                IsDuplicate = false,
                PreviousStatus = previous,
                NextStatus = previous,
                RequiresReconciliation = true,
                ReconciliationReason = $"Invalid transition {previous} -> {command.TransitionType}."
            };
        }

        var reconciliationReason = GetReconciliationReason(previous, command.TransitionType, isOutOfOrder);

        return new QueueCallTransitionDecision
        {
            Accepted = true,
            IsDuplicate = false,
            PreviousStatus = previous,
            NextStatus = next.Value,
            RequiresReconciliation = reconciliationReason is not null,
            ReconciliationReason = reconciliationReason
        };
    }

    private static QueueCallLifecycleStatus? ResolveNextStatus(
        QueueCallLifecycleStatus previous,
        QueueCallTransitionType transitionType)
    {
        if (TerminalStates.Contains(previous))
        {
            return transitionType switch
            {
                QueueCallTransitionType.Completed when previous == QueueCallLifecycleStatus.Completed => previous,
                QueueCallTransitionType.Abandoned when previous == QueueCallLifecycleStatus.Abandoned => previous,
                _ => null
            };
        }

        return (previous, transitionType) switch
        {
            (QueueCallLifecycleStatus.Unknown, QueueCallTransitionType.EnteredQueue) => QueueCallLifecycleStatus.EnteredQueue,
            (QueueCallLifecycleStatus.Unknown, QueueCallTransitionType.Waiting) => QueueCallLifecycleStatus.Waiting,
            (QueueCallLifecycleStatus.Unknown, QueueCallTransitionType.Ringing) => QueueCallLifecycleStatus.Ringing,
            (QueueCallLifecycleStatus.Unknown, QueueCallTransitionType.Answered) => QueueCallLifecycleStatus.Answered,
            (QueueCallLifecycleStatus.Unknown, QueueCallTransitionType.Abandoned) => QueueCallLifecycleStatus.Abandoned,
            (QueueCallLifecycleStatus.Unknown, QueueCallTransitionType.Completed) => QueueCallLifecycleStatus.Completed,

            (QueueCallLifecycleStatus.EnteredQueue, QueueCallTransitionType.Waiting) => QueueCallLifecycleStatus.Waiting,
            (QueueCallLifecycleStatus.EnteredQueue, QueueCallTransitionType.Ringing) => QueueCallLifecycleStatus.Ringing,
            (QueueCallLifecycleStatus.EnteredQueue, QueueCallTransitionType.Answered) => QueueCallLifecycleStatus.Answered,
            (QueueCallLifecycleStatus.EnteredQueue, QueueCallTransitionType.Abandoned) => QueueCallLifecycleStatus.Abandoned,
            (QueueCallLifecycleStatus.EnteredQueue, QueueCallTransitionType.Completed) => QueueCallLifecycleStatus.Completed,

            (QueueCallLifecycleStatus.Waiting, QueueCallTransitionType.Ringing) => QueueCallLifecycleStatus.Ringing,
            (QueueCallLifecycleStatus.Waiting, QueueCallTransitionType.Answered) => QueueCallLifecycleStatus.Answered,
            (QueueCallLifecycleStatus.Waiting, QueueCallTransitionType.Missed) => QueueCallLifecycleStatus.Missed,
            (QueueCallLifecycleStatus.Waiting, QueueCallTransitionType.Abandoned) => QueueCallLifecycleStatus.Abandoned,
            (QueueCallLifecycleStatus.Waiting, QueueCallTransitionType.Completed) => QueueCallLifecycleStatus.Completed,

            (QueueCallLifecycleStatus.Ringing, QueueCallTransitionType.Answered) => QueueCallLifecycleStatus.Answered,
            (QueueCallLifecycleStatus.Ringing, QueueCallTransitionType.Missed) => QueueCallLifecycleStatus.Missed,
            (QueueCallLifecycleStatus.Ringing, QueueCallTransitionType.Transferred) => QueueCallLifecycleStatus.Transferred,
            (QueueCallLifecycleStatus.Ringing, QueueCallTransitionType.Abandoned) => QueueCallLifecycleStatus.Abandoned,
            (QueueCallLifecycleStatus.Ringing, QueueCallTransitionType.Completed) => QueueCallLifecycleStatus.Completed,

            (QueueCallLifecycleStatus.Answered, QueueCallTransitionType.Transferred) => QueueCallLifecycleStatus.Transferred,
            (QueueCallLifecycleStatus.Answered, QueueCallTransitionType.Completed) => QueueCallLifecycleStatus.Completed,

            (QueueCallLifecycleStatus.Transferred, QueueCallTransitionType.Waiting) => QueueCallLifecycleStatus.Waiting,
            (QueueCallLifecycleStatus.Transferred, QueueCallTransitionType.Ringing) => QueueCallLifecycleStatus.Ringing,
            (QueueCallLifecycleStatus.Transferred, QueueCallTransitionType.Answered) => QueueCallLifecycleStatus.Answered,
            (QueueCallLifecycleStatus.Transferred, QueueCallTransitionType.Missed) => QueueCallLifecycleStatus.Missed,
            (QueueCallLifecycleStatus.Transferred, QueueCallTransitionType.Abandoned) => QueueCallLifecycleStatus.Abandoned,
            (QueueCallLifecycleStatus.Transferred, QueueCallTransitionType.Completed) => QueueCallLifecycleStatus.Completed,

            (QueueCallLifecycleStatus.Missed, QueueCallTransitionType.Waiting) => QueueCallLifecycleStatus.Waiting,
            (QueueCallLifecycleStatus.Missed, QueueCallTransitionType.Ringing) => QueueCallLifecycleStatus.Ringing,
            (QueueCallLifecycleStatus.Missed, QueueCallTransitionType.Answered) => QueueCallLifecycleStatus.Answered,
            (QueueCallLifecycleStatus.Missed, QueueCallTransitionType.Abandoned) => QueueCallLifecycleStatus.Abandoned,
            (QueueCallLifecycleStatus.Missed, QueueCallTransitionType.Completed) => QueueCallLifecycleStatus.Completed,

            _ => null
        };
    }

    private static QueueCallLifecycleStatus Map(QueueCallTransitionType transitionType)
    {
        return transitionType switch
        {
            QueueCallTransitionType.EnteredQueue => QueueCallLifecycleStatus.EnteredQueue,
            QueueCallTransitionType.Waiting => QueueCallLifecycleStatus.Waiting,
            QueueCallTransitionType.Ringing => QueueCallLifecycleStatus.Ringing,
            QueueCallTransitionType.Answered => QueueCallLifecycleStatus.Answered,
            QueueCallTransitionType.Transferred => QueueCallLifecycleStatus.Transferred,
            QueueCallTransitionType.Missed => QueueCallLifecycleStatus.Missed,
            QueueCallTransitionType.Abandoned => QueueCallLifecycleStatus.Abandoned,
            QueueCallTransitionType.Completed => QueueCallLifecycleStatus.Completed,
            _ => QueueCallLifecycleStatus.Unknown
        };
    }

    private static string? GetReconciliationReason(
        QueueCallLifecycleStatus previous,
        QueueCallTransitionType transitionType,
        bool isOutOfOrder)
    {
        if (isOutOfOrder)
        {
            return "Event observed out-of-order relative to previously applied events.";
        }

        return (previous, transitionType) switch
        {
            (QueueCallLifecycleStatus.Unknown, QueueCallTransitionType.Waiting) => "Waiting observed before explicit queue-entry event.",
            (QueueCallLifecycleStatus.Unknown, QueueCallTransitionType.Ringing) => "Ringing observed before queue-entry/waiting event.",
            (QueueCallLifecycleStatus.Unknown, QueueCallTransitionType.Answered) => "Answer observed before ring/wait event.",
            (QueueCallLifecycleStatus.Unknown, QueueCallTransitionType.Abandoned) => "Abandon observed before queue-entry event.",
            (QueueCallLifecycleStatus.Unknown, QueueCallTransitionType.Completed) => "Completion observed before prior lifecycle events.",
            (QueueCallLifecycleStatus.EnteredQueue, QueueCallTransitionType.Answered) => "Answer observed without intermediate waiting/ringing states.",
            (QueueCallLifecycleStatus.Waiting, QueueCallTransitionType.Answered) => "Answer observed without explicit ringing state.",
            _ => null
        };
    }
}
