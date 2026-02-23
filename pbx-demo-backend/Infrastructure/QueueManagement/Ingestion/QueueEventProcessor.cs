using System.Text;
using CallControl.Api.Domain;
using CallControl.Api.Infrastructure.QueueManagement.Persistence;
using Microsoft.EntityFrameworkCore;
using pbx_demo_backend.Domain.QueueManagement.Contracts;

namespace CallControl.Api.Infrastructure.QueueManagement.Ingestion;

public sealed class QueueEventProcessor : IQueueEventProcessor
{
    private readonly PBXDbContext _db;
    private readonly QueueEventIdempotencyKeyFactory _idempotencyKeys;
    private readonly ILogger<QueueEventProcessor> _logger;

    public QueueEventProcessor(
        PBXDbContext db,
        QueueEventIdempotencyKeyFactory idempotencyKeys,
        ILogger<QueueEventProcessor> logger)
    {
        _db = db;
        _idempotencyKeys = idempotencyKeys;
        _logger = logger;
    }

    public Task ProcessAsync(QueueInboundEventEnvelope envelope, CancellationToken ct)
        => ProcessBatchAsync([envelope], ct);

    public async Task ProcessBatchAsync(IReadOnlyList<QueueInboundEventEnvelope> batch, CancellationToken ct)
    {
        if (batch is null)
        {
            throw new ArgumentNullException(nameof(batch));
        }

        if (batch.Count == 0)
        {
            return;
        }

        foreach (var envelope in batch)
        {
            ct.ThrowIfCancellationRequested();
            await PersistEnvelopeAsync(envelope, ct);
        }
    }

    private async Task PersistEnvelopeAsync(QueueInboundEventEnvelope envelope, CancellationToken ct)
    {
        if (envelope is null)
        {
            return;
        }

        var source = NormalizeRequired(envelope.Source, nameof(envelope.Source));
        var eventType = NormalizeRequired(envelope.EventType, nameof(envelope.EventType));
        var orderingKey = NormalizeRequired(envelope.OrderingKey, nameof(envelope.OrderingKey));
        var payloadJson = string.IsNullOrWhiteSpace(envelope.PayloadJson) ? "{}" : envelope.PayloadJson;
        var idempotencyKey = _idempotencyKeys.CreateFromEnvelope(envelope);

        if (await _db.QueueCallEvents.AnyAsync(x => x.IdempotencyKey == idempotencyKey, ct))
        {
            return;
        }

        var nowUtc = DateTimeOffset.UtcNow;
        var eventAtUtc = envelope.EventAtUtc == default ? nowUtc : envelope.EventAtUtc;
        var entity = new QueueCallEventEntity
        {
            Source = Truncate(source, 32),
            EventType = Truncate(eventType, 64),
            OrderingKey = Truncate(orderingKey, 256),
            EventAtUtc = eventAtUtc,
            ObservedAtUtc = nowUtc,
            IdempotencyKey = Truncate(idempotencyKey, 128),
            PayloadHash = ComputePayloadHash(payloadJson),
            PayloadJson = payloadJson,
            ProcessingStatus = QueueCallEventProcessingStatus.Pending,
            NextAttemptAtUtc = null,
            LastAttemptAtUtc = null,
            ProcessingAttemptCount = 0
        };

        _db.QueueCallEvents.Add(entity);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsLikelyUniqueViolation(ex))
        {
                _logger.LogDebug(
                    ex,
                    "Queue event inbox duplicate insert ignored. Source={Source}, EventType={EventType}, IdempotencyKey={IdempotencyKey}.",
                    source,
                    eventType,
                    idempotencyKey);
            _db.Entry(entity).State = EntityState.Detached;
        }
    }

    private static byte[] ComputePayloadHash(string payloadJson)
        => System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(payloadJson));

    private static string NormalizeRequired(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new BadRequestException($"Queue inbound event {name} is required.");
        }

        return value.Trim();
    }

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];

    private static bool IsLikelyUniqueViolation(DbUpdateException ex)
    {
        var message = ex.InnerException?.Message ?? ex.Message;
        return message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase)
            || message.Contains("duplicate", StringComparison.OrdinalIgnoreCase)
            || message.Contains("2601", StringComparison.OrdinalIgnoreCase)
            || message.Contains("2627", StringComparison.OrdinalIgnoreCase);
    }
}
