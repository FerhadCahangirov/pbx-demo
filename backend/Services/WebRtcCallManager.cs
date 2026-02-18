using CallControl.Api.Domain;
using CallControl.Api.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace CallControl.Api.Services;

public sealed class WebRtcCallManager
{
    private const string StatusRinging = "Ringing";
    private const string StatusConnecting = "Connecting";
    private const string StatusConnected = "Connected";
    private const string StatusEnded = "Ended";

    private static readonly HashSet<string> AllowedSignalTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "offer",
        "answer",
        "ice"
    };

    private readonly SessionRegistry _sessionRegistry;
    private readonly SessionPresenceRegistry _presenceRegistry;
    private readonly IHubContext<SoftphoneHub, ISoftphoneHubClient> _hubContext;
    private readonly ILogger<WebRtcCallManager> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, BrowserCallRecord> _callsById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _callIdsBySessionId = new(StringComparer.Ordinal);

    public WebRtcCallManager(
        SessionRegistry sessionRegistry,
        SessionPresenceRegistry presenceRegistry,
        IHubContext<SoftphoneHub, ISoftphoneHubClient> hubContext,
        ILogger<WebRtcCallManager> logger)
    {
        _sessionRegistry = sessionRegistry;
        _presenceRegistry = presenceRegistry;
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task<IReadOnlyList<BrowserCallView>> GetCallsForSessionAsync(string sessionId, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!_callIdsBySessionId.TryGetValue(sessionId, out var callIds))
            {
                return [];
            }

            var calls = callIds
                .Select(callId =>
                {
                    return _callsById.TryGetValue(callId, out var call) ? ToView(call, sessionId) : null;
                })
                .Where(view => view is not null)
                .Cast<BrowserCallView>()
                .OrderByDescending(view => view.CreatedAtUtc)
                .ToList();

            return calls;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<BrowserCallView> PlaceCallAsync(string callerSessionId, string destinationExtension, CancellationToken cancellationToken)
    {
        var normalizedDestination = destinationExtension?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedDestination))
        {
            throw new BadRequestException("Destination extension is required.");
        }

        if (!_sessionRegistry.TryGet(callerSessionId, out var callerSession))
        {
            throw new UnauthorizedException("Session is not found or expired.");
        }

        var callerExtension = callerSession.OwnedExtensionDn;
        if (string.Equals(callerExtension, normalizedDestination, StringComparison.Ordinal))
        {
            throw new BadRequestException("Calling the same extension is not allowed.");
        }

        var calleeSession = FindOnlineSessionByExtension(normalizedDestination)
            ?? throw new NotFoundException($"Destination extension '{normalizedDestination}' is not connected in browser softphone.");

        BrowserCallRecord call;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (HasActiveCallLocked(callerSessionId))
            {
                throw new BadRequestException("The caller already has an active call.");
            }

            if (HasActiveCallLocked(calleeSession.SessionId))
            {
                throw new BadRequestException($"Extension '{normalizedDestination}' already has an active call.");
            }

            call = new BrowserCallRecord
            {
                CallId = Guid.NewGuid().ToString("N"),
                CallerSessionId = callerSessionId,
                CallerExtension = callerExtension,
                CallerUsername = callerSession.Username,
                CalleeSessionId = calleeSession.SessionId,
                CalleeExtension = calleeSession.OwnedExtensionDn,
                CalleeUsername = calleeSession.Username,
                Status = StatusRinging,
                CreatedAtUtc = DateTimeOffset.UtcNow
            };

            _callsById[call.CallId] = call;
            AddCallIndexLocked(callerSessionId, call.CallId);
            AddCallIndexLocked(calleeSession.SessionId, call.CallId);
        }
        finally
        {
            _gate.Release();
        }

        await PublishCallUpdateAsync(call);
        return ToView(call, callerSessionId);
    }

    public async Task AnswerCallAsync(string sessionId, string callId, CancellationToken cancellationToken)
    {
        BrowserCallRecord call;
        var changed = false;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            call = GetCallForParticipantLocked(sessionId, callId);
            if (!string.Equals(call.CalleeSessionId, sessionId, StringComparison.Ordinal))
            {
                throw new ForbiddenException("Only the called side can answer this call.");
            }

            if (string.Equals(call.Status, StatusEnded, StringComparison.Ordinal))
            {
                return;
            }

            if (!string.Equals(call.Status, StatusRinging, StringComparison.Ordinal))
            {
                throw new BadRequestException("Only ringing calls can be answered.");
            }

            call.Status = StatusConnecting;
            call.AnsweredAtUtc ??= DateTimeOffset.UtcNow;
            changed = true;
        }
        finally
        {
            _gate.Release();
        }

        if (changed)
        {
            await PublishCallUpdateAsync(call);
        }
    }

    public async Task RejectCallAsync(string sessionId, string callId, CancellationToken cancellationToken)
    {
        BrowserCallRecord? completedCall = null;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var call = GetCallForParticipantLocked(sessionId, callId);
            if (!string.Equals(call.Status, StatusRinging, StringComparison.Ordinal))
            {
                throw new BadRequestException("Only ringing calls can be rejected.");
            }

            var reason = string.Equals(sessionId, call.CalleeSessionId, StringComparison.Ordinal)
                ? "rejected"
                : "canceled";

            completedCall = EndCallLocked(call, reason);
        }
        finally
        {
            _gate.Release();
        }

        if (completedCall is not null)
        {
            await PublishCallUpdateAsync(completedCall);
        }
    }

    public async Task EndCallAsync(string sessionId, string callId, CancellationToken cancellationToken)
    {
        BrowserCallRecord? completedCall = null;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var call = GetCallForParticipantLocked(sessionId, callId);
            if (string.Equals(call.Status, StatusEnded, StringComparison.Ordinal))
            {
                return;
            }

            completedCall = EndCallLocked(call, "ended");
        }
        finally
        {
            _gate.Release();
        }

        if (completedCall is not null)
        {
            await PublishCallUpdateAsync(completedCall);
        }
    }

    public async Task MarkCallConnectedAsync(string sessionId, string callId, CancellationToken cancellationToken)
    {
        BrowserCallRecord call;
        var changed = false;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            call = GetCallForParticipantLocked(sessionId, callId);
            if (string.Equals(call.Status, StatusEnded, StringComparison.Ordinal))
            {
                return;
            }

            if (string.Equals(call.CallerSessionId, sessionId, StringComparison.Ordinal))
            {
                call.CallerMediaConnected = true;
            }
            else if (string.Equals(call.CalleeSessionId, sessionId, StringComparison.Ordinal))
            {
                call.CalleeMediaConnected = true;
            }

            if (call.CallerMediaConnected && call.CalleeMediaConnected
                && !string.Equals(call.Status, StatusConnected, StringComparison.Ordinal))
            {
                call.Status = StatusConnected;
                call.AnsweredAtUtc ??= DateTimeOffset.UtcNow;
                changed = true;
            }
        }
        finally
        {
            _gate.Release();
        }

        if (changed)
        {
            await PublishCallUpdateAsync(call);
        }
    }

    public async Task ForwardWebRtcSignalAsync(string sessionId, WebRtcSignalRequest request, CancellationToken cancellationToken)
    {
        var normalizedCallId = request.CallId?.Trim() ?? string.Empty;
        var normalizedType = request.Type?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(normalizedCallId))
        {
            throw new BadRequestException("Call id is required for signaling.");
        }

        if (string.IsNullOrWhiteSpace(normalizedType) || !AllowedSignalTypes.Contains(normalizedType))
        {
            throw new BadRequestException("Signal type must be one of: offer, answer, ice.");
        }

        BrowserCallRecord call;
        string targetSessionId;
        string fromExtension;
        string toExtension;
        var publishCallUpdate = false;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            call = GetCallForParticipantLocked(sessionId, normalizedCallId);
            if (string.Equals(call.Status, StatusEnded, StringComparison.Ordinal))
            {
                throw new BadRequestException("Cannot send signaling for a finished call.");
            }

            if (string.Equals(call.CallerSessionId, sessionId, StringComparison.Ordinal))
            {
                targetSessionId = call.CalleeSessionId;
                fromExtension = call.CallerExtension;
                toExtension = call.CalleeExtension;
            }
            else
            {
                targetSessionId = call.CallerSessionId;
                fromExtension = call.CalleeExtension;
                toExtension = call.CallerExtension;
            }

            if (string.Equals(normalizedType, "answer", StringComparison.OrdinalIgnoreCase)
                && string.Equals(call.Status, StatusRinging, StringComparison.Ordinal))
            {
                call.Status = StatusConnecting;
                call.AnsweredAtUtc ??= DateTimeOffset.UtcNow;
                publishCallUpdate = true;
            }
        }
        finally
        {
            _gate.Release();
        }

        if (publishCallUpdate)
        {
            await PublishCallUpdateAsync(call);
        }

        var signal = new WebRtcSignalMessage
        {
            CallId = call.CallId,
            Type = normalizedType.ToLowerInvariant(),
            Sdp = request.Sdp,
            Candidate = request.Candidate,
            SdpMid = request.SdpMid,
            SdpMLineIndex = request.SdpMLineIndex,
            FromExtension = fromExtension,
            ToExtension = toExtension,
            SentAtUtc = DateTimeOffset.UtcNow
        };

        await _hubContext.Clients.Group(HubGroupName.ForSession(targetSessionId)).WebRtcSignal(signal);
    }

    public async Task HandleSessionDisconnectedAsync(string sessionId)
    {
        List<BrowserCallRecord> endedCalls;

        await _gate.WaitAsync();
        try
        {
            if (!_callIdsBySessionId.TryGetValue(sessionId, out var callIds) || callIds.Count == 0)
            {
                return;
            }

            endedCalls = callIds
                .Select(callId =>
                {
                    if (!_callsById.TryGetValue(callId, out var call))
                    {
                        return null;
                    }

                    return EndCallLocked(call, "peer_disconnected");
                })
                .Where(call => call is not null)
                .Cast<BrowserCallRecord>()
                .ToList();
        }
        finally
        {
            _gate.Release();
        }

        foreach (var call in endedCalls)
        {
            try
            {
                await PublishCallUpdateAsync(call);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to publish disconnected call update. CallId={CallId}", call.CallId);
            }
        }
    }

    private SoftphoneSession? FindOnlineSessionByExtension(string extensionDn)
    {
        foreach (var session in _sessionRegistry.List())
        {
            if (!string.Equals(session.OwnedExtensionDn, extensionDn, StringComparison.Ordinal))
            {
                continue;
            }

            if (!_presenceRegistry.IsSessionOnline(session.SessionId))
            {
                continue;
            }

            return session;
        }

        return null;
    }

    private BrowserCallRecord GetCallForParticipantLocked(string sessionId, string callId)
    {
        if (!_callsById.TryGetValue(callId, out var call))
        {
            throw new NotFoundException($"Call '{callId}' was not found.");
        }

        if (!string.Equals(call.CallerSessionId, sessionId, StringComparison.Ordinal)
            && !string.Equals(call.CalleeSessionId, sessionId, StringComparison.Ordinal))
        {
            throw new ForbiddenException("This call does not belong to the current session.");
        }

        return call;
    }

    private bool HasActiveCallLocked(string sessionId)
    {
        if (!_callIdsBySessionId.TryGetValue(sessionId, out var callIds))
        {
            return false;
        }

        foreach (var callId in callIds)
        {
            if (_callsById.TryGetValue(callId, out var call) && IsActiveStatus(call.Status))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsActiveStatus(string status)
    {
        return string.Equals(status, StatusRinging, StringComparison.Ordinal)
            || string.Equals(status, StatusConnecting, StringComparison.Ordinal)
            || string.Equals(status, StatusConnected, StringComparison.Ordinal);
    }

    private void AddCallIndexLocked(string sessionId, string callId)
    {
        if (!_callIdsBySessionId.TryGetValue(sessionId, out var sessionCalls))
        {
            sessionCalls = new HashSet<string>(StringComparer.Ordinal);
            _callIdsBySessionId[sessionId] = sessionCalls;
        }

        sessionCalls.Add(callId);
    }

    private BrowserCallRecord EndCallLocked(BrowserCallRecord call, string reason)
    {
        if (!string.Equals(call.Status, StatusEnded, StringComparison.Ordinal))
        {
            call.Status = StatusEnded;
            call.EndReason = reason;
            call.EndedAtUtc = DateTimeOffset.UtcNow;
        }

        _callsById.Remove(call.CallId);
        RemoveCallIndexLocked(call.CallerSessionId, call.CallId);
        RemoveCallIndexLocked(call.CalleeSessionId, call.CallId);
        return call;
    }

    private void RemoveCallIndexLocked(string sessionId, string callId)
    {
        if (!_callIdsBySessionId.TryGetValue(sessionId, out var sessionCalls))
        {
            return;
        }

        sessionCalls.Remove(callId);
        if (sessionCalls.Count == 0)
        {
            _callIdsBySessionId.Remove(sessionId);
        }
    }

    private Task PublishCallUpdateAsync(BrowserCallRecord call)
    {
        var callerView = ToView(call, call.CallerSessionId);
        var calleeView = ToView(call, call.CalleeSessionId);

        return Task.WhenAll(
            _hubContext.Clients.Group(HubGroupName.ForSession(call.CallerSessionId)).BrowserCallUpdated(callerView),
            _hubContext.Clients.Group(HubGroupName.ForSession(call.CalleeSessionId)).BrowserCallUpdated(calleeView));
    }

    private static BrowserCallView ToView(BrowserCallRecord call, string sessionId)
    {
        var isIncoming = string.Equals(call.CalleeSessionId, sessionId, StringComparison.Ordinal);

        return new BrowserCallView
        {
            CallId = call.CallId,
            Status = call.Status,
            LocalExtension = isIncoming ? call.CalleeExtension : call.CallerExtension,
            RemoteExtension = isIncoming ? call.CallerExtension : call.CalleeExtension,
            RemoteUsername = isIncoming ? call.CallerUsername : call.CalleeUsername,
            IsIncoming = isIncoming,
            CreatedAtUtc = call.CreatedAtUtc,
            AnsweredAtUtc = call.AnsweredAtUtc,
            EndedAtUtc = call.EndedAtUtc,
            EndReason = call.EndReason
        };
    }

    private sealed class BrowserCallRecord
    {
        public required string CallId { get; init; }
        public required string CallerSessionId { get; init; }
        public required string CallerExtension { get; init; }
        public required string CallerUsername { get; init; }
        public required string CalleeSessionId { get; init; }
        public required string CalleeExtension { get; init; }
        public required string CalleeUsername { get; init; }
        public required string Status { get; set; }
        public required DateTimeOffset CreatedAtUtc { get; init; }
        public DateTimeOffset? AnsweredAtUtc { get; set; }
        public DateTimeOffset? EndedAtUtc { get; set; }
        public string? EndReason { get; set; }
        public bool CallerMediaConnected { get; set; }
        public bool CalleeMediaConnected { get; set; }
    }
}
