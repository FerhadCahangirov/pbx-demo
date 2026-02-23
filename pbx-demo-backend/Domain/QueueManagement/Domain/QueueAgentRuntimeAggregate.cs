namespace pbx_demo_backend.Domain.QueueManagement.Domain;

public enum QueueAgentRuntimeStatus
{
    Unknown = 0,
    LoggedOut = 1,
    LoggedIn = 2,
    Available = 3,
    Ringing = 4,
    Talking = 5,
    WrapUp = 6
}

public sealed class QueueAgentRuntimeAggregate : QueueAggregateRoot
{
    public long QueueId { get; private set; }
    public long AgentId { get; private set; }
    public string ExtensionNumber { get; private set; } = string.Empty;
    public QueueAgentRuntimeStatus Status { get; private set; } = QueueAgentRuntimeStatus.Unknown;
    public QueueCallKey? CurrentCallKey { get; private set; }
    public DateTimeOffset? LoggedInAtUtc { get; private set; }
    public DateTimeOffset? LastStatusChangedAtUtc { get; private set; }
    public long AccumulatedTalkingMs { get; private set; }
    public long AccumulatedWrapUpMs { get; private set; }
    public QueueReconciliationMarker Reconciliation { get; } = new();

    private DateTimeOffset? _currentTalkingStartedAtUtc;
    private DateTimeOffset? _currentWrapUpStartedAtUtc;

    private QueueAgentRuntimeAggregate()
    {
    }

    public static QueueAgentRuntimeAggregate Create(long queueId, long agentId, string extensionNumber)
    {
        if (queueId <= 0)
        {
            throw new QueueDomainValidationException("QueueId must be greater than zero.");
        }

        if (agentId <= 0)
        {
            throw new QueueDomainValidationException("AgentId must be greater than zero.");
        }

        return new QueueAgentRuntimeAggregate
        {
            QueueId = queueId,
            AgentId = agentId,
            ExtensionNumber = QueueDomainGuard.Required(extensionNumber, nameof(extensionNumber))
        };
    }

    public void Login(DateTimeOffset occurredAtUtc)
    {
        TransitionTo(QueueAgentRuntimeStatus.LoggedIn, occurredAtUtc);
        LoggedInAtUtc ??= occurredAtUtc;
        CurrentCallKey = null;
    }

    public void MarkAvailable(DateTimeOffset occurredAtUtc)
    {
        if (Status == QueueAgentRuntimeStatus.LoggedOut)
        {
            MarkOutOfOrder("Available observed while logged out.", occurredAtUtc);
        }

        StopTalkingIfAny(occurredAtUtc);
        StopWrapUpIfAny(occurredAtUtc);
        CurrentCallKey = null;
        TransitionTo(QueueAgentRuntimeStatus.Available, occurredAtUtc);
    }

    public void OfferCall(QueueCallKey callKey, DateTimeOffset occurredAtUtc)
    {
        if (Status == QueueAgentRuntimeStatus.LoggedOut)
        {
            MarkOutOfOrder("Call offer observed while agent logged out.", occurredAtUtc);
        }

        CurrentCallKey = callKey;
        TransitionTo(QueueAgentRuntimeStatus.Ringing, occurredAtUtc);
    }

    public void AnswerCall(QueueCallKey callKey, DateTimeOffset occurredAtUtc)
    {
        CurrentCallKey = callKey;
        StopWrapUpIfAny(occurredAtUtc);
        _currentTalkingStartedAtUtc ??= occurredAtUtc;
        TransitionTo(QueueAgentRuntimeStatus.Talking, occurredAtUtc);
    }

    public void StartWrapUp(DateTimeOffset occurredAtUtc)
    {
        StopTalkingIfAny(occurredAtUtc);
        _currentWrapUpStartedAtUtc ??= occurredAtUtc;
        TransitionTo(QueueAgentRuntimeStatus.WrapUp, occurredAtUtc);
    }

    public void EndWrapUp(DateTimeOffset occurredAtUtc)
    {
        StopWrapUpIfAny(occurredAtUtc);
        CurrentCallKey = null;
        TransitionTo(QueueAgentRuntimeStatus.Available, occurredAtUtc);
    }

    public void Logout(DateTimeOffset occurredAtUtc)
    {
        StopTalkingIfAny(occurredAtUtc);
        StopWrapUpIfAny(occurredAtUtc);
        CurrentCallKey = null;
        TransitionTo(QueueAgentRuntimeStatus.LoggedOut, occurredAtUtc);
    }

    private void TransitionTo(QueueAgentRuntimeStatus nextStatus, DateTimeOffset occurredAtUtc)
    {
        var previous = Status;
        if (previous == nextStatus)
        {
            return;
        }

        Status = nextStatus;
        LastStatusChangedAtUtc = occurredAtUtc;
        Raise(new QueueAgentStatusChangedDomainEvent(
            QueueId,
            AgentId,
            previous.ToString(),
            nextStatus.ToString(),
            occurredAtUtc));
    }

    private void StopTalkingIfAny(DateTimeOffset occurredAtUtc)
    {
        if (_currentTalkingStartedAtUtc is null)
        {
            return;
        }

        if (occurredAtUtc < _currentTalkingStartedAtUtc.Value)
        {
            MarkOutOfOrder("Talking end observed before talking start.", occurredAtUtc);
            _currentTalkingStartedAtUtc = null;
            return;
        }

        AccumulatedTalkingMs += (long)(occurredAtUtc - _currentTalkingStartedAtUtc.Value).TotalMilliseconds;
        _currentTalkingStartedAtUtc = null;
    }

    private void StopWrapUpIfAny(DateTimeOffset occurredAtUtc)
    {
        if (_currentWrapUpStartedAtUtc is null)
        {
            return;
        }

        if (occurredAtUtc < _currentWrapUpStartedAtUtc.Value)
        {
            MarkOutOfOrder("Wrap-up end observed before wrap-up start.", occurredAtUtc);
            _currentWrapUpStartedAtUtc = null;
            return;
        }

        AccumulatedWrapUpMs += (long)(occurredAtUtc - _currentWrapUpStartedAtUtc.Value).TotalMilliseconds;
        _currentWrapUpStartedAtUtc = null;
    }

    private void MarkOutOfOrder(string reason, DateTimeOffset occurredAtUtc)
    {
        Reconciliation.Mark(reason, occurredAtUtc);
    }
}
