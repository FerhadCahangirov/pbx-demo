using pbx_demo_backend.Domain.QueueManagement.Contracts;

namespace CallControl.Api.Application.QueueManagement;

public sealed class QueueKpiCalculator
{
    private const int DefaultShortAbandonThresholdSec = 5;

    public QueueKpiComputation Calculate(
        long queueId,
        QueueAnalyticsQuery query,
        IReadOnlyList<QueueCallEntity> calls,
        int? queryLevelSlaThresholdSec = null)
    {
        ArgumentNullException.ThrowIfNull(query);
        calls ??= [];

        var materialized = calls
            .Where(c => c.QueuedAtUtc is not null)
            .ToList();

        var answered = materialized.Where(IsAnswered).ToList();
        var abandoned = materialized.Where(IsAbandoned).ToList();
        var missed = materialized.Where(c => c.Disposition == QueueCallDisposition.Missed).ToList();

        var waitingAll = materialized
            .Where(c => c.WaitingMs is not null && c.WaitingMs.Value >= 0)
            .Select(c => c.WaitingMs!.Value)
            .ToArray();
        var waitingAnswered = answered
            .Where(c => c.WaitingMs is not null && c.WaitingMs.Value >= 0)
            .Select(c => c.WaitingMs!.Value)
            .ToArray();
        var talkingAnswered = answered
            .Where(c => c.TalkingMs is not null && c.TalkingMs.Value >= 0)
            .Select(c => c.TalkingMs!.Value)
            .ToArray();

        var shortAbandonThresholdMs = DefaultShortAbandonThresholdSec * 1000L;
        var shortAbandonCount = abandoned.Count(c => c.WaitingMs is not null && c.WaitingMs.Value >= 0 && c.WaitingMs.Value < shortAbandonThresholdMs);

        var slaThresholdSec = queryLevelSlaThresholdSec is > 0
            ? queryLevelSlaThresholdSec
            : materialized.Select(c => c.SlaThresholdSec).Where(v => v is > 0).DefaultIfEmpty().Max();
        var effectiveSlaMs = slaThresholdSec is > 0 ? slaThresholdSec.Value * 1000L : (long?)null;

        var slaEligibleCalls = materialized.Count(c => c.WaitingMs is not null && c.WaitingMs.Value >= 0);
        var slaWithinCalls = effectiveSlaMs is null
            ? (int?)null
            : materialized.Count(c => c.WaitingMs is not null && c.WaitingMs.Value >= 0 && c.WaitingMs.Value <= effectiveSlaMs.Value);
        var slaBreachCalls = effectiveSlaMs is null
            ? (int?)null
            : materialized.Count(c => c.WaitingMs is not null && c.WaitingMs.Value > effectiveSlaMs.Value);

        var concurrency = CalculatePeakConcurrency(query, materialized);
        var qci = CalculateCongestionIndex(materialized, waitingAll, effectiveSlaMs, slaBreachCalls, slaEligibleCalls);

        return new QueueKpiComputation
        {
            QueueId = queueId,
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            TotalCalls = materialized.Count,
            AnsweredCalls = answered.Count,
            AbandonedCalls = abandoned.Count,
            MissedCalls = missed.Count,
            ShortAbandonCount = shortAbandonCount,
            AbandonmentRatePct = materialized.Count == 0
                ? null
                : Math.Round((decimal)abandoned.Count * 100m / materialized.Count, 2, MidpointRounding.AwayFromZero),
            AverageWaitingMsAnswered = waitingAnswered.Length == 0 ? null : (long?)waitingAnswered.Average(),
            AverageWaitingMsAll = waitingAll.Length == 0 ? null : (long?)waitingAll.Average(),
            AverageTalkingMs = talkingAnswered.Length == 0 ? null : (long?)talkingAnswered.Average(),
            P90WaitingMs = Percentile(waitingAll, 0.90),
            P95WaitingMs = Percentile(waitingAll, 0.95),
            P90TalkingMs = Percentile(talkingAnswered, 0.90),
            PeakConcurrency = concurrency.PeakConcurrency,
            PeakConcurrencyAtUtc = concurrency.PeakAtUtc,
            SlaThresholdSec = slaThresholdSec,
            SlaEligibleCalls = slaEligibleCalls,
            SlaWithinThresholdCalls = slaWithinCalls,
            SlaBreachCalls = slaBreachCalls,
            SlaPct = slaEligibleCalls == 0 || slaWithinCalls is null
                ? null
                : Math.Round((decimal)slaWithinCalls.Value * 100m / slaEligibleCalls, 2, MidpointRounding.AwayFromZero),
            QueueCongestionIndex = qci.Index,
            Signals = qci.Signals,
            RealtimeClassification = ClassifyRealtimePrecision(materialized)
        };
    }

    public QueueBucketAggregate ComputeBucketAggregate(
        IReadOnlyList<QueueCallEntity> calls,
        int? defaultSlaThresholdSec = null)
    {
        calls ??= [];

        long totalCalls = 0;
        long answeredCalls = 0;
        long abandonedCalls = 0;
        long missedCalls = 0;
        long waitingSum = 0;
        long waitingCount = 0;
        long talkingSum = 0;
        long talkingCount = 0;
        long slaEligible = 0;
        long slaWithin = 0;

        foreach (var call in calls)
        {
            totalCalls++;

            if (IsAnswered(call))
            {
                answeredCalls++;
            }
            else if (IsAbandoned(call))
            {
                abandonedCalls++;
            }
            else if (call.Disposition == QueueCallDisposition.Missed)
            {
                missedCalls++;
            }

            if (call.WaitingMs is >= 0)
            {
                waitingSum += call.WaitingMs.Value;
                waitingCount++;
                slaEligible++;

                var thresholdSec = call.SlaThresholdSec is > 0
                    ? call.SlaThresholdSec
                    : defaultSlaThresholdSec;
                if (thresholdSec is > 0 && call.WaitingMs.Value <= thresholdSec.Value * 1000L)
                {
                    slaWithin++;
                }
            }

            if (IsAnswered(call) && call.TalkingMs is >= 0)
            {
                talkingSum += call.TalkingMs.Value;
                talkingCount++;
            }
        }

        return new QueueBucketAggregate
        {
            TotalCalls = totalCalls,
            AnsweredCalls = answeredCalls,
            AbandonedCalls = abandonedCalls,
            MissedCalls = missedCalls,
            WaitingMsSum = waitingSum,
            WaitingMsCount = waitingCount,
            TalkingMsSum = talkingSum,
            TalkingMsCount = talkingCount,
            SlaEligibleCalls = slaEligible,
            SlaWithinThresholdCalls = slaWithin
        };
    }

    private static QueuePeakConcurrencyComputation CalculatePeakConcurrency(QueueAnalyticsQuery query, IReadOnlyList<QueueCallEntity> calls)
    {
        var events = new List<(DateTimeOffset AtUtc, int Delta)>(calls.Count * 2);

        foreach (var call in calls)
        {
            var start = call.QueuedAtUtc;
            if (start is null)
            {
                continue;
            }

            var end = call.CompletedAtUtc ?? call.AbandonedAtUtc ?? call.LastSeenAtUtc;
            var intervalStart = start.Value < query.FromUtc ? query.FromUtc : start.Value;
            var intervalEnd = end > query.ToUtc ? query.ToUtc : end;

            if (intervalEnd <= intervalStart)
            {
                continue;
            }

            events.Add((intervalStart, +1));
            events.Add((intervalEnd, -1));
        }

        if (events.Count == 0)
        {
            return new QueuePeakConcurrencyComputation();
        }

        var ordered = events
            .OrderBy(x => x.AtUtc)
            .ThenBy(x => x.Delta) // process departures before arrivals at same timestamp for [start,end)
            .ToList();

        var current = 0;
        var peak = 0;
        DateTimeOffset? peakAt = null;

        foreach (var evt in ordered)
        {
            current += evt.Delta;
            if (current > peak)
            {
                peak = current;
                peakAt = evt.AtUtc;
            }
        }

        return new QueuePeakConcurrencyComputation
        {
            PeakConcurrency = Math.Max(0, peak),
            PeakAtUtc = peakAt
        };
    }

    private static QueueCongestionComputation CalculateCongestionIndex(
        IReadOnlyList<QueueCallEntity> calls,
        IReadOnlyList<long> waitingDurationsMs,
        long? slaThresholdMs,
        int? slaBreachCalls,
        int slaEligibleCalls)
    {
        if (calls.Count == 0)
        {
            return new QueueCongestionComputation
            {
                Index = 0m,
                Signals = new QueueCongestionSignalBreakdown
                {
                    WaitingLoadScore = 0m,
                    WaitingTimeScore = 0m,
                    SlaBreachScore = 0m,
                    CompositeScore = 0m
                }
            };
        }

        var openWaitingCount = calls.Count(c =>
            c.CurrentStatus is QueueCallLifecycleStatus.EnteredQueue or QueueCallLifecycleStatus.Waiting);
        var avgWaitingMs = waitingDurationsMs.Count == 0 ? 0d : waitingDurationsMs.Average();

        // Normalized dimensions (0..1)
        var waitingLoadScore = Clamp01(openWaitingCount / 20d); // configurable later
        var waitingTimeScore = slaThresholdMs is > 0
            ? Clamp01(avgWaitingMs / (slaThresholdMs.Value * 2d))
            : Clamp01(avgWaitingMs / 120000d);
        var slaBreachScore = slaEligibleCalls <= 0 || slaBreachCalls is null
            ? 0d
            : Clamp01((double)slaBreachCalls.Value / slaEligibleCalls);

        var composite = (0.35d * waitingLoadScore) + (0.35d * waitingTimeScore) + (0.30d * slaBreachScore);
        var index = (decimal)Math.Round(composite * 100d, 2, MidpointRounding.AwayFromZero);

        return new QueueCongestionComputation
        {
            Index = index,
            Signals = new QueueCongestionSignalBreakdown
            {
                WaitingLoadScore = Math.Round((decimal)(waitingLoadScore * 100d), 2),
                WaitingTimeScore = Math.Round((decimal)(waitingTimeScore * 100d), 2),
                SlaBreachScore = Math.Round((decimal)(slaBreachScore * 100d), 2),
                CompositeScore = index
            }
        };
    }

    private static string ClassifyRealtimePrecision(IReadOnlyList<QueueCallEntity> calls)
    {
        if (calls.Count == 0)
        {
            return "Finalized";
        }

        var hasOpenCalls = calls.Any(c =>
            c.CurrentStatus != QueueCallLifecycleStatus.Completed &&
            c.CurrentStatus != QueueCallLifecycleStatus.Abandoned);

        var hasMissingDurations = calls.Any(c => IsAnswered(c) && c.TalkingMs is null);
        return (hasOpenCalls || hasMissingDurations) ? "ProvisionalRealtime" : "Finalized";
    }

    private static bool IsAnswered(QueueCallEntity call)
        => call.AnsweredAtUtc is not null || call.Disposition == QueueCallDisposition.Answered;

    private static bool IsAbandoned(QueueCallEntity call)
        => (call.AbandonedAtUtc is not null && call.AnsweredAtUtc is null)
           || call.Disposition == QueueCallDisposition.Abandoned;

    private static long? Percentile(long[] values, double p)
    {
        if (values is null || values.Length == 0)
        {
            return null;
        }

        if (values.Length == 1)
        {
            return values[0];
        }

        Array.Sort(values);
        var rank = (values.Length - 1) * p;
        var lower = (int)Math.Floor(rank);
        var upper = (int)Math.Ceiling(rank);
        if (lower == upper)
        {
            return values[lower];
        }

        var fraction = rank - lower;
        var interpolated = values[lower] + ((values[upper] - values[lower]) * fraction);
        return (long)Math.Round(interpolated, MidpointRounding.AwayFromZero);
    }

    private static double Clamp01(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return 0d;
        }

        if (value < 0d)
        {
            return 0d;
        }

        if (value > 1d)
        {
            return 1d;
        }

        return value;
    }
}

public sealed class QueueKpiComputation
{
    public long QueueId { get; set; }
    public DateTimeOffset GeneratedAtUtc { get; set; }
    public long TotalCalls { get; set; }
    public long AnsweredCalls { get; set; }
    public long AbandonedCalls { get; set; }
    public long MissedCalls { get; set; }
    public long ShortAbandonCount { get; set; }
    public decimal? AbandonmentRatePct { get; set; }
    public long? AverageWaitingMsAnswered { get; set; }
    public long? AverageWaitingMsAll { get; set; }
    public long? AverageTalkingMs { get; set; }
    public long? P90WaitingMs { get; set; }
    public long? P95WaitingMs { get; set; }
    public long? P90TalkingMs { get; set; }
    public int PeakConcurrency { get; set; }
    public DateTimeOffset? PeakConcurrencyAtUtc { get; set; }
    public int? SlaThresholdSec { get; set; }
    public int SlaEligibleCalls { get; set; }
    public int? SlaWithinThresholdCalls { get; set; }
    public int? SlaBreachCalls { get; set; }
    public decimal? SlaPct { get; set; }
    public decimal QueueCongestionIndex { get; set; }
    public QueueCongestionSignalBreakdown Signals { get; set; } = new();
    public string RealtimeClassification { get; set; } = "Finalized";
}

public sealed class QueueCongestionSignalBreakdown
{
    public decimal WaitingLoadScore { get; set; }
    public decimal WaitingTimeScore { get; set; }
    public decimal SlaBreachScore { get; set; }
    public decimal CompositeScore { get; set; }
}

public sealed class QueueBucketAggregate
{
    public long TotalCalls { get; set; }
    public long AnsweredCalls { get; set; }
    public long AbandonedCalls { get; set; }
    public long MissedCalls { get; set; }
    public long WaitingMsSum { get; set; }
    public long WaitingMsCount { get; set; }
    public long TalkingMsSum { get; set; }
    public long TalkingMsCount { get; set; }
    public long SlaEligibleCalls { get; set; }
    public long SlaWithinThresholdCalls { get; set; }
}

internal sealed class QueuePeakConcurrencyComputation
{
    public int PeakConcurrency { get; set; }
    public DateTimeOffset? PeakAtUtc { get; set; }
}

internal sealed class QueueCongestionComputation
{
    public decimal Index { get; set; }
    public QueueCongestionSignalBreakdown Signals { get; set; } = new();
}
