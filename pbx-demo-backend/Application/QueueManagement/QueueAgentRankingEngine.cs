using pbx_demo_backend.Domain.QueueManagement.Contracts;

namespace CallControl.Api.Application.QueueManagement;

public sealed class QueueAgentRankingEngine
{
    public List<QueueAgentRankingResult> Build(
        long queueId,
        QueueAnalyticsQuery query,
        IReadOnlyList<QueueCallEntity> calls,
        IReadOnlyList<QueueAgentActivityEntity> activities,
        IReadOnlyDictionary<long, ExtensionEntity> extensionsById)
    {
        ArgumentNullException.ThrowIfNull(query);
        calls ??= [];
        activities ??= [];
        extensionsById ??= new Dictionary<long, ExtensionEntity>();

        var answeredCalls = calls
            .Where(c => c.QueueId == queueId)
            .Where(c => c.AnsweredByExtensionId is not null)
            .Where(c => c.AnsweredAtUtc is not null)
            .ToList();

        var callsByAgent = answeredCalls
            .GroupBy(c => c.AnsweredByExtensionId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        var activityByAgent = activities
            .Where(a => a.QueueId == queueId)
            .GroupBy(a => a.ExtensionId)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.OccurredAtUtc).ToList());

        var allAgentIds = callsByAgent.Keys
            .Concat(activityByAgent.Keys)
            .Distinct()
            .OrderBy(x => x)
            .ToArray();

        if (allAgentIds.Length == 0)
        {
            return [];
        }

        var provisionalRows = new List<QueueAgentRankingComputation>(allAgentIds.Length);
        foreach (var agentId in allAgentIds)
        {
            callsByAgent.TryGetValue(agentId, out var answeredByAgent);
            answeredByAgent ??= [];

            activityByAgent.TryGetValue(agentId, out var agentActivities);
            agentActivities ??= [];

            var answeredCount = answeredByAgent.Count;
            var avgWaitingMs = answeredByAgent.Where(c => c.WaitingMs is not null).Select(c => c.WaitingMs!.Value).DefaultIfEmpty().Average();
            var avgTalkingMs = answeredByAgent.Where(c => c.TalkingMs is not null).Select(c => c.TalkingMs!.Value).DefaultIfEmpty().Average();
            var slaThresholdMs = (query.SlaThresholdSec ?? answeredByAgent.Select(c => c.SlaThresholdSec).Where(v => v is > 0).DefaultIfEmpty().Max() ?? 0) * 1000L;
            var slaCompliance = answeredCount == 0 || slaThresholdMs <= 0
                ? (decimal?)null
                : Math.Round((decimal)answeredByAgent.Count(c => c.WaitingMs is not null && c.WaitingMs.Value <= slaThresholdMs) / answeredCount, 4, MidpointRounding.AwayFromZero);

            var talkTimeMs = answeredByAgent.Where(c => c.TalkingMs is not null && c.TalkingMs.Value >= 0).Sum(c => c.TalkingMs!.Value);
            var acwTimeMs = agentActivities
                .Where(a => a.ActivityType is QueueAgentActivityType.WrapUpStart or QueueAgentActivityType.WrapUpEnd)
                .Where(a => a.DurationMs is not null && a.DurationMs.Value >= 0)
                .Sum(a => a.DurationMs!.Value);

            var loggedInMs = EstimateLoggedInDurationMs(query, agentActivities);
            var occupancyMsDenominator = loggedInMs > 0 ? loggedInMs : (long)(query.ToUtc - query.FromUtc).TotalMilliseconds;
            var utilization = occupancyMsDenominator <= 0 ? 0m : Clamp01((decimal)talkTimeMs / occupancyMsDenominator);
            var occupancy = occupancyMsDenominator <= 0 ? 0m : Clamp01((decimal)(talkTimeMs + acwTimeMs) / occupancyMsDenominator);

            provisionalRows.Add(new QueueAgentRankingComputation
            {
                AgentId = agentId,
                ExtensionNumber = extensionsById.TryGetValue(agentId, out var ext) ? ext.ExtensionNumber : string.Empty,
                DisplayName = extensionsById.TryGetValue(agentId, out ext) ? ext.DisplayName : null,
                AnsweredCalls = answeredCount,
                AvgWaitingMs = answeredCount == 0 ? null : (long?)Math.Round(avgWaitingMs, MidpointRounding.AwayFromZero),
                AvgTalkingMs = answeredCount == 0 ? null : (long?)Math.Round(avgTalkingMs, MidpointRounding.AwayFromZero),
                SlaCompliance = slaCompliance,
                Utilization = utilization,
                Occupancy = occupancy,
                LoggedInMs = loggedInMs,
                TalkMs = talkTimeMs,
                AcwMs = acwTimeMs
            });
        }

        var answerCountRange = Range(provisionalRows.Select(x => (double)x.AnsweredCalls).ToArray());
        var avgHandleRange = Range(provisionalRows.Select(x => (double?)(x.AvgTalkingMs ?? 0)).Where(x => x is not null).Select(x => x!.Value).ToArray());

        var ranked = provisionalRows.Select(x =>
        {
            var slaComponent = x.SlaCompliance ?? 0.5m;
            var answerRateComponent = NormalizeHigherBetter(x.AnsweredCalls, answerCountRange);
            var handleTimeInverseComponent = NormalizeLowerBetter(x.AvgTalkingMs ?? 0, avgHandleRange);
            var utilizationComponent = x.Occupancy > 0m ? x.Occupancy : x.Utilization;

            var score = (0.35m * slaComponent) +
                        (0.20m * answerRateComponent) +
                        (0.25m * handleTimeInverseComponent) +
                        (0.20m * utilizationComponent);

            return new QueueAgentRankingResult
            {
                QueueId = queueId,
                AgentId = x.AgentId,
                ExtensionNumber = x.ExtensionNumber,
                DisplayName = x.DisplayName,
                AnsweredCalls = x.AnsweredCalls,
                AverageWaitingMs = x.AvgWaitingMs,
                AverageTalkingMs = x.AvgTalkingMs,
                SlaCompliancePct = x.SlaCompliance is null ? null : Math.Round(x.SlaCompliance.Value * 100m, 2, MidpointRounding.AwayFromZero),
                UtilizationPct = Math.Round(x.Utilization * 100m, 2, MidpointRounding.AwayFromZero),
                OccupancyPct = Math.Round(x.Occupancy * 100m, 2, MidpointRounding.AwayFromZero),
                AgentRankingScore = Math.Round(score * 100m, 2, MidpointRounding.AwayFromZero),
                Components = new QueueAgentRankingComponents
                {
                    SlaComplianceComponent = Math.Round(slaComponent * 100m, 2),
                    AnswerRateComponent = Math.Round(answerRateComponent * 100m, 2),
                    HandleTimeInverseComponent = Math.Round(handleTimeInverseComponent * 100m, 2),
                    UtilizationComponent = Math.Round(utilizationComponent * 100m, 2)
                }
            };
        })
        .OrderByDescending(x => x.AgentRankingScore)
        .ThenBy(x => x.AgentId)
        .ToList();

        for (var i = 0; i < ranked.Count; i++)
        {
            ranked[i].Rank = i + 1;
        }

        return ranked;
    }

    private static long EstimateLoggedInDurationMs(QueueAnalyticsQuery query, IReadOnlyList<QueueAgentActivityEntity> activities)
    {
        if (activities.Count == 0)
        {
            return 0;
        }

        long explicitLoggedInMs = 0;
        DateTimeOffset? currentLoginAt = null;

        foreach (var evt in activities.OrderBy(x => x.OccurredAtUtc).ThenBy(x => x.Id))
        {
            if (evt.OccurredAtUtc < query.FromUtc || evt.OccurredAtUtc >= query.ToUtc)
            {
                continue;
            }

            if (evt.ActivityType == QueueAgentActivityType.Login)
            {
                currentLoginAt ??= evt.OccurredAtUtc;
            }
            else if (evt.ActivityType == QueueAgentActivityType.Logout && currentLoginAt is not null)
            {
                explicitLoggedInMs += (long)Math.Max(0, (evt.OccurredAtUtc - currentLoginAt.Value).TotalMilliseconds);
                currentLoginAt = null;
            }
        }

        if (currentLoginAt is not null)
        {
            explicitLoggedInMs += (long)Math.Max(0, (query.ToUtc - currentLoginAt.Value).TotalMilliseconds);
        }

        if (explicitLoggedInMs > 0)
        {
            return explicitLoggedInMs;
        }

        // Provisional fallback when login/logout telemetry is not yet populated.
        return (long)Math.Max(0, (query.ToUtc - query.FromUtc).TotalMilliseconds);
    }

    private static (double Min, double Max, bool HasValues) Range(double[] values)
    {
        if (values.Length == 0)
        {
            return (0d, 0d, false);
        }

        return (values.Min(), values.Max(), true);
    }

    private static decimal NormalizeHigherBetter(long value, (double Min, double Max, bool HasValues) range)
    {
        if (!range.HasValues)
        {
            return 0.5m;
        }

        if (Math.Abs(range.Max - range.Min) < 0.000001d)
        {
            return 0.5m;
        }

        return Clamp01((decimal)((value - range.Min) / (range.Max - range.Min)));
    }

    private static decimal NormalizeLowerBetter(long value, (double Min, double Max, bool HasValues) range)
    {
        if (!range.HasValues)
        {
            return 0.5m;
        }

        if (Math.Abs(range.Max - range.Min) < 0.000001d)
        {
            return 0.5m;
        }

        return Clamp01((decimal)(1d - ((value - range.Min) / (range.Max - range.Min))));
    }

    private static decimal Clamp01(decimal value)
    {
        if (value < 0m)
        {
            return 0m;
        }

        if (value > 1m)
        {
            return 1m;
        }

        return value;
    }
}

public sealed class QueueAgentRankingResult
{
    public long QueueId { get; set; }
    public long AgentId { get; set; }
    public string ExtensionNumber { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public int Rank { get; set; }
    public int AnsweredCalls { get; set; }
    public long? AverageWaitingMs { get; set; }
    public long? AverageTalkingMs { get; set; }
    public decimal? SlaCompliancePct { get; set; }
    public decimal UtilizationPct { get; set; }
    public decimal OccupancyPct { get; set; }
    public decimal AgentRankingScore { get; set; }
    public QueueAgentRankingComponents Components { get; set; } = new();
}

public sealed class QueueAgentRankingComponents
{
    public decimal SlaComplianceComponent { get; set; }
    public decimal AnswerRateComponent { get; set; }
    public decimal HandleTimeInverseComponent { get; set; }
    public decimal UtilizationComponent { get; set; }
}

internal sealed class QueueAgentRankingComputation
{
    public long AgentId { get; set; }
    public string ExtensionNumber { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public int AnsweredCalls { get; set; }
    public long? AvgWaitingMs { get; set; }
    public long? AvgTalkingMs { get; set; }
    public decimal? SlaCompliance { get; set; }
    public decimal Utilization { get; set; }
    public decimal Occupancy { get; set; }
    public long LoggedInMs { get; set; }
    public long TalkMs { get; set; }
    public long AcwMs { get; set; }
}

