using System.Net;
using System.Text.Json;
using CallControl.Api.Domain;
using CallControl.Api.Infrastructure;

namespace CallControl.Api.Services;

public sealed class CallManager
{
    private readonly SessionRegistry _sessionRegistry;
    private readonly ThreeCxClientFactory _threeCxClientFactory;
    private readonly EventDispatcher _eventDispatcher;
    private readonly ILogger<CallManager> _logger;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public CallManager(
        SessionRegistry sessionRegistry,
        ThreeCxClientFactory threeCxClientFactory,
        EventDispatcher eventDispatcher,
        ILogger<CallManager> logger)
    {
        _sessionRegistry = sessionRegistry;
        _threeCxClientFactory = threeCxClientFactory;
        _eventDispatcher = eventDispatcher;
        _logger = logger;
    }

    public async Task CreateSessionAsync(
        string sessionId,
        string username,
        string ownedExtensionDn,
        ThreeCxConnectSettings settings,
        CancellationToken cancellationToken)
    {
        var normalizedOwnedExtension = ownedExtensionDn?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedOwnedExtension))
        {
            throw new ForbiddenException("The user does not own any extension.");
        }

        var client = _threeCxClientFactory.Create(settings);
        try
        {
            await client.InitializeAsync(cancellationToken);
            var fullInfo = await client.GetFullInfoAsync(cancellationToken);
            var topology = CallControlMapFactory.ToMap(fullInfo);

            if (!topology.TryGetValue(normalizedOwnedExtension, out var ownedDnInfo)
                || !string.Equals(ownedDnInfo.Type, CallControlConstants.ExtensionType, StringComparison.Ordinal))
            {
                throw new ForbiddenException($"Owned extension '{normalizedOwnedExtension}' is not available in 3CX for this user.");
            }

            var session = new SoftphoneSession
            {
                SessionId = sessionId,
                Username = username,
                OwnedExtensionDn = normalizedOwnedExtension,
                ConnectionSettings = settings,
                ThreeCxClient = client,
                WsConnected = client.IsWebSocketConnected
            };

            foreach (var kv in topology)
            {
                session.TopologyByDn[kv.Key] = kv.Value;
            }

            _sessionRegistry.Add(session);

            client.WsEventReceived += wsEvent => HandleWsEventAsync(sessionId, wsEvent);
            client.WsConnectionStateChanged += connected => HandleWsConnectionStateChangedAsync(sessionId, connected);

            SessionSnapshotResponse snapshot;
            await session.Gate.WaitAsync(cancellationToken);
            try
            {
                session.SelectedExtensionDn = normalizedOwnedExtension;
                RebuildSelectedExtensionStateLocked(session);
                session.LastUpdatedUtc = DateTimeOffset.UtcNow;
                snapshot = BuildSnapshotLocked(session);
            }
            finally
            {
                session.Gate.Release();
            }

            await _eventDispatcher.PublishSnapshotAsync(sessionId, snapshot);
        }
        catch
        {
            await client.DisposeAsync();
            throw;
        }
    }

    public async Task DisconnectSessionAsync(string sessionId)
    {
        await _sessionRegistry.RemoveAsync(sessionId);
    }

    public async Task<SessionSnapshotResponse> GetSnapshotAsync(string sessionId, CancellationToken cancellationToken)
    {
        var session = GetSessionOrThrow(sessionId);

        await session.Gate.WaitAsync(cancellationToken);
        try
        {
            return BuildSnapshotLocked(session);
        }
        finally
        {
            session.Gate.Release();
        }
    }

    public async Task SelectExtensionAsync(string sessionId, string extensionDn, CancellationToken cancellationToken)
    {
        var session = GetSessionOrThrow(sessionId);
        SessionSnapshotResponse snapshot;

        await session.Gate.WaitAsync(cancellationToken);
        try
        {
            var normalizedDn = string.IsNullOrWhiteSpace(extensionDn)
                ? session.OwnedExtensionDn
                : extensionDn.Trim();

            if (!string.Equals(session.OwnedExtensionDn, normalizedDn, StringComparison.Ordinal))
            {
                throw new ForbiddenException("Each user can bind only their configured owned extension.");
            }

            if (!session.TopologyByDn.TryGetValue(normalizedDn, out var dnInfo)
                || !string.Equals(dnInfo.Type, CallControlConstants.ExtensionType, StringComparison.Ordinal))
            {
                throw new NotFoundException("The selected extension is not available in 3CX.");
            }

            session.SelectedExtensionDn = normalizedDn;
            RebuildSelectedExtensionStateLocked(session);
            session.LastUpdatedUtc = DateTimeOffset.UtcNow;
            snapshot = BuildSnapshotLocked(session);
        }
        finally
        {
            session.Gate.Release();
        }

        await _eventDispatcher.PublishSnapshotAsync(sessionId, snapshot);
        await _eventDispatcher.PublishEventAsync(sessionId, new SoftphoneEventEnvelope
        {
            EventType = "extension.selected",
            Payload = new { extensionDn }
        });
    }

    public async Task SetActiveDeviceAsync(string sessionId, string deviceId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            throw new BadRequestException("Device id is required.");
        }

        var session = GetSessionOrThrow(sessionId);
        SessionSnapshotResponse snapshot;
        var normalizedDeviceId = deviceId.Trim();

        await session.Gate.WaitAsync(cancellationToken);
        try
        {
            if (string.IsNullOrWhiteSpace(session.SelectedExtensionDn))
            {
                throw new BadRequestException("Select an extension first.");
            }

            if (!session.Devices.ContainsKey(normalizedDeviceId))
            {
                throw new NotFoundException("Device is not available for the selected extension.");
            }

            session.ActiveDeviceId = normalizedDeviceId;
            session.LastUpdatedUtc = DateTimeOffset.UtcNow;
            snapshot = BuildSnapshotLocked(session);
        }
        finally
        {
            session.Gate.Release();
        }

        await _eventDispatcher.PublishSnapshotAsync(sessionId, snapshot);
    }

    public async Task MakeOutgoingCallAsync(string sessionId, string destination, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(destination))
        {
            throw new BadRequestException("Destination is required.");
        }

        var session = GetSessionOrThrow(sessionId);

        string sourceDn;
        string activeDeviceId;

        await session.Gate.WaitAsync(cancellationToken);
        try
        {
            sourceDn = session.SelectedExtensionDn ?? throw new BadRequestException("Select an extension first.");
            activeDeviceId = session.ActiveDeviceId ?? throw new BadRequestException("Select an active device.");
        }
        finally
        {
            session.Gate.Release();
        }

        var normalizedDestination = destination.Trim();
        if (string.Equals(activeDeviceId, CallControlConstants.UnregisteredDeviceId, StringComparison.Ordinal))
        {
            try
            {
                await session.ThreeCxClient.MakeCallAsync(sourceDn, normalizedDestination, cancellationToken);
            }
            catch (UpstreamApiException ex) when (ex.ErrorCode == (int)HttpStatusCode.UnprocessableEntity)
            {
                throw new BadRequestException(
                    "The PBX rejected server-route calling for this extension. Select a registered 3CX device to place the call.");
            }
        }
        else
        {
            try
            {
                await session.ThreeCxClient.MakeCallFromDeviceAsync(
                    sourceDn,
                    Uri.EscapeDataString(activeDeviceId),
                    normalizedDestination,
                    cancellationToken);
            }
            catch (UpstreamApiException ex) when (ex.ErrorCode == (int)HttpStatusCode.UnprocessableEntity)
            {
                await session.ThreeCxClient.MakeCallAsync(sourceDn, normalizedDestination, cancellationToken);
            }
        }
    }

    public Task AnswerCallAsync(string sessionId, long participantId, CancellationToken cancellationToken)
    {
        return ControlParticipantAsync(sessionId, participantId, CallControlConstants.ParticipantActionAnswer, null, cancellationToken);
    }

    public Task RejectCallAsync(string sessionId, long participantId, CancellationToken cancellationToken)
    {
        return ControlParticipantAsync(sessionId, participantId, CallControlConstants.ParticipantActionDrop, null, cancellationToken);
    }

    public Task EndCallAsync(string sessionId, long participantId, CancellationToken cancellationToken)
    {
        return ControlParticipantAsync(sessionId, participantId, CallControlConstants.ParticipantActionDrop, null, cancellationToken);
    }

    public Task TransferCallAsync(string sessionId, long participantId, string destination, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(destination))
        {
            throw new BadRequestException("Transfer destination is required.");
        }

        return ControlParticipantAsync(
            sessionId,
            participantId,
            CallControlConstants.ParticipantActionTransferTo,
            destination.Trim(),
            cancellationToken);
    }

    private async Task ControlParticipantAsync(
        string sessionId,
        long participantId,
        string action,
        string? destination,
        CancellationToken cancellationToken)
    {
        if (participantId <= 0)
        {
            throw new BadRequestException("Participant id must be greater than 0.");
        }

        var session = GetSessionOrThrow(sessionId);
        string sourceDn;

        await session.Gate.WaitAsync(cancellationToken);
        try
        {
            sourceDn = session.SelectedExtensionDn ?? throw new BadRequestException("Select an extension first.");
        }
        finally
        {
            session.Gate.Release();
        }

        await session.ThreeCxClient.ControlParticipantAsync(sourceDn, participantId, action, destination, cancellationToken);
    }

    private async Task HandleWsConnectionStateChangedAsync(string sessionId, bool connected)
    {
        if (!_sessionRegistry.TryGet(sessionId, out var session))
        {
            return;
        }

        SessionSnapshotResponse snapshot;
        var gateAcquired = false;
        try
        {
            await session.Gate.WaitAsync();
            gateAcquired = true;
            session.WsConnected = connected;
            session.LastUpdatedUtc = DateTimeOffset.UtcNow;
            snapshot = BuildSnapshotLocked(session);
        }
        catch (ObjectDisposedException)
        {
            return;
        }
        finally
        {
            if (gateAcquired)
            {
                session.Gate.Release();
            }
        }

        await _eventDispatcher.PublishEventAsync(sessionId, new SoftphoneEventEnvelope
        {
            EventType = connected ? "ws.connected" : "ws.disconnected",
            Payload = new { connected }
        });

        await _eventDispatcher.PublishSnapshotAsync(sessionId, snapshot);
    }

    private async Task HandleWsEventAsync(string sessionId, ThreeCxWsEvent wsEvent)
    {
        if (wsEvent.Event is null || string.IsNullOrWhiteSpace(wsEvent.Event.Entity))
        {
            return;
        }

        if (!_sessionRegistry.TryGet(sessionId, out var session))
        {
            return;
        }

        var op = EntityPathHelper.DetermineOperation(wsEvent.Event.Entity);
        if (string.IsNullOrWhiteSpace(op.Dn)
            || string.IsNullOrWhiteSpace(op.Type)
            || string.IsNullOrWhiteSpace(op.Id))
        {
            return;
        }

        try
        {
            if (wsEvent.Event.EventType == ThreeCxEventType.Upset)
            {
                var payload = await session.ThreeCxClient.RequestEntityAsync(wsEvent.Event.Entity);
                if (payload is null)
                {
                    return;
                }

                await ProcessUpsertAsync(sessionId, session, op, payload.Value);
                return;
            }

            if (wsEvent.Event.EventType == ThreeCxEventType.Remove)
            {
                await ProcessRemoveAsync(sessionId, session, op);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to process 3CX websocket event for session {SessionId}. Entity={Entity}", sessionId, wsEvent.Event.Entity);
        }
    }

    private async Task ProcessUpsertAsync(string sessionId, SoftphoneSession session, EntityOperation op, JsonElement payload)
    {
        SessionSnapshotResponse snapshot;
        SoftphoneEventEnvelope? envelope = null;

        await session.Gate.WaitAsync();
        try
        {
            if (!session.TopologyByDn.TryGetValue(op.Dn, out var dnInfo))
            {
                dnInfo = new ThreeCxDnInfoModel
                {
                    Dn = op.Dn
                };
                session.TopologyByDn[op.Dn] = dnInfo;
            }

            if (string.Equals(op.Type, CallControlConstants.ParticipantEntity, StringComparison.Ordinal))
            {
                var participant = payload.Deserialize<ThreeCxParticipant>(_jsonOptions);
                if (participant?.Id is long participantId)
                {
                    dnInfo.Participants[participantId] = participant;
                    var callView = ApplyParticipantUpsertLocked(session, op.Dn, participant);
                    envelope = new SoftphoneEventEnvelope
                    {
                        EventType = MapCallEventType(participant.Status),
                        Payload = new
                        {
                            call = callView,
                            sourceExtension = op.Dn
                        }
                    };
                }
            }
            else if (string.Equals(op.Type, CallControlConstants.DeviceEntity, StringComparison.Ordinal))
            {
                var device = payload.Deserialize<ThreeCxDevice>(_jsonOptions);
                if (!string.IsNullOrWhiteSpace(device?.DeviceId))
                {
                    dnInfo.Devices[device.DeviceId] = device;
                    if (string.Equals(session.SelectedExtensionDn, op.Dn, StringComparison.Ordinal))
                    {
                        RebuildSelectedExtensionStateLocked(session);
                    }

                    envelope = new SoftphoneEventEnvelope
                    {
                        EventType = "device.updated",
                        Payload = new { sourceExtension = op.Dn, deviceId = device.DeviceId }
                    };
                }
            }

            session.LastUpdatedUtc = DateTimeOffset.UtcNow;
            snapshot = BuildSnapshotLocked(session);
        }
        finally
        {
            session.Gate.Release();
        }

        if (envelope is not null)
        {
            await _eventDispatcher.PublishEventAsync(sessionId, envelope);
        }

        await _eventDispatcher.PublishSnapshotAsync(sessionId, snapshot);
    }

    private async Task ProcessRemoveAsync(string sessionId, SoftphoneSession session, EntityOperation op)
    {
        SessionSnapshotResponse snapshot;
        SoftphoneEventEnvelope? envelope = null;

        await session.Gate.WaitAsync();
        try
        {
            if (!session.TopologyByDn.TryGetValue(op.Dn, out var dnInfo))
            {
                return;
            }

            if (string.Equals(op.Type, CallControlConstants.ParticipantEntity, StringComparison.Ordinal)
                && long.TryParse(op.Id, out var participantId))
            {
                dnInfo.Participants.Remove(participantId);
                var removed = RemoveParticipantLocked(session, op.Dn, participantId);
                if (removed is not null)
                {
                    envelope = new SoftphoneEventEnvelope
                    {
                        EventType = "call.ended",
                        Payload = new { call = removed, sourceExtension = op.Dn }
                    };
                }
            }
            else if (string.Equals(op.Type, CallControlConstants.DeviceEntity, StringComparison.Ordinal))
            {
                dnInfo.Devices.Remove(op.Id);

                if (string.Equals(session.SelectedExtensionDn, op.Dn, StringComparison.Ordinal))
                {
                    RebuildSelectedExtensionStateLocked(session);
                }

                envelope = new SoftphoneEventEnvelope
                {
                    EventType = "device.removed",
                    Payload = new { sourceExtension = op.Dn, deviceId = op.Id }
                };
            }

            session.LastUpdatedUtc = DateTimeOffset.UtcNow;
            snapshot = BuildSnapshotLocked(session);
        }
        finally
        {
            session.Gate.Release();
        }

        if (envelope is not null)
        {
            await _eventDispatcher.PublishEventAsync(sessionId, envelope);
        }

        await _eventDispatcher.PublishSnapshotAsync(sessionId, snapshot);
    }

    private static string MapCallEventType(string? status)
    {
        if (string.Equals(status, CallControlConstants.ParticipantStatusRinging, StringComparison.OrdinalIgnoreCase))
        {
            return "call.ringing";
        }

        if (string.Equals(status, CallControlConstants.ParticipantStatusConnected, StringComparison.OrdinalIgnoreCase))
        {
            return "call.connected";
        }

        if (string.Equals(status, CallControlConstants.ParticipantStatusDialing, StringComparison.OrdinalIgnoreCase))
        {
            return "call.dialing";
        }

        return "call.updated";
    }

    private SoftphoneCallView? ApplyParticipantUpsertLocked(SoftphoneSession session, string dn, ThreeCxParticipant participant)
    {
        if (participant.Id is not long participantId)
        {
            return null;
        }

        if (!string.Equals(session.SelectedExtensionDn, dn, StringComparison.Ordinal))
        {
            return null;
        }

        var direction = InferDirection(participant, session.DirectionByParticipant.TryGetValue(participantId, out var existingDirection)
            ? existingDirection
            : SoftphoneCallDirection.Outgoing);

        session.Participants[participantId] = participant;
        session.DirectionByParticipant[participantId] = direction;

        if (string.Equals(participant.Status, CallControlConstants.ParticipantStatusConnected, StringComparison.OrdinalIgnoreCase))
        {
            session.ConnectedAtByParticipant.TryAdd(participantId, DateTimeOffset.UtcNow);
        }
        else if (!string.Equals(participant.Status, CallControlConstants.ParticipantStatusRinging, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(participant.Status, CallControlConstants.ParticipantStatusDialing, StringComparison.OrdinalIgnoreCase))
        {
            session.ConnectedAtByParticipant.TryRemove(participantId, out _);
        }

        return ToCallView(session, participant, direction);
    }

    private SoftphoneCallView? RemoveParticipantLocked(SoftphoneSession session, string dn, long participantId)
    {
        if (!string.Equals(session.SelectedExtensionDn, dn, StringComparison.Ordinal))
        {
            return null;
        }

        if (!session.Participants.TryRemove(participantId, out var participant))
        {
            return null;
        }

        session.ConnectedAtByParticipant.TryRemove(participantId, out _);
        if (!session.DirectionByParticipant.TryRemove(participantId, out var direction))
        {
            direction = SoftphoneCallDirection.Outgoing;
        }

        return ToCallView(session, participant, direction);
    }

    private void RebuildSelectedExtensionStateLocked(SoftphoneSession session)
    {
        var previousActiveDeviceId = session.ActiveDeviceId;
        session.Devices.Clear();
        session.Participants.Clear();
        session.DirectionByParticipant.Clear();

        var selectedDn = session.SelectedExtensionDn;
        if (string.IsNullOrWhiteSpace(selectedDn))
        {
            session.ActiveDeviceId = null;
            session.ConnectedAtByParticipant.Clear();
            return;
        }

        if (!session.TopologyByDn.TryGetValue(selectedDn, out var dnInfo)
            || !string.Equals(dnInfo.Type, CallControlConstants.ExtensionType, StringComparison.Ordinal))
        {
            session.SelectedExtensionDn = null;
            session.ActiveDeviceId = null;
            session.ConnectedAtByParticipant.Clear();
            return;
        }

        foreach (var device in dnInfo.Devices.Values)
        {
            if (!string.IsNullOrWhiteSpace(device.DeviceId))
            {
                session.Devices[device.DeviceId] = device;
            }
        }

        session.Devices[CallControlConstants.UnregisteredDeviceId] = new ThreeCxDevice
        {
            Dn = selectedDn,
            DeviceId = CallControlConstants.UnregisteredDeviceId,
            UserAgent = "Web App / server route"
        };

        foreach (var participant in dnInfo.Participants.Values)
        {
            if (participant.Id is not long participantId)
            {
                continue;
            }

            var direction = InferDirection(participant, SoftphoneCallDirection.Outgoing);
            session.Participants[participantId] = participant;
            session.DirectionByParticipant[participantId] = direction;

            if (string.Equals(participant.Status, CallControlConstants.ParticipantStatusConnected, StringComparison.OrdinalIgnoreCase))
            {
                session.ConnectedAtByParticipant.TryAdd(participantId, DateTimeOffset.UtcNow);
            }
            else
            {
                session.ConnectedAtByParticipant.TryRemove(participantId, out _);
            }
        }

        if (!string.IsNullOrWhiteSpace(previousActiveDeviceId) && session.Devices.ContainsKey(previousActiveDeviceId))
        {
            session.ActiveDeviceId = previousActiveDeviceId;
        }
        else
        {
            session.ActiveDeviceId = CallControlConstants.UnregisteredDeviceId;
        }
    }

    private SessionSnapshotResponse BuildSnapshotLocked(SoftphoneSession session)
    {
        var devices = session.Devices.Values
            .OrderBy(v => v.UserAgent, StringComparer.OrdinalIgnoreCase)
            .ThenBy(v => v.DeviceId, StringComparer.OrdinalIgnoreCase)
            .Select(v => new SoftphoneDeviceView
            {
                Dn = v.Dn,
                DeviceId = v.DeviceId,
                UserAgent = v.UserAgent
            })
            .ToList();

        var calls = session.Participants.Values
            .Where(v => v.Id.HasValue)
            .Select(v =>
            {
                var participantId = v.Id!.Value;
                if (!session.DirectionByParticipant.TryGetValue(participantId, out var direction))
                {
                    direction = InferDirection(v, SoftphoneCallDirection.Outgoing);
                }

                return ToCallView(session, v, direction);
            })
            .Where(v => v is not null)
            .Cast<SoftphoneCallView>()
            .OrderBy(v => StatusSort(v.Status))
            .ThenBy(v => v.ParticipantId)
            .ToList();

        return new SessionSnapshotResponse
        {
            Connected = true,
            Username = session.Username,
            SessionId = session.SessionId,
            SelectedExtensionDn = session.SelectedExtensionDn,
            OwnedExtensionDn = session.OwnedExtensionDn,
            Devices = devices,
            ActiveDeviceId = session.ActiveDeviceId,
            Calls = calls,
            WsConnected = session.WsConnected,
            LastUpdatedUtc = session.LastUpdatedUtc
        };
    }

    private static int StatusSort(string? status)
    {
        if (string.Equals(status, CallControlConstants.ParticipantStatusRinging, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (string.Equals(status, CallControlConstants.ParticipantStatusDialing, StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        if (string.Equals(status, CallControlConstants.ParticipantStatusConnected, StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        return 3;
    }

    private static SoftphoneCallDirection InferDirection(ThreeCxParticipant participant, SoftphoneCallDirection fallback)
    {
        if (string.Equals(participant.Status, CallControlConstants.ParticipantStatusDialing, StringComparison.OrdinalIgnoreCase))
        {
            return SoftphoneCallDirection.Outgoing;
        }

        if (string.Equals(participant.Status, CallControlConstants.ParticipantStatusRinging, StringComparison.OrdinalIgnoreCase)
            && (participant.DirectControl ?? false))
        {
            return SoftphoneCallDirection.Incoming;
        }

        return fallback;
    }

    private static SoftphoneCallView ToCallView(
        SoftphoneSession session,
        ThreeCxParticipant participant,
        SoftphoneCallDirection direction)
    {
        var participantId = participant.Id ?? 0L;

        return new SoftphoneCallView
        {
            ParticipantId = participantId,
            CallId = participant.CallId,
            LegId = participant.LegId,
            Status = participant.Status,
            RemoteParty = participant.PartyCallerId,
            RemoteName = participant.PartyCallerName,
            Direction = direction,
            DirectControl = participant.DirectControl ?? false,
            ConnectedAtUtc = session.ConnectedAtByParticipant.TryGetValue(participantId, out var connectedAt)
                ? connectedAt
                : null
        };
    }

    private SoftphoneSession GetSessionOrThrow(string sessionId)
    {
        if (_sessionRegistry.TryGet(sessionId, out var session))
        {
            return session;
        }

        throw new UnauthorizedException("Session is not found or expired.");
    }
}
