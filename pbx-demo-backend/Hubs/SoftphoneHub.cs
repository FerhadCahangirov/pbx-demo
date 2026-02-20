using CallControl.Api.Domain;
using CallControl.Api.Infrastructure;
using CallControl.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace CallControl.Api.Hubs;

[Authorize]
public sealed class SoftphoneHub : Hub<ISoftphoneHubClient>
{
    private readonly CallManager _callManager;
    private readonly WebRtcCallManager _webRtcCallManager;
    private readonly SessionPresenceRegistry _presenceRegistry;
    private readonly ILogger<SoftphoneHub> _logger;

    public SoftphoneHub(
        CallManager callManager,
        WebRtcCallManager webRtcCallManager,
        SessionPresenceRegistry presenceRegistry,
        ILogger<SoftphoneHub> logger)
    {
        _callManager = callManager;
        _webRtcCallManager = webRtcCallManager;
        _presenceRegistry = presenceRegistry;
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        var sessionId = Context.User?.RequireSessionId()
            ?? throw new InvalidOperationException("Session is required.");
        var username = Context.User?.RequireUsername()
            ?? throw new InvalidOperationException("Username is required.");

        await _callManager.EnsureSessionAsync(sessionId, username, Context.ConnectionAborted);

        await Groups.AddToGroupAsync(Context.ConnectionId, HubGroupName.ForSession(sessionId));
        _presenceRegistry.RegisterConnection(sessionId, Context.ConnectionId);

        var snapshot = await _callManager.GetSnapshotAsync(sessionId, Context.ConnectionAborted);
        var browserCalls = await _webRtcCallManager.GetCallsForSessionAsync(sessionId, Context.ConnectionAborted);

        await Clients.Caller.SessionSnapshot(snapshot);
        await Clients.Caller.BrowserCallsSnapshot(browserCalls);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var sessionId = Context.User?.FindFirst(ClaimTypesEx.SessionId)?.Value;
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, HubGroupName.ForSession(sessionId));
            _presenceRegistry.UnregisterConnection(sessionId, Context.ConnectionId);
            await _webRtcCallManager.HandleSessionDisconnectedAsync(sessionId);
        }

        await base.OnDisconnectedAsync(exception);
    }

    public Task<BrowserCallView> PlaceBrowserCall(string destinationExtension)
    {
        var sessionId = Context.User?.RequireSessionId()
            ?? throw new InvalidOperationException("Session is required.");

        return ExecuteHubActionAsync(
            "PlaceBrowserCall",
            () => _webRtcCallManager.PlaceCallAsync(sessionId, destinationExtension, Context.ConnectionAborted));
    }

    public Task AnswerBrowserCall(string callId)
    {
        var sessionId = Context.User?.RequireSessionId()
            ?? throw new InvalidOperationException("Session is required.");

        return ExecuteHubActionAsync(
            "AnswerBrowserCall",
            () => _webRtcCallManager.AnswerCallAsync(sessionId, callId, Context.ConnectionAborted));
    }

    public Task RejectBrowserCall(string callId)
    {
        var sessionId = Context.User?.RequireSessionId()
            ?? throw new InvalidOperationException("Session is required.");

        return ExecuteHubActionAsync(
            "RejectBrowserCall",
            () => _webRtcCallManager.RejectCallAsync(sessionId, callId, Context.ConnectionAborted));
    }

    public Task EndBrowserCall(string callId)
    {
        var sessionId = Context.User?.RequireSessionId()
            ?? throw new InvalidOperationException("Session is required.");

        return ExecuteHubActionAsync(
            "EndBrowserCall",
            () => _webRtcCallManager.EndCallAsync(sessionId, callId, Context.ConnectionAborted));
    }

    public Task SendWebRtcSignal(WebRtcSignalRequest request)
    {
        var sessionId = Context.User?.RequireSessionId()
            ?? throw new InvalidOperationException("Session is required.");

        return ExecuteHubActionAsync(
            "SendWebRtcSignal",
            () => _webRtcCallManager.ForwardWebRtcSignalAsync(sessionId, request, Context.ConnectionAborted));
    }

    public Task MarkCallConnected(string callId)
    {
        var sessionId = Context.User?.RequireSessionId()
            ?? throw new InvalidOperationException("Session is required.");

        return ExecuteHubActionAsync(
            "MarkCallConnected",
            () => _webRtcCallManager.MarkCallConnectedAsync(sessionId, callId, Context.ConnectionAborted));
    }

    private async Task ExecuteHubActionAsync(string methodName, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (AppException ex)
        {
            throw new HubException(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Hub method {MethodName} failed. ConnectionId={ConnectionId}", methodName, Context.ConnectionId);
            throw;
        }
    }

    private async Task<T> ExecuteHubActionAsync<T>(string methodName, Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (AppException ex)
        {
            throw new HubException(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Hub method {MethodName} failed. ConnectionId={ConnectionId}", methodName, Context.ConnectionId);
            throw;
        }
    }
}

public interface ISoftphoneHubClient
{
    Task SessionSnapshot(CallControl.Api.Domain.SessionSnapshotResponse snapshot);
    Task SoftphoneEvent(CallControl.Api.Domain.SoftphoneEventEnvelope envelope);
    Task BrowserCallsSnapshot(IReadOnlyList<BrowserCallView> calls);
    Task BrowserCallUpdated(BrowserCallView call);
    Task WebRtcSignal(WebRtcSignalMessage signal);
}

public static class HubGroupName
{
    public static string ForSession(string sessionId) => $"session:{sessionId}";
}
