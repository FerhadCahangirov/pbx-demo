namespace pbx_demo_backend.Domain.QueueManagement.Domain;

public interface IQueueDomainEvent
{
    Guid EventId { get; }
    DateTimeOffset OccurredAtUtc { get; }
    string EventType { get; }
}

public abstract record QueueDomainEventBase(DateTimeOffset OccurredAtUtc) : IQueueDomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public virtual string EventType => GetType().Name;
}

public abstract class QueueAggregateRoot
{
    private readonly List<IQueueDomainEvent> _domainEvents = [];

    public long Version { get; protected set; }
    public DateTimeOffset LastModifiedAtUtc { get; protected set; } = DateTimeOffset.UtcNow;

    public IReadOnlyCollection<IQueueDomainEvent> DomainEvents => _domainEvents;

    protected void Raise(IQueueDomainEvent @event)
    {
        _domainEvents.Add(@event);
        LastModifiedAtUtc = @event.OccurredAtUtc;
        Version++;
    }

    public void ClearDomainEvents() => _domainEvents.Clear();
}

public class QueueDomainException : InvalidOperationException
{
    public QueueDomainException(string message) : base(message)
    {
    }
}

public sealed class QueueDomainValidationException : QueueDomainException
{
    public QueueDomainValidationException(string message) : base(message)
    {
    }
}

public static class QueueDomainGuard
{
    public static string Required(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new QueueDomainValidationException($"{name} is required.");
        }

        return value.Trim();
    }

    public static int NonNegative(int value, string name)
    {
        if (value < 0)
        {
            throw new QueueDomainValidationException($"{name} cannot be negative.");
        }

        return value;
    }

    public static long NonNegative(long value, string name)
    {
        if (value < 0)
        {
            throw new QueueDomainValidationException($"{name} cannot be negative.");
        }

        return value;
    }
}
