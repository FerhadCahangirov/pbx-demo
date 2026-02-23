using CallControl.Api.Domain;
using CallControl.Api.Infrastructure.QueueManagement.Persistence;
using Microsoft.EntityFrameworkCore;
using pbx_demo_backend.Domain.QueueManagement.Contracts;

namespace CallControl.Api.Application.QueueManagement;

public sealed class QueueLiveSnapshotBuilder
{
    private readonly PBXDbContext _db;
    private readonly ILogger<QueueLiveSnapshotBuilder> _logger;

    public QueueLiveSnapshotBuilder(
        PBXDbContext db,
        ILogger<QueueLiveSnapshotBuilder> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<QueueLiveSnapshotDto> BuildAsync(long queueId, CancellationToken ct)
    {
        var queue = await _db.Queues
            .Include(x => x.Settings)
            .FirstOrDefaultAsync(x => x.Id == queueId, ct)
            ?? throw new NotFoundException($"Queue {queueId} was not found.");

        var nowUtc = DateTimeOffset.UtcNow;

        var activeCalls = await _db.QueueCalls
            .Where(x => x.QueueId == queueId)
            .Where(x =>
                x.CurrentStatus != QueueCallLifecycleStatus.Completed &&
                x.CurrentStatus != QueueCallLifecycleStatus.Abandoned)
            .OrderBy(x => x.QueuedAtUtc)
            .ThenBy(x => x.Id)
            .ToListAsync(ct);

        var latestWaitingSnapshotHeader = await _db.QueueWaitingSnapshots
            .Where(x => x.QueueId == queueId)
            .OrderByDescending(x => x.CapturedAtUtc)
            .ThenByDescending(x => x.Id)
            .Select(x => new { x.SnapshotKey, x.CapturedAtUtc })
            .FirstOrDefaultAsync(ct);

        List<QueueWaitingSnapshotEntity> latestWaitingSnapshotRows = [];
        if (latestWaitingSnapshotHeader is not null)
        {
            latestWaitingSnapshotRows = await _db.QueueWaitingSnapshots
                .Where(x => x.QueueId == queueId && x.SnapshotKey == latestWaitingSnapshotHeader.SnapshotKey)
                .OrderBy(x => x.WaitOrder)
                .ThenBy(x => x.Id)
                .ToListAsync(ct);
        }

        var queueMemberships = await _db.QueueAgents
            .Where(x => x.QueueId == queueId)
            .ToListAsync(ct);

        var latestAgentActivityRows = await _db.QueueAgentActivities
            .Where(x => x.QueueId == queueId)
            .OrderByDescending(x => x.OccurredAtUtc)
            .ThenByDescending(x => x.Id)
            .ToListAsync(ct);

        var latestAgentActivityByExtensionId = latestAgentActivityRows
            .GroupBy(x => x.ExtensionId)
            .ToDictionary(g => g.Key, g => g.First());

        var extensionIds = queueMemberships.Select(x => x.ExtensionId)
            .Concat(activeCalls.Select(x => x.LastAgentExtensionId).Where(x => x is not null).Select(x => x!.Value))
            .Concat(activeCalls.Select(x => x.AnsweredByExtensionId).Where(x => x is not null).Select(x => x!.Value))
            .Concat(latestAgentActivityByExtensionId.Keys)
            .Distinct()
            .ToArray();

        var extensionsById = extensionIds.Length == 0
            ? new Dictionary<long, ExtensionEntity>()
            : await _db.Extensions
                .Where(x => extensionIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, ct);

        var waitingCalls = BuildWaitingCalls(queueId, activeCalls, latestWaitingSnapshotRows, nowUtc);
        var activeCallDtos = BuildActiveCalls(activeCalls, extensionsById, nowUtc);
        var agentStatuses = BuildAgentStatuses(queueMemberships, latestAgentActivityByExtensionId, extensionsById, nowUtc);

        var snapshot = new QueueLiveSnapshotDto
        {
            QueueId = queueId,
            AsOfUtc = nowUtc,
            Version = ComputeVersion(activeCalls, latestWaitingSnapshotRows, latestAgentActivityByExtensionId.Values),
            WaitingCalls = waitingCalls,
            ActiveCalls = activeCallDtos,
            AgentStatuses = agentStatuses
        };

        snapshot.Stats = BuildStats(queue, snapshot);
        return snapshot;
    }

    private static List<QueueWaitingCallLiveDto> BuildWaitingCalls(
        long queueId,
        IReadOnlyList<QueueCallEntity> activeCalls,
        IReadOnlyList<QueueWaitingSnapshotEntity> waitingSnapshotRows,
        DateTimeOffset nowUtc)
    {
        if (waitingSnapshotRows.Count > 0)
        {
            return waitingSnapshotRows
                .Select(x => new QueueWaitingCallLiveDto
                {
                    CallKey = x.CorrelationKey ?? (x.PbxCallId is not null ? $"xapi-activecall:{x.PbxCallId}" : $"waiting:{x.Id}"),
                    QueueCallId = x.QueueCallId,
                    PbxCallId = x.PbxCallId,
                    CallerNumber = x.CallerNumber,
                    CallerName = x.CallerName,
                    WaitOrder = x.WaitOrder,
                    WaitingMs = x.WaitingMs ?? (long)Math.Max(0, (nowUtc - x.CapturedAtUtc).TotalMilliseconds),
                    EstimatedOrder = x.EstimatedOrder
                })
                .OrderBy(x => x.WaitOrder)
                .ThenBy(x => x.CallKey, StringComparer.Ordinal)
                .ToList();
        }

        return activeCalls
            .Where(x => x.CurrentStatus is QueueCallLifecycleStatus.EnteredQueue or QueueCallLifecycleStatus.Waiting)
            .OrderBy(x => x.WaitOrder ?? int.MaxValue)
            .ThenBy(x => x.QueuedAtUtc)
            .ThenBy(x => x.Id)
            .Select((x, i) => new QueueWaitingCallLiveDto
            {
                CallKey = x.CorrelationKey,
                QueueCallId = x.Id,
                PbxCallId = x.PbxCallId,
                CallerNumber = x.CallerNumber,
                CallerName = x.CallerName,
                WaitOrder = x.WaitOrder ?? (i + 1),
                WaitingMs = x.WaitingMs ?? (x.QueuedAtUtc is null ? null : (long?)Math.Max(0, (nowUtc - x.QueuedAtUtc.Value).TotalMilliseconds)),
                EstimatedOrder = x.WaitOrder is null
            })
            .ToList();
    }

    private static List<QueueActiveCallLiveDto> BuildActiveCalls(
        IReadOnlyList<QueueCallEntity> activeCalls,
        IReadOnlyDictionary<long, ExtensionEntity> extensionsById,
        DateTimeOffset nowUtc)
    {
        return activeCalls
            .Where(x => x.CurrentStatus is not QueueCallLifecycleStatus.EnteredQueue and not QueueCallLifecycleStatus.Waiting)
            .OrderBy(x => x.LastSeenAtUtc)
            .ThenBy(x => x.Id)
            .Select(x =>
            {
                var agentId = x.LastAgentExtensionId ?? x.AnsweredByExtensionId;
                var talkingMs = x.TalkingMs;
                if (talkingMs is null && x.AnsweredAtUtc is not null && x.CurrentStatus is QueueCallLifecycleStatus.Answered or QueueCallLifecycleStatus.Transferred)
                {
                    talkingMs = (long)Math.Max(0, (nowUtc - x.AnsweredAtUtc.Value).TotalMilliseconds);
                }

                return new QueueActiveCallLiveDto
                {
                    CallKey = x.CorrelationKey,
                    QueueCallId = x.Id,
                    PbxCallId = x.PbxCallId,
                    Status = x.CurrentStatus.ToString(),
                    AgentId = agentId,
                    AgentExtension = agentId is not null && extensionsById.TryGetValue(agentId.Value, out var ext) ? ext.ExtensionNumber : null,
                    TalkingMs = talkingMs
                };
            })
            .ToList();
    }

    private static List<QueueAgentLiveStatusDto> BuildAgentStatuses(
        IReadOnlyList<QueueAgentEntity> memberships,
        IReadOnlyDictionary<long, QueueAgentActivityEntity> latestAgentActivityByExtensionId,
        IReadOnlyDictionary<long, ExtensionEntity> extensionsById,
        DateTimeOffset nowUtc)
    {
        return memberships
            .Where(x => !x.IsDeleted && x.IsAgentMember)
            .OrderBy(x => x.AgentNumberSnapshot, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Id)
            .Select(m =>
            {
                latestAgentActivityByExtensionId.TryGetValue(m.ExtensionId, out var latestActivity);
                extensionsById.TryGetValue(m.ExtensionId, out var ext);

                var queueStatus = ext?.QueueStatus
                    ?? InferQueueStatusFromActivity(latestActivity?.ActivityType)
                    ?? "Unknown";

                return new QueueAgentLiveStatusDto
                {
                    AgentId = m.ExtensionId,
                    ExtensionNumber = ext?.ExtensionNumber ?? m.AgentNumberSnapshot,
                    DisplayName = ext?.DisplayName ?? m.AgentNameSnapshot,
                    QueueStatus = queueStatus,
                    ActivityType = latestActivity?.ActivityType.ToString() ?? "Unknown",
                    CurrentCallKey = latestActivity?.QueueCallId is not null
                        ? null // call key lookup is not stored on QueueAgentActivityEntity in Batch 1 schema
                        : null,
                    AtUtc = latestActivity?.OccurredAtUtc ?? nowUtc
                };
            })
            .ToList();
    }

    private static QueueStatsSummaryDto BuildStats(QueueEntity queue, QueueLiveSnapshotDto snapshot)
    {
        var waitingMsValues = snapshot.WaitingCalls
            .Where(x => x.WaitingMs is not null)
            .Select(x => x.WaitingMs!.Value)
            .ToArray();

        var loggedInAgents = snapshot.AgentStatuses.Count(x => x.QueueStatus.Equals("LoggedIn", StringComparison.OrdinalIgnoreCase)
            || x.QueueStatus.Equals("Available", StringComparison.OrdinalIgnoreCase)
            || x.QueueStatus.Equals("Ringing", StringComparison.OrdinalIgnoreCase)
            || x.QueueStatus.Equals("Talking", StringComparison.OrdinalIgnoreCase)
            || x.QueueStatus.Equals("WrapUp", StringComparison.OrdinalIgnoreCase));

        var availableAgents = snapshot.AgentStatuses.Count(x => x.QueueStatus.Equals("Available", StringComparison.OrdinalIgnoreCase));
        if (availableAgents == 0 && loggedInAgents > 0)
        {
            availableAgents = loggedInAgents;
        }

        return new QueueStatsSummaryDto
        {
            QueueId = queue.Id,
            AsOfUtc = snapshot.AsOfUtc,
            WaitingCount = snapshot.WaitingCalls.Count,
            ActiveCount = snapshot.ActiveCalls.Count,
            LoggedInAgents = loggedInAgents,
            AvailableAgents = availableAgents,
            AverageWaitingMs = waitingMsValues.Length == 0 ? null : (long?)waitingMsValues.Average(),
            SlaPct = ComputeSlaPct(snapshot, queue.Settings?.SlaTimeSec),
            AnsweredCount = 0,
            AbandonedCount = 0
        };
    }

    private static decimal? ComputeSlaPct(QueueLiveSnapshotDto snapshot, int? slaSec)
    {
        if (slaSec is null || slaSec <= 0)
        {
            return null;
        }

        if (snapshot.WaitingCalls.Count == 0)
        {
            return 100m;
        }

        var thresholdMs = slaSec.Value * 1000L;
        var within = snapshot.WaitingCalls.Count(x => x.WaitingMs is null || x.WaitingMs.Value <= thresholdMs);
        return Math.Round((decimal)within * 100m / snapshot.WaitingCalls.Count, 2, MidpointRounding.AwayFromZero);
    }

    private static long ComputeVersion(
        IReadOnlyList<QueueCallEntity> activeCalls,
        IReadOnlyList<QueueWaitingSnapshotEntity> waitingSnapshotRows,
        IEnumerable<QueueAgentActivityEntity> latestAgentActivities)
    {
        long maxCallVersion = activeCalls.Count == 0 ? 0 : activeCalls.Max(x => x.ProjectionVersion);
        long waitingTicks = waitingSnapshotRows.Count == 0 ? 0 : waitingSnapshotRows.Max(x => x.CapturedAtUtc.UtcTicks);
        long agentCursor = latestAgentActivities.Any() ? latestAgentActivities.Max(x => x.Id) : 0;
        return Math.Max(maxCallVersion, Math.Max(waitingTicks, agentCursor));
    }

    private static string? InferQueueStatusFromActivity(QueueAgentActivityType? activityType)
    {
        return activityType switch
        {
            QueueAgentActivityType.Login => "LoggedIn",
            QueueAgentActivityType.Logout => "LoggedOut",
            QueueAgentActivityType.Offer => "Ringing",
            QueueAgentActivityType.Answer => "Talking",
            QueueAgentActivityType.TalkingStart => "Talking",
            QueueAgentActivityType.TalkingEnd => "Available",
            QueueAgentActivityType.WrapUpStart => "WrapUp",
            QueueAgentActivityType.WrapUpEnd => "Available",
            _ => null
        };
    }
}

