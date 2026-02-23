namespace pbx_demo_backend.Domain.QueueManagement.Domain;

public sealed record QueueCreatedDomainEvent(
    long QueueId,
    Guid TenantId,
    int PbxQueueId,
    string QueueNumber,
    string Name,
    DateTimeOffset OccurredAtUtc) : QueueDomainEventBase(OccurredAtUtc);

public sealed record QueueRenamedDomainEvent(
    long QueueId,
    string OldName,
    string NewName,
    DateTimeOffset OccurredAtUtc) : QueueDomainEventBase(OccurredAtUtc);

public sealed record QueueSettingsChangedDomainEvent(
    long QueueId,
    DateTimeOffset OccurredAtUtc) : QueueDomainEventBase(OccurredAtUtc);

public sealed record QueueAgentsReplacedDomainEvent(
    long QueueId,
    int AgentCount,
    int ManagerCount,
    DateTimeOffset OccurredAtUtc) : QueueDomainEventBase(OccurredAtUtc);

public sealed record QueueDeletedDomainEvent(
    long QueueId,
    DateTimeOffset OccurredAtUtc) : QueueDomainEventBase(OccurredAtUtc);

public sealed record QueueCallLifecycleChangedDomainEvent(
    QueueCallKey CallKey,
    long? QueueId,
    string PreviousStatus,
    string CurrentStatus,
    DateTimeOffset OccurredAtUtc) : QueueDomainEventBase(OccurredAtUtc);

public sealed record QueueCallAnsweredDomainEvent(
    QueueCallKey CallKey,
    long? QueueId,
    long? AgentId,
    DateTimeOffset OccurredAtUtc) : QueueDomainEventBase(OccurredAtUtc);

public sealed record QueueCallCompletedDomainEvent(
    QueueCallKey CallKey,
    long? QueueId,
    string Disposition,
    long? WaitingMs,
    long? TalkingMs,
    DateTimeOffset OccurredAtUtc) : QueueDomainEventBase(OccurredAtUtc);

public sealed record QueueCallTransferredDomainEvent(
    QueueCallKey CallKey,
    long? QueueId,
    long? AgentId,
    int TransferCount,
    DateTimeOffset OccurredAtUtc) : QueueDomainEventBase(OccurredAtUtc);

public sealed record QueueCallMarkedForReconciliationDomainEvent(
    QueueCallKey CallKey,
    string Reason,
    DateTimeOffset OccurredAtUtc) : QueueDomainEventBase(OccurredAtUtc);

public sealed record QueueAgentStatusChangedDomainEvent(
    long QueueId,
    long AgentId,
    string PreviousStatus,
    string CurrentStatus,
    DateTimeOffset OccurredAtUtc) : QueueDomainEventBase(OccurredAtUtc);
