using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using pbx_demo_backend.Domain.QueueManagement.Contracts;

namespace CallControl.Api.Controllers;

[ApiController]
//[Authorize]
[EnableRateLimiting("QueueApi")]
[Route("api/queues")]
public sealed class QueuesController : ControllerBase
{
    private readonly IQueueService _queueService;

    public QueuesController(IQueueService queueService)
    {
        _queueService = queueService;
    }

    [HttpGet]
    public async Task<ActionResult<QueuePagedResult<QueueDto>>> ListQueues([FromQuery] QueueListQuery query, CancellationToken cancellationToken)
    {
        var result = await _queueService.GetQueuesAsync(query ?? new QueueListQuery(), cancellationToken);
        return Ok(result);
    }

    [HttpGet("{queueId:long}")]
    public async Task<ActionResult<QueueDto>> GetQueue(long queueId, CancellationToken cancellationToken)
    {
        var result = await _queueService.GetQueueAsync(queueId, cancellationToken);
        return Ok(result);
    }

    //[Authorize(Policy = "SupervisorOnly")]
    [HttpPost]
    public async Task<ActionResult<QueueDto>> CreateQueue([FromBody] CreateQueueRequest request, CancellationToken cancellationToken)
    {
        var created = await _queueService.CreateQueueAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetQueue), new { queueId = created.Id }, created);
    }

    //[Authorize(Policy = "SupervisorOnly")]
    [HttpPatch("{queueId:long}")]
    public async Task<ActionResult<QueueDto>> UpdateQueue(long queueId, [FromBody] UpdateQueueRequest request, CancellationToken cancellationToken)
    {
        var updated = await _queueService.UpdateQueueAsync(queueId, request, cancellationToken);
        return Ok(updated);
    }

    //[Authorize(Policy = "SupervisorOnly")]
    [HttpDelete("{queueId:long}")]
    public async Task<IActionResult> DeleteQueue(long queueId, CancellationToken cancellationToken)
    {
        await _queueService.DeleteQueueAsync(queueId, cancellationToken);
        return NoContent();
    }
}

