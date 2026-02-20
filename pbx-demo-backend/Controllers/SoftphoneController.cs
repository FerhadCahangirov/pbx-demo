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
    private readonly SipConfigurationService _sipConfigurationService;

    public SoftphoneController(CallManager callManager, SipConfigurationService sipConfigurationService)
    {
        _callManager = callManager;
        _sipConfigurationService = sipConfigurationService;
    }

    [HttpGet("session")]
    public async Task<ActionResult<SessionSnapshotResponse>> GetSession(CancellationToken cancellationToken)
    {
        var sessionId = await EnsureSessionAsync(cancellationToken);
        var snapshot = await _callManager.GetSnapshotAsync(sessionId, cancellationToken);
        return Ok(snapshot);
    }

    [HttpGet("extensions")]
    public async Task<ActionResult<object>> GetExtensions(CancellationToken cancellationToken)
    {
        var sessionId = await EnsureSessionAsync(cancellationToken);
        var snapshot = await _callManager.GetSnapshotAsync(sessionId, cancellationToken);
        return Ok(new
        {
            selectedExtensionDn = snapshot.SelectedExtensionDn,
            ownedExtensionDn = snapshot.OwnedExtensionDn
        });
    }

    [HttpGet("sip/config")]
    public async Task<ActionResult<SipRegistrationConfigResponse>> GetSipConfig(CancellationToken cancellationToken)
    {
        var response = await _sipConfigurationService.GetForUsernameAsync(User.RequireUsername(), cancellationToken);
        return Ok(response);
    }

    [HttpPost("extensions/select")]
    public async Task<IActionResult> SelectExtension(CancellationToken cancellationToken)
    {
        var sessionId = await EnsureSessionAsync(cancellationToken);
        var resolvedExtensionDn = await RequestInputResolver.ResolveFieldAsync(
            Request,
            "extensionDn",
            cancellationToken,
            allowRawFallback: true);

        await _callManager.SelectExtensionAsync(sessionId, resolvedExtensionDn ?? string.Empty, cancellationToken);
        return Accepted();
    }

    [HttpPost("devices/active")]
    public async Task<IActionResult> SetActiveDevice(CancellationToken cancellationToken)
    {
        var sessionId = await EnsureSessionAsync(cancellationToken);
        var deviceId = await RequestInputResolver.ResolveFieldAsync(
            Request,
            "deviceId",
            cancellationToken,
            allowRawFallback: true);

        await _callManager.SetActiveDeviceAsync(sessionId, deviceId ?? string.Empty, cancellationToken);
        return Accepted();
    }

    [HttpPost("calls/outgoing")]
    public async Task<IActionResult> MakeOutgoingCall(CancellationToken cancellationToken)
    {
        var sessionId = await EnsureSessionAsync(cancellationToken);
        var destination = await RequestInputResolver.ResolveFieldAsync(
            Request,
            "destination",
            cancellationToken,
            allowRawFallback: true);

        await _callManager.MakeOutgoingCallAsync(sessionId, destination ?? string.Empty, cancellationToken);
        return Accepted();
    }

    [HttpPost("calls/{participantId:long}/answer")]
    public async Task<IActionResult> AnswerCall(long participantId, CancellationToken cancellationToken)
    {
        var sessionId = await EnsureSessionAsync(cancellationToken);
        await _callManager.AnswerCallAsync(sessionId, participantId, cancellationToken);
        return Accepted();
    }   

    [HttpPost("calls/{participantId:long}/reject")]
    public async Task<IActionResult> RejectCall(long participantId, CancellationToken cancellationToken)
    {
        var sessionId = await EnsureSessionAsync(cancellationToken);
        await _callManager.RejectCallAsync(sessionId, participantId, cancellationToken);
        return Accepted();
    }

    [HttpPost("calls/{participantId:long}/end")]
    public async Task<IActionResult> EndCall(long participantId, CancellationToken cancellationToken)
    {
        var sessionId = await EnsureSessionAsync(cancellationToken);
        await _callManager.EndCallAsync(sessionId, participantId, cancellationToken);
        return Accepted();
    }

    [HttpPost("calls/{participantId:long}/transfer")]
    public async Task<IActionResult> TransferCall(long participantId, CancellationToken cancellationToken)
    {
        var sessionId = await EnsureSessionAsync(cancellationToken);
        var destination = await RequestInputResolver.ResolveFieldAsync(
            Request,
            "destination",
            cancellationToken,
            allowRawFallback: true);

        await _callManager.TransferCallAsync(sessionId, participantId, destination ?? string.Empty, cancellationToken);
        return Accepted();
    }

    [HttpGet("calls/{participantId:long}/audio")]
    public async Task<IActionResult> GetCallAudio(long participantId, CancellationToken cancellationToken)
    {
        var sessionId = await EnsureSessionAsync(cancellationToken);
        var (stream, contentType) = await _callManager.OpenParticipantAudioDownlinkAsync(
            sessionId,
            participantId,
            cancellationToken);

        return File(stream, string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType);
    }

    [HttpPost("calls/{participantId:long}/audio")]
    public async Task<IActionResult> PostCallAudio(long participantId, CancellationToken cancellationToken)
    {
        var sessionId = await EnsureSessionAsync(cancellationToken);
        await _callManager.SendParticipantAudioUplinkAsync(
            sessionId,
            participantId,
            Request.Body,
            cancellationToken);

        return Accepted();
    }

    private async Task<string> EnsureSessionAsync(CancellationToken cancellationToken)
    {
        var sessionId = User.RequireSessionId();
        var username = User.RequireUsername();
        await _callManager.EnsureSessionAsync(sessionId, username, cancellationToken);
        return sessionId;
    }
}
