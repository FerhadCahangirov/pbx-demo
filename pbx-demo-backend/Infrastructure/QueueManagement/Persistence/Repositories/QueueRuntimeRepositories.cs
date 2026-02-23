using Microsoft.EntityFrameworkCore;
using pbx_demo_backend.Domain.QueueManagement.Contracts;

namespace CallControl.Api.Infrastructure.QueueManagement.Persistence.Repositories;

public interface IQueueCallRepository
{
    Task<QueueCallEntity?> GetByIdAsync(long id, CancellationToken ct);
    Task<QueueCallEntity?> GetByCorrelationKeyAsync(Guid tenantId, string correlationKey, CancellationToken ct);
    Task<QueueCallEntity?> GetByPbxCallIdAsync(Guid tenantId, int pbxCallId, CancellationToken ct);
    Task<QueueCallEntity?> GetByCdrIdAsync(Guid tenantId, string cdrId, CancellationToken ct);
    Task<List<QueueCallEntity>> ListActiveByQueueAsync(Guid tenantId, long queueId, CancellationToken ct);
    void Add(QueueCallEntity entity);
}

public interface IQueueCallEventRepository
{
    Task<bool> ExistsIdempotencyKeyAsync(Guid tenantId, string idempotencyKey, CancellationToken ct);
    Task<List<QueueCallEventEntity>> GetPendingBatchAsync(Guid tenantId, int take, DateTimeOffset nowUtc, CancellationToken ct);
    void Add(QueueCallEventEntity entity);
    void AddRange(IEnumerable<QueueCallEventEntity> entities);
}

public interface IQueueCallHistoryRepository
{
    Task<List<QueueCallHistoryEntity>> ListByQueueAsync(Guid tenantId, long queueId, DateTimeOffset fromUtc, DateTimeOffset toUtc, int take, int skip, CancellationToken ct);
    void Add(QueueCallHistoryEntity entity);
    void AddRange(IEnumerable<QueueCallHistoryEntity> entities);
}

public interface IQueueAgentActivityRepository
{
    Task<List<QueueAgentActivityEntity>> ListByAgentAsync(Guid tenantId, long extensionId, DateTimeOffset fromUtc, DateTimeOffset toUtc, int take, int skip, CancellationToken ct);
    void Add(QueueAgentActivityEntity entity);
    void AddRange(IEnumerable<QueueAgentActivityEntity> entities);
}

public interface IQueueOutboxRepository
{
    Task<List<OutboxMessageEntity>> GetUnpublishedBatchAsync(Guid tenantId, int take, CancellationToken ct);
    void Add(OutboxMessageEntity entity);
    void AddRange(IEnumerable<OutboxMessageEntity> entities);
}

public interface IQueueCheckpointRepository
{
    Task<XapiSyncCheckpointEntity?> GetAsync(Guid tenantId, string streamName, string partitionKey, CancellationToken ct);
    void Add(XapiSyncCheckpointEntity entity);
}

public sealed class QueueRuntimeRepository :
    IQueuePersistenceUnitOfWork,
    IQueueCallRepository,
    IQueueCallEventRepository,
    IQueueCallHistoryRepository,
    IQueueAgentActivityRepository,
    IQueueOutboxRepository,
    IQueueCheckpointRepository
{
    private readonly PBXDbContext _db;

    public QueueRuntimeRepository(PBXDbContext db)
    {
        _db = db;
    }

    public Task<int> SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);

    public async Task<QueueCallEntity?> GetByIdAsync(long id, CancellationToken ct)
    {
        return await _db.QueueCalls.FirstOrDefaultAsync(x => x.Id == id, ct);
    }

    public async Task<QueueCallEntity?> GetByCorrelationKeyAsync(Guid tenantId, string correlationKey, CancellationToken ct)
    {
        return await _db.QueueCalls
            .FirstOrDefaultAsync(x => x.CorrelationKey == correlationKey, ct);
    }

    public async Task<QueueCallEntity?> GetByPbxCallIdAsync(Guid tenantId, int pbxCallId, CancellationToken ct)
    {
        return await _db.QueueCalls
            .FirstOrDefaultAsync(x => x.PbxCallId == pbxCallId, ct);
    }

    public async Task<QueueCallEntity?> GetByCdrIdAsync(Guid tenantId, string cdrId, CancellationToken ct)
    {
        return await _db.QueueCalls
            .FirstOrDefaultAsync(x => x.CdrId == cdrId, ct);
    }

    public async Task<List<QueueCallEntity>> ListActiveByQueueAsync(Guid tenantId, long queueId, CancellationToken ct)
    {
        return await _db.QueueCalls
            .Where(x => x.QueueId == queueId)
            .Where(x =>
                x.CurrentStatus != QueueCallLifecycleStatus.Completed &&
                x.CurrentStatus != QueueCallLifecycleStatus.Abandoned)
            .OrderBy(x => x.QueuedAtUtc)
            .ThenBy(x => x.Id)
            .ToListAsync(ct);
    }

    public void Add(QueueCallEntity entity) => _db.QueueCalls.Add(entity);

    public Task<bool> ExistsIdempotencyKeyAsync(Guid tenantId, string idempotencyKey, CancellationToken ct)
        => _db.QueueCallEvents.AnyAsync(x => x.IdempotencyKey == idempotencyKey, ct);

    public async Task<List<QueueCallEventEntity>> GetPendingBatchAsync(Guid tenantId, int take, DateTimeOffset nowUtc, CancellationToken ct)
    {
        return await _db.QueueCallEvents
            .Where(x =>
                x.ProcessingStatus == QueueCallEventProcessingStatus.Pending ||
                (x.ProcessingStatus == QueueCallEventProcessingStatus.Failed && x.NextAttemptAtUtc != null && x.NextAttemptAtUtc <= nowUtc))
            .OrderBy(x => x.EventAtUtc)
            .ThenBy(x => x.Id)
            .Take(take)
            .ToListAsync(ct);
    }

    public void Add(QueueCallEventEntity entity) => _db.QueueCallEvents.Add(entity);

    public void AddRange(IEnumerable<QueueCallEventEntity> entities) => _db.QueueCallEvents.AddRange(entities);

    public async Task<List<QueueCallHistoryEntity>> ListByQueueAsync(
        Guid tenantId,
        long queueId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        int take,
        int skip,
        CancellationToken ct)
    {
        return await _db.QueueCallHistory
            .Where(x => x.QueueId == queueId)
            .Where(x => x.SegmentStartAtUtc != null && x.SegmentStartAtUtc >= fromUtc && x.SegmentStartAtUtc < toUtc)
            .OrderByDescending(x => x.SegmentStartAtUtc)
            .ThenByDescending(x => x.Id)
            .Skip(skip)
            .Take(take)
            .AsNoTracking()
            .ToListAsync(ct);
    }

    public void Add(QueueCallHistoryEntity entity) => _db.QueueCallHistory.Add(entity);

    public void AddRange(IEnumerable<QueueCallHistoryEntity> entities) => _db.QueueCallHistory.AddRange(entities);

    public async Task<List<QueueAgentActivityEntity>> ListByAgentAsync(
        Guid tenantId,
        long extensionId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        int take,
        int skip,
        CancellationToken ct)
    {
        return await _db.QueueAgentActivities
            .Where(x => x.ExtensionId == extensionId)
            .Where(x => x.OccurredAtUtc >= fromUtc && x.OccurredAtUtc < toUtc)
            .OrderByDescending(x => x.OccurredAtUtc)
            .ThenByDescending(x => x.Id)
            .Skip(skip)
            .Take(take)
            .AsNoTracking()
            .ToListAsync(ct);
    }

    public void Add(QueueAgentActivityEntity entity) => _db.QueueAgentActivities.Add(entity);

    public void AddRange(IEnumerable<QueueAgentActivityEntity> entities) => _db.QueueAgentActivities.AddRange(entities);

    public async Task<List<OutboxMessageEntity>> GetUnpublishedBatchAsync(Guid tenantId, int take, CancellationToken ct)
    {
        return await _db.OutboxMessages
            .Where(x => x.PublishedAtUtc == null)
            .OrderBy(x => x.Id)
            .Take(take)
            .ToListAsync(ct);
    }

    public void Add(OutboxMessageEntity entity) => _db.OutboxMessages.Add(entity);

    public void AddRange(IEnumerable<OutboxMessageEntity> entities) => _db.OutboxMessages.AddRange(entities);

    public async Task<XapiSyncCheckpointEntity?> GetAsync(Guid tenantId, string streamName, string partitionKey, CancellationToken ct)
    {
        return await _db.XapiSyncCheckpoints
            .FirstOrDefaultAsync(x =>
                x.StreamName == streamName &&
                x.PartitionKey == partitionKey, ct);
    }

    public void Add(XapiSyncCheckpointEntity entity) => _db.XapiSyncCheckpoints.Add(entity);
}
