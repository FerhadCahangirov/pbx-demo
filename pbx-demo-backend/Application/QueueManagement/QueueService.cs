using CallControl.Api.Domain;
using CallControl.Api.Infrastructure.QueueManagement.Persistence;
using Microsoft.EntityFrameworkCore;
using pbx_demo_backend.Domain.QueueManagement.Contracts;

namespace CallControl.Api.Application.QueueManagement;

public sealed class QueueService : IQueueService
{
    private readonly PBXDbContext _db;
    private readonly IQueueXapiClient _xapiClient;
    private readonly QueueApplicationMapper _mapper;
    private readonly ILogger<QueueService> _logger;

    public QueueService(
        PBXDbContext db,
        IQueueXapiClient xapiClient,
        QueueApplicationMapper mapper,
        ILogger<QueueService> logger)
    {
        _db = db;
        _xapiClient = xapiClient;
        _mapper = mapper;
        _logger = logger;
    }

    public async Task<QueuePagedResult<QueueDto>> GetQueuesAsync(QueueListQuery query, CancellationToken ct)
    {
        query ??= new QueueListQuery();

        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 200);
        var skip = (page - 1) * pageSize;

        var baseQuery = _db.Queues
            .Include(x => x.Settings)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            baseQuery = baseQuery.Where(x => x.Name.Contains(term) || x.QueueNumber.Contains(term));
        }

        if (!string.IsNullOrWhiteSpace(query.QueueNumber))
        {
            var queueNumber = query.QueueNumber.Trim();
            baseQuery = baseQuery.Where(x => x.QueueNumber == queueNumber);
        }

        if (query.IsRegistered is not null)
        {
            baseQuery = baseQuery.Where(x => x.IsRegistered == query.IsRegistered.Value);
        }

        baseQuery = ApplyQueueSort(baseQuery, query.SortBy, query.SortDescending);

        var totalCount = await baseQuery.CountAsync(ct);
        var queues = await baseQuery
            .Skip(skip)
            .Take(pageSize)
            .ToListAsync(ct);

        var queueIds = queues.Select(x => x.Id).ToArray();
        var memberships = queueIds.Length == 0
            ? []
            : await _db.QueueAgents
                .Where(x => queueIds.Contains(x.QueueId))
                .ToListAsync(ct);

        var extensionIds = memberships.Select(x => x.ExtensionId).Distinct().ToArray();
        var extensions = extensionIds.Length == 0
            ? []
            : await _db.Extensions
                .Where(x => extensionIds.Contains(x.Id))
                .ToListAsync(ct);

        return _mapper.ToPagedQueueDto(
            queues,
            totalCount,
            memberships.GroupBy(x => x.QueueId).ToDictionary(g => g.Key, g => g.ToList()),
            extensions.ToDictionary(x => x.Id));
    }

    public async Task<QueueDto> GetQueueAsync(long queueId, CancellationToken ct)
    {
        var queue = await _db.Queues
            .Where(x => x.Id == queueId)
            .Include(x => x.Settings)
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException($"Queue {queueId} was not found.");

        var memberships = await _db.QueueAgents
            .Where(x => x.QueueId == queue.Id)
            .ToListAsync(ct);

        var extensionIds = memberships.Select(x => x.ExtensionId).Distinct().ToArray();
        var extensions = extensionIds.Length == 0
            ? []
            : await _db.Extensions.Where(x => extensionIds.Contains(x.Id)).ToListAsync(ct);

        return _mapper.ToQueueDto(queue, memberships, extensions.ToDictionary(x => x.Id));
    }

    public async Task<QueueDto> CreateQueueAsync(CreateQueueRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var createPayload = _mapper.ToXapiCreateQueue(request);

        // External XAPI call is outside the local DB transaction boundary.
        var created = await _xapiClient.CreateQueueAsync(createPayload, ct);
        var synced = await ReadQueueWithMembershipsFromXapiAsync(created.Id, ct);

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var localQueue = await UpsertQueueMirrorFromXapiAsync(synced.Queue, synced.Agents, synced.Managers, ct);
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return await GetQueueAsync(localQueue.Id, ct);
    }

    public async Task<QueueDto> UpdateQueueAsync(long queueId, UpdateQueueRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var localQueue = await _db.Queues
            .Where(x => x.Id == queueId)
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException($"Queue {queueId} was not found.");

        var current = await _xapiClient.GetQueueAsync(localQueue.PbxQueueId, select: null, expand: null, ct)
            ?? throw new NotFoundException($"3CX queue {localQueue.PbxQueueId} was not found.");

        _mapper.MergeUpdateIntoXapi(current, request);
        current.Agents = MergeQueueAgents(current.Agents, request.Agents, request.ReplaceAgents, _mapper);
        current.Managers = MergeQueueManagers(current.Managers, request.Managers, request.ReplaceManagers, _mapper);

        await _xapiClient.UpdateQueueAsync(localQueue.PbxQueueId, current, ct);
        var synced = await ReadQueueWithMembershipsFromXapiAsync(localQueue.PbxQueueId, ct);

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        await UpsertQueueMirrorFromXapiAsync(synced.Queue, synced.Agents, synced.Managers, ct);
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return await GetQueueAsync(queueId, ct);
    }

    public async Task DeleteQueueAsync(long queueId, CancellationToken ct)
    {
        var queue = await _db.Queues
            .IgnoreQueryFilters()
            .Where(x => x.Id == queueId)
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException($"Queue {queueId} was not found.");

        await _xapiClient.DeleteQueueAsync(queue.PbxQueueId, ifMatch: null, ct);

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        queue.IsDeleted = true;
        queue.UpdatedAtUtc = DateTimeOffset.UtcNow;
        queue.LastXapiSyncAtUtc = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    private async Task<QueueEntity> UpsertQueueMirrorFromXapiAsync(
        XapiPbxQueueDto xapiQueue,
        IReadOnlyList<XapiPbxQueueAgentDto> xapiAgents,
        IReadOnlyList<XapiPbxQueueManagerDto> xapiManagers,
        CancellationToken ct)
    {
        var nowUtc = DateTimeOffset.UtcNow;

        var queue = await _db.Queues
            .IgnoreQueryFilters()
            .Include(x => x.Settings)
            .FirstOrDefaultAsync(x => x.PbxQueueId == xapiQueue.Id, ct);

        if (queue is null)
        {
            queue = new QueueEntity
            {
                QueueNumber = xapiQueue.Number?.Trim() ?? xapiQueue.Id.ToString(),
                Name = xapiQueue.Name?.Trim() ?? $"Queue {xapiQueue.Id}",
                CreatedAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc
            };
            _db.Queues.Add(queue);
        }

        _mapper.ApplyXapiQueueToEntity(queue, xapiQueue, nowUtc);
        await _db.SaveChangesAsync(ct); // Ensure queue key exists before membership sync.

        await SyncQueueMembershipsAsync(queue, xapiAgents, xapiManagers, nowUtc, ct);
        return queue;
    }

    private async Task SyncQueueMembershipsAsync(
        QueueEntity queue,
        IReadOnlyList<XapiPbxQueueAgentDto> xapiAgents,
        IReadOnlyList<XapiPbxQueueManagerDto> xapiManagers,
        DateTimeOffset nowUtc,
        CancellationToken ct)
    {
        var allNumbers = xapiAgents.Select(x => x.Number)
            .Concat(xapiManagers.Select(x => x.Number))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var extensions = allNumbers.Length == 0
            ? []
            : await _db.Extensions
                .Where(x => allNumbers.Contains(x.ExtensionNumber))
                .ToListAsync(ct);

        var extensionsByNumber = extensions.ToDictionary(x => x.ExtensionNumber, StringComparer.OrdinalIgnoreCase);
        var desiredByExtensionId = new Dictionary<long, QueueMembershipDesiredState>();

        foreach (var agent in xapiAgents)
        {
            var extension = await ResolveOrCreateExtensionAsync(agent.Id, agent.Number, agent.Name, extensionsByNumber, ct);
            if (extension is null)
            {
                _logger.LogWarning("Skipping queue agent membership sync for queue {QueueId}; extension {ExtensionNumber} could not be resolved.", queue.Id, agent.Number);
                continue;
            }

            if (!desiredByExtensionId.TryGetValue(extension.Id, out var desired))
            {
                desired = new QueueMembershipDesiredState
                {
                    Extension = extension,
                    AgentNumberSnapshot = extension.ExtensionNumber,
                    AgentNameSnapshot = agent.Name,
                    SkillGroup = agent.SkillGroup
                };
                desiredByExtensionId[extension.Id] = desired;
            }

            desired.IsAgentMember = true;
            desired.AgentNameSnapshot ??= agent.Name;
            desired.SkillGroup ??= agent.SkillGroup;
            if (agent.Id is not null)
            {
                desired.PbxAgentRefId = agent.Id;
            }
        }

        foreach (var manager in xapiManagers)
        {
            var extension = await ResolveOrCreateExtensionAsync(manager.Id, manager.Number, manager.Name, extensionsByNumber, ct);
            if (extension is null)
            {
                _logger.LogWarning("Skipping queue manager membership sync for queue {QueueId}; extension {ExtensionNumber} could not be resolved.", queue.Id, manager.Number);
                continue;
            }

            if (!desiredByExtensionId.TryGetValue(extension.Id, out var desired))
            {
                desired = new QueueMembershipDesiredState
                {
                    Extension = extension,
                    AgentNumberSnapshot = extension.ExtensionNumber,
                    AgentNameSnapshot = manager.Name
                };
                desiredByExtensionId[extension.Id] = desired;
            }

            desired.IsQueueManager = true;
            desired.AgentNameSnapshot ??= manager.Name;
            if (manager.Id is not null && desired.PbxAgentRefId is null)
            {
                desired.PbxAgentRefId = manager.Id;
            }
        }

        var existingMemberships = await _db.QueueAgents
            .IgnoreQueryFilters()
            .Where(x => x.QueueId == queue.Id)
            .ToListAsync(ct);

        var existingByExtensionId = existingMemberships.ToDictionary(x => x.ExtensionId);

        foreach (var desired in desiredByExtensionId.Values)
        {
            if (existingByExtensionId.TryGetValue(desired.Extension.Id, out var membership))
            {
                membership.PbxAgentRefId = desired.PbxAgentRefId ?? membership.PbxAgentRefId;
                membership.AgentNumberSnapshot = desired.AgentNumberSnapshot;
                membership.AgentNameSnapshot = desired.AgentNameSnapshot;
                membership.SkillGroup = desired.SkillGroup;
                membership.IsAgentMember = desired.IsAgentMember;
                membership.IsQueueManager = desired.IsQueueManager;
                membership.IsDeleted = false;
                membership.LastXapiSyncAtUtc = nowUtc;
                membership.UpdatedAtUtc = nowUtc;
                continue;
            }

            _db.QueueAgents.Add(new QueueAgentEntity
            {
                QueueId = queue.Id,
                ExtensionId = desired.Extension.Id,
                PbxAgentRefId = desired.PbxAgentRefId,
                AgentNumberSnapshot = desired.AgentNumberSnapshot,
                AgentNameSnapshot = desired.AgentNameSnapshot,
                SkillGroup = desired.SkillGroup,
                IsAgentMember = desired.IsAgentMember,
                IsQueueManager = desired.IsQueueManager,
                AssignmentSource = "XapiSync",
                IsDeleted = false,
                LastXapiSyncAtUtc = nowUtc,
                CreatedAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc
            });
        }

        foreach (var existing in existingMemberships)
        {
            if (desiredByExtensionId.ContainsKey(existing.ExtensionId))
            {
                continue;
            }

            existing.IsAgentMember = false;
            existing.IsQueueManager = false;
            existing.IsDeleted = true;
            existing.LastXapiSyncAtUtc = nowUtc;
            existing.UpdatedAtUtc = nowUtc;
        }
    }

    private async Task<ExtensionEntity?> ResolveOrCreateExtensionAsync(
        int? pbxUserId,
        string? extensionNumber,
        string? displayName,
        IReadOnlyDictionary<string, ExtensionEntity> extensionsByNumber,
        CancellationToken ct)
    {
        var normalizedNumber = extensionNumber?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedNumber))
        {
            return null;
        }

        if (extensionsByNumber.TryGetValue(normalizedNumber, out var existing))
        {
            if (!string.IsNullOrWhiteSpace(displayName) && string.IsNullOrWhiteSpace(existing.DisplayName))
            {
                existing.DisplayName = displayName.Trim();
            }

            return existing;
        }

        if (pbxUserId is null)
        {
            return null;
        }

        var byPbxId = await _db.Extensions
            .FirstOrDefaultAsync(x => x.PbxUserId == pbxUserId.Value, ct);
        if (byPbxId is not null)
        {
            byPbxId.ExtensionNumber = normalizedNumber;
            byPbxId.DisplayName ??= string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();
            return byPbxId;
        }

        var nowUtc = DateTimeOffset.UtcNow;
        var created = new ExtensionEntity
        {
            PbxUserId = pbxUserId.Value,
            ExtensionNumber = normalizedNumber,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim(),
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc
        };

        _db.Extensions.Add(created);
        await _db.SaveChangesAsync(ct);
        return created;
    }

    private async Task<QueueXapiQueueReadModel> ReadQueueWithMembershipsFromXapiAsync(int pbxQueueId, CancellationToken ct)
    {
        var queue = await _xapiClient.GetQueueAsync(pbxQueueId, select: null, expand: null, ct)
            ?? throw new NotFoundException($"3CX queue {pbxQueueId} was not found.");

        var agents = await _xapiClient.ListQueueAgentsAsync(pbxQueueId, new QueueODataQuery { Top = 1000, OrderBy = ["Number asc"] }, ct);
        var managers = await _xapiClient.ListQueueManagersAsync(pbxQueueId, new QueueODataQuery { Top = 1000, OrderBy = ["Number asc"] }, ct);

        return new QueueXapiQueueReadModel
        {
            Queue = queue,
            Agents = agents.Value ?? [],
            Managers = managers.Value ?? []
        };
    }

    private static IQueryable<QueueEntity> ApplyQueueSort(IQueryable<QueueEntity> query, string? sortBy, bool desc)
    {
        var key = sortBy?.Trim().ToLowerInvariant();
        return (key, desc) switch
        {
            ("queuenumber", false) => query.OrderBy(x => x.QueueNumber).ThenBy(x => x.Id),
            ("queuenumber", true) => query.OrderByDescending(x => x.QueueNumber).ThenByDescending(x => x.Id),
            ("updatedatutc", false) => query.OrderBy(x => x.UpdatedAtUtc).ThenBy(x => x.Id),
            ("updatedatutc", true) => query.OrderByDescending(x => x.UpdatedAtUtc).ThenByDescending(x => x.Id),
            ("createdatutc", false) => query.OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Id),
            ("createdatutc", true) => query.OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id),
            (_, true) => query.OrderByDescending(x => x.Name).ThenByDescending(x => x.Id),
            _ => query.OrderBy(x => x.Name).ThenBy(x => x.Id)
        };
    }

    private static List<XapiPbxQueueAgentDto>? MergeQueueAgents(
        List<XapiPbxQueueAgentDto>? current,
        List<QueueAgentAssignmentDto>? patch,
        bool replace,
        QueueApplicationMapper mapper)
    {
        if (patch is null)
        {
            return current;
        }

        var incoming = mapper.MapAgentsToXapi(patch);
        if (replace)
        {
            return incoming;
        }

        var merged = (current ?? []).ToDictionary(x => x.Number, StringComparer.OrdinalIgnoreCase);
        foreach (var item in incoming)
        {
            merged[item.Number] = item;
        }

        return merged.Values.OrderBy(x => x.Number, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static List<XapiPbxQueueManagerDto>? MergeQueueManagers(
        List<XapiPbxQueueManagerDto>? current,
        List<QueueManagerAssignmentDto>? patch,
        bool replace,
        QueueApplicationMapper mapper)
    {
        if (patch is null)
        {
            return current;
        }

        var incoming = mapper.MapManagersToXapi(patch);
        if (replace)
        {
            return incoming;
        }

        var merged = (current ?? []).ToDictionary(x => x.Number, StringComparer.OrdinalIgnoreCase);
        foreach (var item in incoming)
        {
            merged[item.Number] = item;
        }

        return merged.Values.OrderBy(x => x.Number, StringComparer.OrdinalIgnoreCase).ToList();
    }
}

internal sealed class QueueXapiQueueReadModel
{
    public XapiPbxQueueDto Queue { get; set; } = new();
    public IReadOnlyList<XapiPbxQueueAgentDto> Agents { get; set; } = [];
    public IReadOnlyList<XapiPbxQueueManagerDto> Managers { get; set; } = [];
}

internal sealed class QueueMembershipDesiredState
{
    public ExtensionEntity Extension { get; set; } = null!;
    public int? PbxAgentRefId { get; set; }
    public string AgentNumberSnapshot { get; set; } = string.Empty;
    public string? AgentNameSnapshot { get; set; }
    public string? SkillGroup { get; set; }
    public bool IsAgentMember { get; set; }
    public bool IsQueueManager { get; set; }
}

