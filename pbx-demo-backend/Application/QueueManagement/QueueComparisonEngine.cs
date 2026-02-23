namespace CallControl.Api.Application.QueueManagement;

public sealed class QueueComparisonEngine
{
    public QueueComparisonSummary Build(IReadOnlyList<QueueAnalyticsOverviewResult> overviews)
    {
        overviews ??= [];

        if (overviews.Count == 0)
        {
            return new QueueComparisonSummary
            {
                NormalizationMethod = "MinMax",
                Scores = []
            };
        }

        var slaValues = overviews.Select(x => (double?)x.SlaPct).Where(x => x is not null).Select(x => x!.Value).ToArray();
        var waitingValues = overviews.Select(x => (double?)(x.AverageWaitingMsAll ?? x.AverageWaitingMs)).Where(x => x is not null).Select(x => x!.Value).ToArray();
        var abandonValues = overviews.Select(x => (double?)(x.AbandonmentRatePct)).Where(x => x is not null).Select(x => x!.Value).ToArray();

        var slaRange = MinMax(slaValues);
        var waitingRange = MinMax(waitingValues);
        var abandonRange = MinMax(abandonValues);

        var scores = new List<QueueComparisonScoreResult>(overviews.Count);
        foreach (var overview in overviews)
        {
            var slaComponent = NormalizeHigherBetter(overview.SlaPct, slaRange);
            var waitingComponent = NormalizeLowerBetter(overview.AverageWaitingMsAll ?? overview.AverageWaitingMs, waitingRange);
            var abandonComponent = NormalizeLowerBetter(overview.AbandonmentRatePct, abandonRange);

            var mqi = (0.40m * slaComponent) + (0.35m * waitingComponent) + (0.25m * abandonComponent);
            scores.Add(new QueueComparisonScoreResult
            {
                QueueId = overview.QueueId,
                MQI = Math.Round(mqi * 100m, 2, MidpointRounding.AwayFromZero),
                Rank = 0,
                Components = new QueueComparisonComponentBreakdown
                {
                    ServiceLevelComponent = Math.Round(slaComponent * 100m, 2, MidpointRounding.AwayFromZero),
                    WaitingInverseComponent = Math.Round(waitingComponent * 100m, 2, MidpointRounding.AwayFromZero),
                    AbandonmentInverseComponent = Math.Round(abandonComponent * 100m, 2, MidpointRounding.AwayFromZero)
                }
            });
        }

        var ranked = scores.OrderByDescending(x => x.MQI).ThenBy(x => x.QueueId).ToList();
        for (var i = 0; i < ranked.Count; i++)
        {
            ranked[i].Rank = i + 1;
        }

        return new QueueComparisonSummary
        {
            NormalizationMethod = "MinMax",
            Scores = ranked
        };
    }

    private static (double Min, double Max, bool HasValues) MinMax(double[] values)
    {
        if (values.Length == 0)
        {
            return (0d, 0d, false);
        }

        return (values.Min(), values.Max(), true);
    }

    private static decimal NormalizeHigherBetter(decimal? value, (double Min, double Max, bool HasValues) range)
    {
        if (value is null || !range.HasValues)
        {
            return 0.5m;
        }

        if (Math.Abs(range.Max - range.Min) < 0.000001d)
        {
            return 0.5m;
        }

        var normalized = ((double)value.Value - range.Min) / (range.Max - range.Min);
        return Clamp01((decimal)normalized);
    }

    private static decimal NormalizeLowerBetter(long? value, (double Min, double Max, bool HasValues) range)
    {
        if (value is null)
        {
            return 0.5m;
        }

        return NormalizeLowerBetter((decimal)value.Value, range);
    }

    private static decimal NormalizeLowerBetter(decimal? value, (double Min, double Max, bool HasValues) range)
    {
        if (value is null || !range.HasValues)
        {
            return 0.5m;
        }

        if (Math.Abs(range.Max - range.Min) < 0.000001d)
        {
            return 0.5m;
        }

        var normalized = 1d - (((double)value.Value - range.Min) / (range.Max - range.Min));
        return Clamp01((decimal)normalized);
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

public sealed class QueueComparisonSummary
{
    public string NormalizationMethod { get; set; } = "MinMax";
    public List<QueueComparisonScoreResult> Scores { get; set; } = [];
}

public sealed class QueueComparisonScoreResult
{
    public long QueueId { get; set; }
    public int Rank { get; set; }
    public decimal MQI { get; set; }
    public QueueComparisonComponentBreakdown Components { get; set; } = new();
}

public sealed class QueueComparisonComponentBreakdown
{
    public decimal ServiceLevelComponent { get; set; }
    public decimal WaitingInverseComponent { get; set; }
    public decimal AbandonmentInverseComponent { get; set; }
}

