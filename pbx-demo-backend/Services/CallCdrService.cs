using CallControl.Api.Domain;
using CallControl.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CallControl.Api.Services;

public sealed class CallCdrService
{
    private readonly IDbContextFactory<PBXDbContext> _dbContextFactory;

    public CallCdrService(IDbContextFactory<PBXDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    public async Task UpsertPbxCallAsync(PbxCallCdrUpdate update, CancellationToken cancellationToken)
    {
        if (update.OperatorUserId <= 0)
        {
            return;
        }

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var occurredAtUtc = update.OccurredAtUtc == default ? DateTimeOffset.UtcNow : update.OccurredAtUtc;
        var status = NormalizeStatus(update.Status, update.IsEnded);

        var call = await ResolvePbxCallEntityAsync(dbContext, update, cancellationToken);
        if (call is null)
        {
            call = new AppCallCdrEntity
            {
                Source = AppCallSource.Pbx,
                OperatorUserId = update.OperatorUserId,
                OperatorUsername = update.OperatorUsername,
                OperatorExtension = update.OperatorExtension,
                TrackingKey = BuildPbxTrackingKey(update.OperatorUserId, update.ParticipantId),
                CallScopeId = update.PbxCallId?.ToString() ?? update.ParticipantId.ToString(),
                ParticipantId = update.ParticipantId,
                PbxCallId = update.PbxCallId,
                PbxLegId = update.PbxLegId,
                Direction = update.Direction,
                Status = status,
                RemoteParty = update.RemoteParty,
                RemoteName = update.RemoteName,
                StartedAtUtc = occurredAtUtc,
                LastStatusAtUtc = occurredAtUtc,
                IsActive = !update.IsEnded,
                CreatedAtUtc = occurredAtUtc,
                UpdatedAtUtc = occurredAtUtc
            };
            dbContext.CallCdrs.Add(call);
        }
        else
        {
            call.UpdatedAtUtc = occurredAtUtc;
            call.LastStatusAtUtc = occurredAtUtc;
            call.Status = status;
            call.Direction = update.Direction;
            call.OperatorUsername = update.OperatorUsername;
            call.OperatorExtension = update.OperatorExtension;
            call.RemoteParty = update.RemoteParty;
            call.RemoteName = update.RemoteName;
            call.ParticipantId ??= update.ParticipantId;
            call.PbxCallId ??= update.PbxCallId;
            call.PbxLegId ??= update.PbxLegId;
            call.CallScopeId ??= update.PbxCallId?.ToString() ?? update.ParticipantId.ToString();
        }

        if (string.IsNullOrWhiteSpace(call.TrackingKey))
        {
            call.TrackingKey = BuildPbxTrackingKey(update.OperatorUserId, update.ParticipantId);
        }

        if (update.PbxCallId.HasValue)
        {
            call.CallScopeId = update.PbxCallId.Value.ToString();
        }

        if (status.Equals(CallControlConstants.ParticipantStatusConnected, StringComparison.OrdinalIgnoreCase))
        {
            call.AnsweredAtUtc ??= update.ConnectedAtUtc ?? occurredAtUtc;
        }
        else if (update.ConnectedAtUtc.HasValue)
        {
            call.AnsweredAtUtc ??= update.ConnectedAtUtc;
        }

        if (update.IsEnded)
        {
            call.Status = "Ended";
            call.IsActive = false;
            call.EndReason = string.IsNullOrWhiteSpace(update.EndReason) ? call.EndReason : update.EndReason;
            call.EndedAtUtc ??= occurredAtUtc;
        }
        else
        {
            call.IsActive = !string.Equals(call.Status, "Ended", StringComparison.OrdinalIgnoreCase);
        }

        await AppendStatusHistoryIfNeededAsync(
            dbContext,
            call,
            call.Status,
            update.EventType,
            update.EndReason,
            occurredAtUtc,
            cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task UpsertWebRtcCallAsync(WebRtcCallCdrUpdate update, CancellationToken cancellationToken)
    {
        if (update.OperatorUserId <= 0 || string.IsNullOrWhiteSpace(update.CallId))
        {
            return;
        }

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var occurredAtUtc = update.OccurredAtUtc == default ? DateTimeOffset.UtcNow : update.OccurredAtUtc;
        var isEnded = update.IsEnded || string.Equals(update.Status, "Ended", StringComparison.OrdinalIgnoreCase);
        var status = NormalizeStatus(update.Status, isEnded);
        var trackingKey = BuildWebRtcTrackingKey(update.CallId, update.OperatorUserId);

        var call = await dbContext.CallCdrs
            .FirstOrDefaultAsync(
                v => v.Source == AppCallSource.Browser
                    && v.TrackingKey == trackingKey
                    && v.OperatorUserId == update.OperatorUserId,
                cancellationToken);

        if (call is null)
        {
            call = new AppCallCdrEntity
            {
                Source = AppCallSource.Browser,
                OperatorUserId = update.OperatorUserId,
                OperatorUsername = update.OperatorUsername,
                OperatorExtension = update.OperatorExtension,
                TrackingKey = trackingKey,
                CallScopeId = update.CallId,
                Direction = update.Direction,
                Status = status,
                RemoteParty = update.RemoteParty,
                RemoteName = update.RemoteName,
                StartedAtUtc = update.StartedAtUtc == default ? occurredAtUtc : update.StartedAtUtc,
                LastStatusAtUtc = occurredAtUtc,
                IsActive = !isEnded,
                CreatedAtUtc = occurredAtUtc,
                UpdatedAtUtc = occurredAtUtc
            };
            dbContext.CallCdrs.Add(call);
        }
        else
        {
            call.UpdatedAtUtc = occurredAtUtc;
            call.LastStatusAtUtc = occurredAtUtc;
            call.Status = status;
            call.OperatorUsername = update.OperatorUsername;
            call.OperatorExtension = update.OperatorExtension;
            call.Direction = update.Direction;
            call.RemoteParty = update.RemoteParty;
            call.RemoteName = update.RemoteName;
            call.CallScopeId = update.CallId;
        }

        if (update.AnsweredAtUtc.HasValue)
        {
            call.AnsweredAtUtc ??= update.AnsweredAtUtc;
        }
        else if (string.Equals(status, "Connected", StringComparison.OrdinalIgnoreCase)
                 || string.Equals(status, "Connecting", StringComparison.OrdinalIgnoreCase))
        {
            call.AnsweredAtUtc ??= occurredAtUtc;
        }

        if (isEnded)
        {
            call.Status = "Ended";
            call.IsActive = false;
            call.EndReason = string.IsNullOrWhiteSpace(update.EndReason) ? call.EndReason : update.EndReason;
            call.EndedAtUtc ??= update.EndedAtUtc ?? occurredAtUtc;
        }
        else
        {
            call.IsActive = true;
        }

        await AppendStatusHistoryIfNeededAsync(
            dbContext,
            call,
            call.Status,
            update.EventType,
            update.EndReason,
            occurredAtUtc,
            cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<CrmCallHistoryResponse> GetCallHistoryAsync(
        int? operatorUserId,
        int take,
        int skip,
        CancellationToken cancellationToken)
    {
        var normalizedTake = Math.Clamp(take, 1, 300);
        var normalizedSkip = Math.Max(0, skip);

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var query = dbContext.CallCdrs
            .AsNoTracking()
            .Include(v => v.OperatorUser)
            .AsQueryable();

        if (operatorUserId.HasValue && operatorUserId.Value > 0)
        {
            query = query.Where(v => v.OperatorUserId == operatorUserId.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var calls = await query
            .OrderByDescending(v => v.StartedAtUtc)
            .ThenByDescending(v => v.Id)
            .Skip(normalizedSkip)
            .Take(normalizedTake)
            .ToListAsync(cancellationToken);

        var callIds = calls.Select(v => v.Id).ToList();
        var historyByCallId = new Dictionary<long, List<CrmCallStatusHistoryItemResponse>>();
        if (callIds.Count > 0)
        {
            var historyRows = await dbContext.CallCdrStatusHistory
                .AsNoTracking()
                .Where(v => callIds.Contains(v.CallCdrId))
                .OrderBy(v => v.OccurredAtUtc)
                .ThenBy(v => v.Id)
                .ToListAsync(cancellationToken);

            historyByCallId = historyRows
                .GroupBy(v => v.CallCdrId)
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .Select(v => new CrmCallStatusHistoryItemResponse
                        {
                            Status = v.Status,
                            EventType = v.EventType,
                            EventReason = v.EventReason,
                            OccurredAtUtc = v.OccurredAtUtc
                        })
                        .ToList());
        }

        var now = DateTimeOffset.UtcNow;
        var items = calls.Select(call =>
        {
            historyByCallId.TryGetValue(call.Id, out var statusHistory);

            return new CrmCallHistoryItemResponse
            {
                Id = call.Id,
                Source = call.Source.ToString(),
                OperatorUserId = call.OperatorUserId,
                OperatorUsername = call.OperatorUsername,
                OperatorDisplayName = BuildDisplayName(call.OperatorUser?.FirstName, call.OperatorUser?.LastName, call.OperatorUsername),
                OperatorExtension = call.OperatorExtension,
                TrackingKey = call.TrackingKey,
                CallScopeId = call.CallScopeId,
                ParticipantId = call.ParticipantId,
                PbxCallId = call.PbxCallId,
                PbxLegId = call.PbxLegId,
                Direction = call.Direction.ToString(),
                Status = call.Status,
                RemoteParty = call.RemoteParty,
                RemoteName = call.RemoteName,
                EndReason = call.EndReason,
                StartedAtUtc = call.StartedAtUtc,
                AnsweredAtUtc = call.AnsweredAtUtc,
                EndedAtUtc = call.EndedAtUtc,
                IsActive = call.IsActive,
                TalkDurationSeconds = ResolveTalkDurationSeconds(call.AnsweredAtUtc, call.EndedAtUtc, call.IsActive, now),
                TotalDurationSeconds = ResolveTotalDurationSeconds(call.StartedAtUtc, call.EndedAtUtc, call.IsActive, now),
                StatusHistory = statusHistory ?? []
            };
        }).ToList();

        return new CrmCallHistoryResponse
        {
            TotalCount = totalCount,
            Take = normalizedTake,
            Skip = normalizedSkip,
            Items = items
        };
    }

    public async Task<CrmCallAnalyticsResponse> GetCallAnalyticsAsync(
        DateTimeOffset periodStartUtc,
        DateTimeOffset periodEndUtc,
        CancellationToken cancellationToken)
    {
        if (periodEndUtc < periodStartUtc)
        {
            (periodStartUtc, periodEndUtc) = (periodEndUtc, periodStartUtc);
        }

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var calls = await dbContext.CallCdrs
            .AsNoTracking()
            .Where(v => v.StartedAtUtc >= periodStartUtc && v.StartedAtUtc <= periodEndUtc)
            .ToListAsync(cancellationToken);

        var operatorIds = calls.Select(v => v.OperatorUserId).Distinct().ToList();
        var usersById = await dbContext.Users
            .AsNoTracking()
            .Where(v => operatorIds.Contains(v.Id))
            .ToDictionaryAsync(v => v.Id, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var answeredCalls = calls.Count(v => v.AnsweredAtUtc.HasValue);
        var totalTalkSeconds = calls.Sum(v => ResolveTalkDurationSeconds(v.AnsweredAtUtc, v.EndedAtUtc, v.IsActive, now) ?? 0L);
        var operatorKpis = calls
            .GroupBy(v => new { v.OperatorUserId, v.OperatorUsername, v.OperatorExtension })
            .Select(group =>
            {
                usersById.TryGetValue(group.Key.OperatorUserId, out var user);
                var groupedCalls = group.ToList();
                var groupedAnsweredCalls = groupedCalls.Count(v => v.AnsweredAtUtc.HasValue);
                var groupedTotalTalkSeconds = groupedCalls.Sum(v => ResolveTalkDurationSeconds(v.AnsweredAtUtc, v.EndedAtUtc, v.IsActive, now) ?? 0L);

                return new CrmOperatorCallKpiResponse
                {
                    OperatorUserId = group.Key.OperatorUserId,
                    OperatorUsername = group.Key.OperatorUsername,
                    OperatorDisplayName = BuildDisplayName(user?.FirstName, user?.LastName, group.Key.OperatorUsername),
                    OperatorExtension = group.Key.OperatorExtension,
                    TotalCalls = groupedCalls.Count,
                    ActiveCalls = groupedCalls.Count(v => v.IsActive),
                    AnsweredCalls = groupedAnsweredCalls,
                    MissedCalls = groupedCalls.Count(IsMissedCall),
                    FailedCalls = groupedCalls.Count(IsFailedCall),
                    TotalTalkSeconds = groupedTotalTalkSeconds,
                    AverageTalkSeconds = groupedAnsweredCalls == 0
                        ? 0
                        : groupedTotalTalkSeconds / (double)groupedAnsweredCalls,
                    LastCallAtUtc = groupedCalls.Max(v => v.StartedAtUtc)
                };
            })
            .OrderByDescending(v => v.TotalCalls)
            .ThenBy(v => v.OperatorUsername, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new CrmCallAnalyticsResponse
        {
            PeriodStartUtc = periodStartUtc,
            PeriodEndUtc = periodEndUtc,
            GeneratedAtUtc = now,
            TotalCalls = calls.Count,
            ActiveCalls = calls.Count(v => v.IsActive),
            AnsweredCalls = answeredCalls,
            MissedCalls = calls.Count(IsMissedCall),
            FailedCalls = calls.Count(IsFailedCall),
            TotalTalkSeconds = totalTalkSeconds,
            AverageTalkSeconds = answeredCalls == 0 ? 0 : totalTalkSeconds / (double)answeredCalls,
            TotalOperators = operatorKpis.Count,
            ActiveOperators = operatorKpis.Count(v => v.ActiveCalls > 0),
            OperatorKpis = operatorKpis
        };
    }

    private static async Task<AppCallCdrEntity?> ResolvePbxCallEntityAsync(
        PBXDbContext dbContext,
        PbxCallCdrUpdate update,
        CancellationToken cancellationToken)
    {
        var baseQuery = dbContext.CallCdrs
            .Where(v => v.OperatorUserId == update.OperatorUserId && v.Source == AppCallSource.Pbx);



        if (update.PbxCallId.HasValue)
        {
            var byCallId = await baseQuery
                .Where(v => v.IsActive && v.PbxCallId == update.PbxCallId.Value)
                .OrderByDescending(v => v.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (byCallId is not null)
            {
                return byCallId;
            }
        }

        if (update.ParticipantId > 0)
        {
            var byParticipant = await baseQuery
                .Where(v => v.IsActive && v.ParticipantId == update.ParticipantId)
                .OrderByDescending(v => v.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (byParticipant is not null)
            {
                return byParticipant;
            }
        }

        if (update.PbxCallId.HasValue)
        {
            return await baseQuery
                .Where(v => v.PbxCallId == update.PbxCallId.Value)
                .OrderByDescending(v => v.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        return null;
    }

    private static async Task AppendStatusHistoryIfNeededAsync(
        PBXDbContext dbContext,
        AppCallCdrEntity call,
        string status,
        string eventType,
        string? eventReason,
        DateTimeOffset occurredAtUtc,
        CancellationToken cancellationToken)
    {
        var normalizedStatus = NormalizeText(status);
        var normalizedEventType = NormalizeText(eventType);
        var normalizedEventReason = NormalizeText(eventReason);

        if (call.Id == 0)
        {
            call.StatusHistory.Add(new AppCallCdrStatusHistoryEntity
            {
                Status = normalizedStatus,
                EventType = normalizedEventType,
                EventReason = string.IsNullOrWhiteSpace(normalizedEventReason) ? null : normalizedEventReason,
                OccurredAtUtc = occurredAtUtc,
                CreatedAtUtc = DateTimeOffset.UtcNow
            });
            return;
        }

        var last = await dbContext.CallCdrStatusHistory
            .AsNoTracking()
            .Where(v => v.CallCdrId == call.Id)
            .OrderByDescending(v => v.Id)
            .Select(v => new
            {
                v.Status,
                v.EventType,
                v.EventReason
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (last is not null
            && string.Equals(last.Status, normalizedStatus, StringComparison.OrdinalIgnoreCase)
            && string.Equals(last.EventType, normalizedEventType, StringComparison.OrdinalIgnoreCase)
            && string.Equals(NormalizeText(last.EventReason), normalizedEventReason, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        dbContext.CallCdrStatusHistory.Add(new AppCallCdrStatusHistoryEntity
        {
            CallCdrId = call.Id,
            Status = normalizedStatus,
            EventType = normalizedEventType,
            EventReason = string.IsNullOrWhiteSpace(normalizedEventReason) ? null : normalizedEventReason,
            OccurredAtUtc = occurredAtUtc,
            CreatedAtUtc = DateTimeOffset.UtcNow
        });
    }

    private static bool IsMissedCall(AppCallCdrEntity call)
    {
        return !call.IsActive
            && call.Direction == SoftphoneCallDirection.Incoming
            && !call.AnsweredAtUtc.HasValue;
    }

    private static bool IsFailedCall(AppCallCdrEntity call)
    {
        return !call.IsActive
            && call.Direction == SoftphoneCallDirection.Outgoing
            && !call.AnsweredAtUtc.HasValue;
    }

    private static long? ResolveTalkDurationSeconds(
        DateTimeOffset? answeredAtUtc,
        DateTimeOffset? endedAtUtc,
        bool isActive,
        DateTimeOffset now)
    {
        if (!answeredAtUtc.HasValue)
        {
            return null;
        }

        var effectiveEnd = endedAtUtc ?? (isActive ? now : answeredAtUtc.Value);
        var duration = effectiveEnd - answeredAtUtc.Value;
        if (duration <= TimeSpan.Zero)
        {
            return 0;
        }

        return (long)Math.Round(duration.TotalSeconds, MidpointRounding.AwayFromZero);
    }

    private static long? ResolveTotalDurationSeconds(
        DateTimeOffset startedAtUtc,
        DateTimeOffset? endedAtUtc,
        bool isActive,
        DateTimeOffset now)
    {
        var effectiveEnd = endedAtUtc ?? (isActive ? now : null);
        if (!effectiveEnd.HasValue)
        {
            return null;
        }

        var duration = effectiveEnd.Value - startedAtUtc;
        if (duration <= TimeSpan.Zero)
        {
            return 0;
        }

        return (long)Math.Round(duration.TotalSeconds, MidpointRounding.AwayFromZero);
    }

    private static string BuildPbxTrackingKey(int operatorUserId, long participantId)
    {
        return $"pbx:{operatorUserId}:{participantId}";
    }

    private static string BuildWebRtcTrackingKey(string callId, int operatorUserId)
    {
        return $"webrtc:{callId}:{operatorUserId}";
    }

    private static string NormalizeStatus(string? status, bool isEnded)
    {
        if (isEnded)
        {
            return "Ended";
        }

        var normalized = NormalizeText(status);
        return string.IsNullOrWhiteSpace(normalized) ? "Unknown" : normalized;
    }

    private static string NormalizeText(string? value)
    {
        return value?.Trim() ?? string.Empty;
    }

    private static string BuildDisplayName(string? firstName, string? lastName, string fallback)
    {
        var composed = string.Join(" ", new[] { firstName?.Trim(), lastName?.Trim() }.Where(v => !string.IsNullOrWhiteSpace(v)));
        return string.IsNullOrWhiteSpace(composed) ? fallback : composed;
    }
}

public sealed record PbxCallCdrUpdate
{
    public required int OperatorUserId { get; init; }
    public required string OperatorUsername { get; init; }
    public required string OperatorExtension { get; init; }
    public required string SourceDn { get; init; }
    public required long ParticipantId { get; init; }
    public long? PbxCallId { get; init; }
    public long? PbxLegId { get; init; }
    public required string Status { get; init; }
    public required SoftphoneCallDirection Direction { get; init; }
    public string? RemoteParty { get; init; }
    public string? RemoteName { get; init; }
    public DateTimeOffset? ConnectedAtUtc { get; init; }
    public DateTimeOffset OccurredAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public bool IsEnded { get; init; }
    public string? EndReason { get; init; }
    public string EventType { get; init; } = "pbx.status";
}

public sealed record WebRtcCallCdrUpdate
{
    public required string CallId { get; init; }
    public required int OperatorUserId { get; init; }
    public required string OperatorUsername { get; init; }
    public required string OperatorExtension { get; init; }
    public required SoftphoneCallDirection Direction { get; init; }
    public required string Status { get; init; }
    public string? RemoteParty { get; init; }
    public string? RemoteName { get; init; }
    public DateTimeOffset StartedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? AnsweredAtUtc { get; init; }
    public DateTimeOffset? EndedAtUtc { get; init; }
    public bool IsEnded { get; init; }
    public string? EndReason { get; init; }
    public DateTimeOffset OccurredAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public string EventType { get; init; } = "browser.status";
}
