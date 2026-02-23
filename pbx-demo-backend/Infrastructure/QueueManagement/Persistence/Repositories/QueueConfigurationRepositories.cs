using Microsoft.EntityFrameworkCore;
using pbx_demo_backend.Domain.QueueManagement.Contracts;

namespace CallControl.Api.Infrastructure.QueueManagement.Persistence.Repositories;

public interface IQueuePersistenceUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct);
}

public interface IQueueRepository
{
    Task<QueueEntity?> GetByIdAsync(long queueId, CancellationToken ct);
    Task<QueueEntity?> GetByPbxQueueIdAsync(Guid tenantId, int pbxQueueId, CancellationToken ct);
    Task<QueueEntity?> GetByQueueNumberAsync(Guid tenantId, string queueNumber, CancellationToken ct);
    Task<List<QueueEntity>> ListAsync(Guid tenantId, int skip, int take, CancellationToken ct);
    Task<List<QueueEntity>> ListIncludingDeletedAsync(Guid tenantId, int skip, int take, CancellationToken ct);
    Task<int> CountAsync(Guid tenantId, CancellationToken ct);
    void Add(QueueEntity queue);
}

public interface IExtensionRepository
{
    Task<ExtensionEntity?> GetExtensionByIdAsync(long extensionId, CancellationToken ct);
    Task<ExtensionEntity?> GetByPbxUserIdAsync(Guid tenantId, int pbxUserId, CancellationToken ct);
    Task<ExtensionEntity?> GetByExtensionNumberAsync(Guid tenantId, string extensionNumber, CancellationToken ct);
    Task<List<ExtensionEntity>> ListByIdsAsync(Guid tenantId, IReadOnlyCollection<long> extensionIds, CancellationToken ct);
    void Add(ExtensionEntity extension);
}

public interface IQueueAgentRepository
{
    Task<List<QueueAgentEntity>> ListForQueueAsync(long queueId, CancellationToken ct);
    Task<List<QueueAgentEntity>> ListForQueueIncludingDeletedAsync(long queueId, CancellationToken ct);
    void Add(QueueAgentEntity entity);
    void AddRange(IEnumerable<QueueAgentEntity> entities);
}

public sealed class QueueConfigurationRepository :
    IQueuePersistenceUnitOfWork,
    IQueueRepository,
    IExtensionRepository,
    IQueueAgentRepository
{
    private readonly PBXDbContext _db;

    public QueueConfigurationRepository(PBXDbContext db)
    {
        _db = db;
    }

    public Task<int> SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);

    public async Task<QueueEntity?> GetByIdAsync(long queueId, CancellationToken ct)
    {
        return await _db.Queues
            .Include(x => x.Settings)
            .Include(x => x.Agents)
            .FirstOrDefaultAsync(x => x.Id == queueId, ct);
    }

    public async Task<QueueEntity?> GetByPbxQueueIdAsync(Guid tenantId, int pbxQueueId, CancellationToken ct)
    {
        return await _db.Queues
            .Include(x => x.Settings)
            .FirstOrDefaultAsync(x => x.PbxQueueId == pbxQueueId, ct);
    }

    public async Task<QueueEntity?> GetByQueueNumberAsync(Guid tenantId, string queueNumber, CancellationToken ct)
    {
        return await _db.Queues
            .Include(x => x.Settings)
            .FirstOrDefaultAsync(x => x.QueueNumber == queueNumber, ct);
    }

    public async Task<List<QueueEntity>> ListAsync(Guid tenantId, int skip, int take, CancellationToken ct)
    {
        return await _db.Queues
            .OrderBy(x => x.Name)
            .Skip(skip)
            .Take(take)
            .AsNoTracking()
            .ToListAsync(ct);
    }

    public async Task<List<QueueEntity>> ListIncludingDeletedAsync(Guid tenantId, int skip, int take, CancellationToken ct)
    {
        return await _db.Queues
            .IgnoreQueryFilters()
            .OrderBy(x => x.Name)
            .Skip(skip)
            .Take(take)
            .AsNoTracking()
            .ToListAsync(ct);
    }

    public Task<int> CountAsync(Guid tenantId, CancellationToken ct)
        => _db.Queues.CountAsync(ct);

    public void Add(QueueEntity queue) => _db.Queues.Add(queue);

    public async Task<ExtensionEntity?> GetExtensionByIdAsync(long extensionId, CancellationToken ct)
    {
        return await _db.Extensions.FirstOrDefaultAsync(x => x.Id == extensionId, ct);
    }

    public async Task<ExtensionEntity?> GetByPbxUserIdAsync(Guid tenantId, int pbxUserId, CancellationToken ct)
    {
        return await _db.Extensions.FirstOrDefaultAsync(x => x.PbxUserId == pbxUserId, ct);
    }

    public async Task<ExtensionEntity?> GetByExtensionNumberAsync(Guid tenantId, string extensionNumber, CancellationToken ct)
    {
        return await _db.Extensions.FirstOrDefaultAsync(x => x.ExtensionNumber == extensionNumber, ct);
    }

    public async Task<List<ExtensionEntity>> ListByIdsAsync(Guid tenantId, IReadOnlyCollection<long> extensionIds, CancellationToken ct)
    {
        if (extensionIds.Count == 0)
        {
            return [];
        }

        return await _db.Extensions
            .Where(x => extensionIds.Contains(x.Id))
            .ToListAsync(ct);
    }

    public void Add(ExtensionEntity extension) => _db.Extensions.Add(extension);

    public async Task<List<QueueAgentEntity>> ListForQueueAsync(long queueId, CancellationToken ct)
    {
        return await _db.QueueAgents
            .Where(x => x.QueueId == queueId)
            .OrderBy(x => x.AgentNumberSnapshot)
            .ToListAsync(ct);
    }

    public async Task<List<QueueAgentEntity>> ListForQueueIncludingDeletedAsync(long queueId, CancellationToken ct)
    {
        return await _db.QueueAgents
            .IgnoreQueryFilters()
            .Where(x => x.QueueId == queueId)
            .OrderBy(x => x.AgentNumberSnapshot)
            .ToListAsync(ct);
    }

    public void Add(QueueAgentEntity entity) => _db.QueueAgents.Add(entity);

    public void AddRange(IEnumerable<QueueAgentEntity> entities) => _db.QueueAgents.AddRange(entities);
}
