using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using pbx_demo_backend.Domain.QueueManagement.Contracts;

namespace CallControl.Api.Controllers;

[ApiController]
[Authorize]
[EnableRateLimiting("QueueApi")]
[Route("api/queue-analytics")]
public sealed class QueueAnalyticsController : ControllerBase
{
    private readonly IQueueAnalyticsService _queueAnalyticsService;

    public QueueAnalyticsController(IQueueAnalyticsService queueAnalyticsService)
    {
        _queueAnalyticsService = queueAnalyticsService;
    }

    [HttpGet("{queueId:long}")]
    public async Task<ActionResult<object>> GetQueueAnalytics(
        long queueId,
        [FromQuery] QueueAnalyticsQuery query,
        CancellationToken cancellationToken)
    {
        var result = await _queueAnalyticsService.GetQueueAnalyticsAsync(queueId, query, cancellationToken);
        return Ok(result);
    }

    [HttpGet("compare")]
    public async Task<ActionResult<object>> CompareQueues(
        [FromQuery(Name = "queueId")] long[] queueIds,
        [FromQuery] QueueAnalyticsQuery query,
        CancellationToken cancellationToken)
    {
        var result = await _queueAnalyticsService.GetMultiQueueComparisonAsync(queueIds, query, cancellationToken);
        return Ok(result);
    }
}

