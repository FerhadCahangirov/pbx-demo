using CallControl.Api.Domain;
using CallControl.Api.Infrastructure;
using CallControl.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CallControl.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/softphone")]
public sealed class SoftphoneController : ControllerBase
{
    private readonly CallManager _callManager;

    public SoftphoneController(CallManager callManager)
    {
        _callManager = callManager;
    }

    [HttpGet("session")]
    public async Task<ActionResult<SessionSnapshotResponse>> GetSession(CancellationToken cancellationToken)
    {
        var snapshot = await _callManager.GetSnapshotAsync(User.RequireSessionId(), cancellationToken);
        return Ok(snapshot);
    }

    [HttpGet("extensions")]
    public async Task<ActionResult<object>> GetExtensions(CancellationToken cancellationToken)
    {
        var snapshot = await _callManager.GetSnapshotAsync(User.RequireSessionId(), cancellationToken);
        return Ok(new
        {
            selectedExtensionDn = snapshot.SelectedExtensionDn,
            ownedExtensionDn = snapshot.OwnedExtensionDn
        });
    }

    [HttpPost("extensions/select")]
    public async Task<IActionResult> SelectExtension(CancellationToken cancellationToken)
    {
        var resolvedExtensionDn = await RequestInputResolver.ResolveFieldAsync(
            Request,
            "extensionDn",
            cancellationToken,
            allowRawFallback: true);

        await _callManager.SelectExtensionAsync(User.RequireSessionId(), resolvedExtensionDn ?? string.Empty, cancellationToken);
        return Accepted();
    }

    [HttpPost("devices/active")]
    public async Task<IActionResult> SetActiveDevice(CancellationToken cancellationToken)
    {
        var deviceId = await RequestInputResolver.ResolveFieldAsync(
            Request,
            "deviceId",
            cancellationToken,
            allowRawFallback: true);

        await _callManager.SetActiveDeviceAsync(User.RequireSessionId(), deviceId ?? string.Empty, cancellationToken);
        return Accepted();
    }

    [HttpPost("calls/outgoing")]
    public async Task<IActionResult> MakeOutgoingCall(CancellationToken cancellationToken)
    {
        var destination = await RequestInputResolver.ResolveFieldAsync(
            Request,
            "destination",
            cancellationToken,
            allowRawFallback: true);

        await _callManager.MakeOutgoingCallAsync(User.RequireSessionId(), destination ?? string.Empty, cancellationToken);
        return Accepted();
    }

    [HttpPost("calls/{participantId:long}/answer")]
    public async Task<IActionResult> AnswerCall(long participantId, CancellationToken cancellationToken)
    {
        await _callManager.AnswerCallAsync(User.RequireSessionId(), participantId, cancellationToken);
        return Accepted();
    }

    [HttpPost("calls/{participantId:long}/reject")]
    public async Task<IActionResult> RejectCall(long participantId, CancellationToken cancellationToken)
    {
        await _callManager.RejectCallAsync(User.RequireSessionId(), participantId, cancellationToken);
        return Accepted();
    }

    [HttpPost("calls/{participantId:long}/end")]
    public async Task<IActionResult> EndCall(long participantId, CancellationToken cancellationToken)
    {
        await _callManager.EndCallAsync(User.RequireSessionId(), participantId, cancellationToken);
        return Accepted();
    }

    [HttpPost("calls/{participantId:long}/transfer")]
    public async Task<IActionResult> TransferCall(long participantId, CancellationToken cancellationToken)
    {
        var destination = await RequestInputResolver.ResolveFieldAsync(
            Request,
            "destination",
            cancellationToken,
            allowRawFallback: true);

        await _callManager.TransferCallAsync(User.RequireSessionId(), participantId, destination ?? string.Empty, cancellationToken);
        return Accepted();
    }
}
