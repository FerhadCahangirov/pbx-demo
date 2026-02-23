using CallControl.Api.Application.QueueManagement;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using pbx_demo_backend.Domain.QueueManagement.Contracts;

namespace CallControl.Api.Controllers;

[ApiController]
[Authorize]
[EnableRateLimiting("QueueApi")]
[Route("api/queue-live")]
public sealed class QueueLiveController : ControllerBase
{
    private readonly IQueueLiveStateService _queueLiveStateService;
    private readonly IQueueOutboxSignalrPublisher _outboxSignalrPublisher;

    public QueueLiveController(
        IQueueLiveStateService queueLiveStateService,
        IQueueOutboxSignalrPublisher outboxSignalrPublisher)
    {
        _queueLiveStateService = queueLiveStateService;
        _outboxSignalrPublisher = outboxSignalrPublisher;
    }

    [HttpGet("{queueId:long}/snapshot")]
    public async Task<ActionResult<QueueLiveSnapshotDto>> GetSnapshot(long queueId, CancellationToken cancellationToken)
    {
        var snapshot = await _queueLiveStateService.GetSnapshotAsync(queueId, cancellationToken);
        return Ok(snapshot);
    }

    [Authorize(Policy = "SupervisorOnly")]
    [HttpPost("{queueId:long}/publish")]
    public async Task<IActionResult> PublishSnapshot(long queueId, CancellationToken cancellationToken)
    {
        await _queueLiveStateService.PublishSnapshotAsync(queueId, cancellationToken);
        return Accepted(new { queueId });
    }

    [Authorize(Policy = "SupervisorOnly")]
    [HttpPost("outbox/publish")]
    public async Task<ActionResult<object>> PublishOutboxBatch(CancellationToken cancellationToken)
    {
        var processed = await _outboxSignalrPublisher.ProcessPendingAsync(cancellationToken);
        return Ok(new { processed });
    }
}
