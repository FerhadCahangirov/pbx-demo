namespace pbx_demo_backend.Domain.QueueManagement.Contracts;

// Batch 1 external DTO contracts for documented queue analytics report endpoints.

public sealed class XapiPbxAbandonedQueueCallsDto
{
    public string? CallerId { get; set; }
    public string? CallHistoryId { get; set; }
    public DateTimeOffset? CallTime { get; set; }
    public DateTimeOffset? CallTimeForCsv { get; set; }
    public string? ExtensionDisplayName { get; set; }
    public string? ExtensionDn { get; set; }
    public bool? IsLoggedIn { get; set; }
    public long? PollingAttempts { get; set; }
    public string? QueueDisplayName { get; set; }
    public string QueueDn { get; set; } = string.Empty;
    public string? WaitTime { get; set; } // OpenAPI format=duration
}

public sealed class XapiPbxAgentLoginHistoryDto
{
    public string Agent { get; set; } = string.Empty;
    public string AgentNo { get; set; } = string.Empty;
    public DateTimeOffset? Day { get; set; }
    public string? LoggedInDayInterval { get; set; }
    public DateTimeOffset? LoggedInDt { get; set; }
    public string? LoggedInInterval { get; set; }
    public string? LoggedInTotalInterval { get; set; }
    public DateTimeOffset? LoggedOutDt { get; set; }
    public string QueueNo { get; set; } = string.Empty;
    public string? TalkingDayInterval { get; set; }
    public string? TalkingInterval { get; set; }
    public string? TalkingTotalInterval { get; set; }
}

public sealed class XapiPbxAgentsInQueueStatisticsDto
{
    public long? AnsweredCount { get; set; }
    public int? AnsweredPercent { get; set; }
    public long? AnsweredPerHourCount { get; set; }
    public string? AvgRingTime { get; set; }
    public string? AvgTalkTime { get; set; }
    public string Dn { get; set; } = string.Empty;
    public string? DnDisplayName { get; set; }
    public string? LoggedInTime { get; set; }
    public long? LostCount { get; set; }
    public string? Queue { get; set; }
    public string? QueueDisplayName { get; set; }
    public string? RingTime { get; set; }
    public string? TalkTime { get; set; }
}

public sealed class XapiPbxBreachesSlaDto
{
    public string CallerId { get; set; } = string.Empty;
    public DateTimeOffset CallTime { get; set; }
    public string Queue { get; set; } = string.Empty;
    public string? QueueDnNumber { get; set; }
    public string? WaitingTime { get; set; }
}

public sealed class XapiPbxCallDistributionDto
{
    public DateTimeOffset DateTimeInterval { get; set; }
    public int IncomingCount { get; set; }
    public int OutgoingCount { get; set; }
}

public sealed class XapiPbxDetailedQueueStatisticsDto
{
    public long? AnsweredCount { get; set; }
    public string? AvgRingTime { get; set; }
    public string? AvgTalkTime { get; set; }
    public long? CallbacksCount { get; set; }
    public long? CallsCount { get; set; }
    public string? QueueDn { get; set; }
    public string QueueDnNumber { get; set; } = string.Empty;
    public string? RingTime { get; set; }
    public string? TalkTime { get; set; }
}

public sealed class XapiPbxQueueAnsweredCallsByWaitTimeDto
{
    public DateTimeOffset AnsweredTime { get; set; }
    public DateTimeOffset CallTime { get; set; }
    public string Destination { get; set; } = string.Empty;
    public string Dn { get; set; } = string.Empty;
    public string? DnNumber { get; set; }
    public string? RingTime { get; set; }
    public int? SentimentScore { get; set; }
    public string Source { get; set; } = string.Empty;
}

public sealed class XapiPbxQueueCallbacksDto
{
    public long? CallbacksCount { get; set; }
    public string? Dn { get; set; }
    public long? FailCallbacksCount { get; set; }
    public string QueueDnNumber { get; set; } = string.Empty;
    public long? ReceivedCount { get; set; }
}

public sealed class XapiPbxQueueFailedCallbacksDto
{
    public string CallbackNo { get; set; } = string.Empty;
    public DateTimeOffset CallTime { get; set; }
    public string Dn { get; set; } = string.Empty;
    public string? QueueDnNumber { get; set; }
    public string? RingTime { get; set; }
}

public sealed class XapiPbxQueuePerformanceOverviewDto
{
    public long? ExtensionAnsweredCount { get; set; }
    public string? ExtensionDisplayName { get; set; }
    public string ExtensionDn { get; set; } = string.Empty;
    public long? ExtensionDroppedCount { get; set; }
    public long? QueueAnsweredCount { get; set; }
    public string QueueDisplayName { get; set; } = string.Empty;
    public string? QueueDn { get; set; }
    public long? QueueReceivedCount { get; set; }
    public int? SortOrder { get; set; }
    public string? TalkTime { get; set; }
}

public sealed class XapiPbxQueuePerformanceTotalsDto
{
    public long? ExtensionAnsweredCount { get; set; }
    public long? ExtensionDroppedCount { get; set; }
    public string? QueueDisplayName { get; set; }
    public string QueueDn { get; set; } = string.Empty;
    public long? QueueReceivedCount { get; set; }
}

public sealed class XapiPbxStatisticSlaDto
{
    public long? BadSlaCallsCount { get; set; }
    public string? Dn { get; set; }
    public string QueueDnNumber { get; set; } = string.Empty;
    public long? ReceivedCount { get; set; }
}

public sealed class XapiPbxTeamQueueGeneralStatisticsDto
{
    public int? AgentsInQueueCount { get; set; }
    public long? AnsweredCount { get; set; }
    public string? AvgTalkTime { get; set; }
    public string? Dn { get; set; }
    public string QueueDnNumber { get; set; } = string.Empty;
    public long? ReceivedCount { get; set; }
    public string? TotalTalkTime { get; set; }
}

public sealed class XapiPbxTimeReportDataDto
{
    public DateTimeOffset XValue { get; set; }
    public int? YValue1 { get; set; }
    public int? YValue2 { get; set; }
}

public sealed class XapiPbxUserActivityDto
{
    public int AnsweredCount { get; set; }
    public DateTimeOffset DateTimeInterval { get; set; }
    public int UnansweredCount { get; set; }
}
