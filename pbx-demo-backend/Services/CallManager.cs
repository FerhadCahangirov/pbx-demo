using System.Net;
using System.Text.Json;
using CallControl.Api.Domain;
using CallControl.Api.Infrastructure;
using CallControl.Api.Infrastructure.QueueManagement.Ingestion;
using Microsoft.Extensions.Options;

namespace CallControl.Api.Services;

public sealed class CallManager
{
    private readonly SoftphoneOptions _options;
    private readonly UserDirectoryService _userDirectoryService;
    private readonly SessionRegistry _sessionRegistry;
    private readonly ThreeCxClientFactory _threeCxClientFactory;
    private readonly EventDispatcher _eventDispatcher;
    private readonly CallCdrService _callCdrService;
    private readonly QueueThreeCxWebSocketIngestionBridge _queueWebSocketIngestionBridge;
    private readonly ILogger<CallManager> _logger;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public CallManager(
        IOptions<SoftphoneOptions> options,
        UserDirectoryService userDirectoryService,
        SessionRegistry sessionRegistry,
        ThreeCxClientFactory threeCxClientFactory,
        EventDispatcher eventDispatcher,
        CallCdrService callCdrService,
        QueueThreeCxWebSocketIngestionBridge queueWebSocketIngestionBridge,
        ILogger<CallManager> logger)
    {
        _options = options.Value;
        _userDirectoryService = userDirectoryService;
        _sessionRegistry = sessionRegistry;
        _threeCxClientFactory = threeCxClientFactory;
        _eventDispatcher = eventDispatcher;
        _callCdrService = callCdrService;
        _queueWebSocketIngestionBridge = queueWebSocketIngestionBridge;
        _logger = logger;
    }

    public async Task EnsureSessionAsync(string sessionId, string username, CancellationToken cancellationToken)
    {
        if (_sessionRegistry.TryGet(sessionId, out _))
        {
            return;
        }

        var user = await _userDirectoryService.FindByUsernameAsync(username, cancellationToken);
        if (user is null)
        {
            throw new UnauthorizedException("Session is not found or expired.");
        }

        var normalizedOwnedExtension = user.OwnedExtension?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedOwnedExtension))
        {
            throw new ForbiddenException("The user does not own any extension.");
        }

        var pbxBase = _options.ThreeCx.PbxBase?.Trim() ?? string.Empty;
        var appId = _options.ThreeCx.AppId?.Trim() ?? string.Empty;
        var appSecret = _options.ThreeCx.AppSecret?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(pbxBase)
            || string.IsNullOrWhiteSpace(appId)
            || string.IsNullOrWhiteSpace(appSecret))
        {
            throw new InternalServerErrorException("3CX app credentials are not configured on server.");
        }

        try
        {
            await CreateSessionAsync(
                sessionId,
                user.Id,
                user.Username,
                normalizedOwnedExtension,
                user.ControlDn,
                new ThreeCxConnectSettings
                {
                    PbxBase = pbxBase,
                    AppId = appId,
                    AppSecret = appSecret
                },
                cancellationToken);

            _logger.LogInformation("Recovered softphone session {SessionId} for user {Username}.", sessionId, username);
        }
        catch (InvalidOperationException)
        {
            // Session can be created concurrently by another request.
        }
    }

    public async Task CreateSessionAsync(
        string sessionId,
        int appUserId,
        string username,
        string ownedExtensionDn,
        string? configuredControlDn,
        ThreeCxConnectSettings settings,
        CancellationToken cancellationToken)
    {
        var normalizedOwnedExtension = ownedExtensionDn?.Trim() ?? string.Empty;
        var normalizedConfiguredControlDn = configuredControlDn?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedConfiguredControlDn))
        {
            normalizedConfiguredControlDn = null;
        }
        var normalizedAppId = settings.AppId?.Trim() ?? string.Empty;

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
                || !string.Equals(ownedDnInfo.Type, CallControlConstants.ExtensionType, StringComparison.OrdinalIgnoreCase))
            {
                throw new ForbiddenException($"Owned extension '{normalizedOwnedExtension}' is not available in 3CX for this user.");
            }

            if (!string.IsNullOrWhiteSpace(normalizedConfiguredControlDn)
                && !topology.ContainsKey(normalizedConfiguredControlDn))
            {
                throw new ForbiddenException(
                    $"Configured control DN '{normalizedConfiguredControlDn}' is not available for this API integration.");
            }

            var resolvedControlDn = normalizedConfiguredControlDn;
            if (string.IsNullOrWhiteSpace(resolvedControlDn)
                && !string.IsNullOrWhiteSpace(normalizedAppId)
                && topology.TryGetValue(normalizedAppId, out var appDnInfo)
                && string.Equals(appDnInfo.Type, CallControlConstants.RoutePointType, StringComparison.OrdinalIgnoreCase))
            {
                resolvedControlDn = normalizedAppId;
                _logger.LogInformation(
                    "Using AppId DN '{ControlDn}' as default control DN for user '{Username}'.",
                    resolvedControlDn,
                    username);
            }

            var session = new SoftphoneSession
            {
                SessionId = sessionId,
                AppUserId = appUserId,
                Username = username,
                OwnedExtensionDn = normalizedOwnedExtension,
                ControlDn = resolvedControlDn,
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

            foreach (var call in snapshot.Calls)
            {
                await PersistPbxCdrUpdateAsync(new PbxCallCdrUpdate
                {
                    OperatorUserId = session.AppUserId,
                    OperatorUsername = session.Username,
                    OperatorExtension = session.OwnedExtensionDn,
                    SourceDn = call.Dn ?? session.OwnedExtensionDn,
                    ParticipantId = call.ParticipantId,
                    PbxCallId = call.CallId,
                    PbxLegId = call.LegId,
                    Status = call.Status ?? string.Empty,
                    Direction = call.Direction,
                    RemoteParty = call.RemoteParty,
                    RemoteName = call.RemoteName,
                    ConnectedAtUtc = call.ConnectedAtUtc,
                    OccurredAtUtc = DateTimeOffset.UtcNow,
                    IsEnded = false,
                    EndReason = null,
                    EventType = "pbx.session.bootstrap"
                });
            }
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
                || !string.Equals(dnInfo.Type, CallControlConstants.ExtensionType, StringComparison.OrdinalIgnoreCase))
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
                    activeDeviceId,
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
        return ControlParticipantAsync(
            sessionId,
            participantId,
            CallControlConstants.ParticipantActionAnswer,
            null,
            cancellationToken,
            preferAnswerableParticipant: true);
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

    public async Task<(Stream Stream, string ContentType)> OpenParticipantAudioDownlinkAsync(
        string sessionId,
        long participantId,
        CancellationToken cancellationToken)
    {
        if (participantId <= 0)
        {
            throw new BadRequestException("Participant id must be greater than 0.");
        }

        var session = GetSessionOrThrow(sessionId);
        var sourceDn = await ResolveParticipantDnAsync(session, participantId, cancellationToken);
        return await session.ThreeCxClient.OpenParticipantAudioStreamAsync(sourceDn, participantId, cancellationToken);
    }

    public async Task SendParticipantAudioUplinkAsync(
        string sessionId,
        long participantId,
        Stream stream,
        CancellationToken cancellationToken)
    {
        if (participantId <= 0)
        {
            throw new BadRequestException("Participant id must be greater than 0.");
        }

        var session = GetSessionOrThrow(sessionId);
        var sourceDn = await ResolveParticipantDnAsync(session, participantId, cancellationToken);
        await session.ThreeCxClient.SendParticipantAudioStreamAsync(sourceDn, participantId, stream, cancellationToken);
    }

    private async Task ControlParticipantAsync(
        string sessionId,
        long participantId,
        string action,
        string? destination,
        CancellationToken cancellationToken,
        bool preferAnswerableParticipant = false)
    {
        if (participantId <= 0)
        {
            throw new BadRequestException("Participant id must be greater than 0.");
        }

        var session = GetSessionOrThrow(sessionId);
        if (preferAnswerableParticipant)
        {
            await RefreshTopologyBeforeAnswerAsync(sessionId, session, cancellationToken);
        }

        string sourceDn;
        long targetParticipantId = participantId;
        IReadOnlyList<ParticipantReference>? answerCandidates = null;

        await session.Gate.WaitAsync(cancellationToken);
        try
        {
            var selectedDn = session.SelectedExtensionDn ?? throw new BadRequestException("Select an extension first.");
            var controlDn = session.ControlDn;
            var requestedParticipant = ResolveRequestedParticipantLocked(
                session,
                participantId,
                selectedDn,
                controlDn,
                preferAnswerableParticipant);

            if (preferAnswerableParticipant)
            {
                if (requestedParticipant is null)
                {
                    throw new NotFoundException($"Participant {participantId} is no longer available.");
                }

                answerCandidates = ResolveAnswerCandidatesLocked(
                    session,
                    requestedParticipant,
                    selectedDn,
                    controlDn);
                sourceDn = requestedParticipant.Dn;
                targetParticipantId = requestedParticipant.ParticipantId;
            }
            else
            {
                sourceDn = requestedParticipant?.Dn ?? selectedDn;
            }
        }
        finally
        {
            session.Gate.Release();
        }

        if (preferAnswerableParticipant && answerCandidates is not null)
        {
            var ringingCandidates = answerCandidates
                .Where(candidate => IsRingingParticipant(candidate.Status))
                .ToList();

            var attemptCandidates = ringingCandidates.Any(IsAnswerableParticipant)
                ? ringingCandidates
                : answerCandidates.ToList();

            attemptCandidates = attemptCandidates
                .OrderByDescending(IsAnswerableParticipant)
                .ThenByDescending(candidate => IsRingingParticipant(candidate.Status))
                .ThenBy(candidate => candidate.ParticipantId)
                .ToList();

            if (attemptCandidates.Count == 0)
            {
                throw new BadRequestException(
                    $"Participant {participantId} is no longer available for answer.");
            }

            if (!attemptCandidates.Any(IsAnswerableParticipant))
            {
                var nonAnswerableSummary = string.Join(", ", attemptCandidates.Select(candidate =>
                    $"{candidate.ParticipantId}@{candidate.Dn}(call={candidate.CallId?.ToString() ?? "?"},dc={candidate.DirectControl},type={candidate.DnType ?? "?"},status={candidate.Status ?? "?"})"));
                _logger.LogWarning(
                    "No answerable candidate found by metadata, but trying best-effort answer anyway. Candidates={Candidates}",
                    nonAnswerableSummary);
            }

            UpstreamApiException? lastFailure = null;
            foreach (var candidate in attemptCandidates)
            {
                try
                {
                    await session.ThreeCxClient.ControlParticipantAsync(
                        candidate.Dn,
                        candidate.ParticipantId,
                        action,
                        destination,
                        cancellationToken);

                    if (candidate.ParticipantId != participantId || !string.Equals(candidate.Dn, sourceDn, StringComparison.Ordinal))
                    {
                        _logger.LogInformation(
                            "Answer call fallback succeeded with participant {ResolvedParticipantId} on DN {ResolvedDn} (requested participant {RequestedParticipantId}).",
                            candidate.ParticipantId,
                            candidate.Dn,
                            participantId);
                    }

                    return;
                }
                catch (UpstreamApiException ex)
                    when (ex.ErrorCode == (int)HttpStatusCode.UnprocessableEntity
                          || ex.ErrorCode == (int)HttpStatusCode.Forbidden
                          || ex.ErrorCode == (int)HttpStatusCode.NotFound)
                {
                    lastFailure = ex;
                    _logger.LogDebug(
                        "Answer candidate failed with {StatusCode}. Participant={ParticipantId}, Dn={Dn}, DirectControl={DirectControl}, DnType={DnType}",
                        ex.ErrorCode,
                        candidate.ParticipantId,
                        candidate.Dn,
                        candidate.DirectControl,
                        candidate.DnType ?? "(null)");
                }
            }

            if (string.Equals(action, CallControlConstants.ParticipantActionAnswer, StringComparison.OrdinalIgnoreCase)
                && !attemptCandidates.Any(IsAnswerableParticipant))
            {
                var routedAnswerSucceeded = await TryRouteToControlDnAndAnswerAsync(
                    sessionId,
                    session,
                    attemptCandidates,
                    cancellationToken);
                if (routedAnswerSucceeded)
                {
                    return;
                }
            }

            var triedSummary = string.Join(", ", attemptCandidates.Select(candidate =>
                $"{candidate.ParticipantId}@{candidate.Dn}(call={candidate.CallId?.ToString() ?? "?"},dc={candidate.DirectControl},type={candidate.DnType ?? "?"},status={candidate.Status ?? "?"})"));

            var upstreamReason = lastFailure?.Message;
            if (lastFailure?.ErrorCode == (int)HttpStatusCode.Forbidden)
            {
                throw new BadRequestException(
                    $"3CX denied control permission (403) for all answer candidates. Tried: {triedSummary}. " +
                    "Verify your 3CX API integration can control/monitor the target DN (and RoutePoint DN if used).");
            }

            throw new BadRequestException(
                $"3CX rejected all answer attempts. Tried: {triedSummary}. " +
                $"Last upstream reason: {upstreamReason ?? "none"}");
        }

        try
        {
            await session.ThreeCxClient.ControlParticipantAsync(
                sourceDn,
                targetParticipantId,
                action,
                destination,
                cancellationToken);
        }
        catch (UpstreamApiException ex)
            when (ex.ErrorCode == (int)HttpStatusCode.UnprocessableEntity
                  && string.Equals(action, CallControlConstants.ParticipantActionAnswer, StringComparison.OrdinalIgnoreCase))
        {
            throw new BadRequestException(
                "3CX rejected answer for this participant (422). " +
                "Usually this means the selected leg is not directly controllable. " +
                "Verify the ringing participant has direct_control=true.");
        }
    }

    private async Task RefreshTopologyBeforeAnswerAsync(string sessionId, SoftphoneSession session, CancellationToken cancellationToken)
    {
        IReadOnlyList<ThreeCxDnInfo> fullInfo;
        try
        {
            fullInfo = await session.ThreeCxClient.GetFullInfoAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Skipped topology refresh before answer for session {SessionId}.", sessionId);
            return;
        }

        var refreshedTopology = CallControlMapFactory.ToMap(fullInfo);
        SessionSnapshotResponse snapshot;

        await session.Gate.WaitAsync(cancellationToken);
        try
        {
            session.TopologyByDn.Clear();
            foreach (var kv in refreshedTopology)
            {
                session.TopologyByDn[kv.Key] = kv.Value;
            }

            RebuildSelectedExtensionStateLocked(session);
            session.WsConnected = session.ThreeCxClient.IsWebSocketConnected;
            session.LastUpdatedUtc = DateTimeOffset.UtcNow;
            snapshot = BuildSnapshotLocked(session);
        }
        finally
        {
            session.Gate.Release();
        }

        await _eventDispatcher.PublishSnapshotAsync(sessionId, snapshot);
    }

    private static ParticipantReference? ResolveRequestedParticipantLocked(
        SoftphoneSession session,
        long participantId,
        string selectedDn,
        string? controlDn,
        bool preferAnswerable)
    {
        var scopedMatch = OrderCandidatesForControl(
                EnumerateParticipantsLocked(session, selectedDn, controlDn)
                    .Where(candidate => candidate.ParticipantId == participantId),
                selectedDn,
                controlDn,
                preferAnswerable)
            .FirstOrDefault();

        if (scopedMatch is not null)
        {
            if (!preferAnswerable || IsAnswerableParticipant(scopedMatch))
            {
                return scopedMatch;
            }
        }

        var globalMatch = OrderCandidatesForControl(
                EnumerateParticipantsLocked(session)
                    .Where(candidate => candidate.ParticipantId == participantId),
                selectedDn,
                controlDn,
                preferAnswerable)
            .FirstOrDefault();

        return globalMatch ?? scopedMatch;
    }

    private static ParticipantReference? FindParticipantByIdLocked(
        SoftphoneSession session,
        long participantId,
        string? selectedDn,
        string? controlDn)
    {
        var effectiveSelectedDn = selectedDn ?? string.Empty;
        return OrderCandidatesForControl(
                EnumerateParticipantsLocked(session)
                    .Where(candidate => candidate.ParticipantId == participantId),
                effectiveSelectedDn,
                controlDn,
                preferAnswerable: false)
            .FirstOrDefault();
    }

    private static IReadOnlyList<ParticipantReference> ResolveAnswerCandidatesLocked(
        SoftphoneSession session,
        ParticipantReference requested,
        string selectedDn,
        string? controlDn)
    {
        var result = new List<ParticipantReference>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var scopedParticipants = EnumerateParticipantsLocked(session, selectedDn, controlDn).ToList();
        var globalParticipants = EnumerateParticipantsLocked(session).ToList();

        static string CandidateKey(ParticipantReference candidate) => $"{candidate.Dn}:{candidate.ParticipantId}";
        void AddUnique(ParticipantReference candidate)
        {
            var key = CandidateKey(candidate);
            if (!seen.Add(key))
            {
                return;
            }

            result.Add(candidate);
        }

        AddUnique(requested);

        var ringingScopedParticipants = scopedParticipants
            .Where(candidate => IsRingingParticipant(candidate.Status))
            .ToList();

        IOrderedEnumerable<ParticipantReference> OrderForAnswerFallback(IEnumerable<ParticipantReference> candidates)
        {
            return candidates
                .OrderByDescending(IsAnswerableParticipant)
                .ThenByDescending(candidate => IsRingingParticipant(candidate.Status))
                .ThenByDescending(candidate => IsParticipantOnDn(candidate, controlDn))
                .ThenByDescending(candidate => IsParticipantOnDn(candidate, selectedDn))
                .ThenBy(candidate => candidate.ParticipantId);
        }

        var sameParticipantScopedCandidates = OrderForAnswerFallback(
            ringingScopedParticipants.Where(candidate => candidate.ParticipantId == requested.ParticipantId));

        foreach (var candidate in sameParticipantScopedCandidates)
        {
            AddUnique(candidate);
        }

        var sameParticipantGlobalCandidates = OrderForAnswerFallback(
            globalParticipants.Where(candidate => candidate.ParticipantId == requested.ParticipantId));

        foreach (var candidate in sameParticipantGlobalCandidates)
        {
            AddUnique(candidate);
        }

        if (requested.CallId is long callId)
        {
            var sameCallScopedCandidates = OrderForAnswerFallback(
                scopedParticipants.Where(candidate => candidate.CallId == callId));

            foreach (var candidate in sameCallScopedCandidates)
            {
                AddUnique(candidate);
            }

            var sameCallGlobalCandidates = OrderForAnswerFallback(
                globalParticipants.Where(candidate => candidate.CallId == callId));

            foreach (var candidate in sameCallGlobalCandidates)
            {
                AddUnique(candidate);
            }
        }

        var selectedDnRingingCandidates = OrderForAnswerFallback(
            ringingScopedParticipants.Where(candidate => string.Equals(candidate.Dn, selectedDn, StringComparison.Ordinal)));

        foreach (var candidate in selectedDnRingingCandidates)
        {
            AddUnique(candidate);
        }

        if (!string.IsNullOrWhiteSpace(controlDn))
        {
            var controlDnRingingCandidates = OrderForAnswerFallback(
                ringingScopedParticipants.Where(candidate => string.Equals(candidate.Dn, controlDn, StringComparison.Ordinal)));

            foreach (var candidate in controlDnRingingCandidates)
            {
                AddUnique(candidate);
            }
        }

        return result;
    }

    private static IOrderedEnumerable<ParticipantReference> OrderCandidatesForControl(
        IEnumerable<ParticipantReference> candidates,
        string selectedDn,
        string? controlDn,
        bool preferAnswerable)
    {
        if (preferAnswerable)
        {
            return candidates
                .OrderByDescending(IsAnswerableParticipant)
                .ThenByDescending(candidate => IsParticipantOnDn(candidate, controlDn))
                .ThenByDescending(candidate => IsParticipantOnDn(candidate, selectedDn))
                .ThenBy(candidate => candidate.ParticipantId);
        }

        return candidates
            .OrderByDescending(candidate => IsParticipantOnDn(candidate, controlDn))
            .ThenByDescending(candidate => IsParticipantOnDn(candidate, selectedDn))
            .ThenByDescending(IsAnswerableParticipant)
            .ThenBy(candidate => candidate.ParticipantId);
    }

    private static IEnumerable<ParticipantReference> EnumerateParticipantsLocked(SoftphoneSession session)
    {
        foreach (var kv in session.TopologyByDn)
        {
            var dn = kv.Key;
            var dnInfo = kv.Value;
            foreach (var participantKv in dnInfo.Participants)
            {
                var participant = participantKv.Value;
                yield return new ParticipantReference(
                    participantKv.Key,
                    dn,
                    dnInfo.Type,
                    participant.DirectControl ?? false,
                    participant.CallId,
                    participant.Status);
            }
        }
    }

    private static IEnumerable<ParticipantReference> EnumerateParticipantsLocked(
        SoftphoneSession session,
        string selectedDn,
        string? controlDn)
    {
        foreach (var candidate in EnumerateParticipantsLocked(session))
        {
            if (IsParticipantOnDn(candidate, selectedDn) || IsParticipantOnDn(candidate, controlDn))
            {
                yield return candidate;
            }
        }
    }

    private static bool IsParticipantOnDn(ParticipantReference participant, string? dn)
    {
        return !string.IsNullOrWhiteSpace(dn)
            && string.Equals(participant.Dn, dn, StringComparison.Ordinal);
    }

    private static bool IsRingingParticipant(string? status)
    {
        return string.Equals(status, CallControlConstants.ParticipantStatusRinging, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAnswerableParticipant(ParticipantReference participant)
    {
        if (participant.DirectControl)
        {
            return true;
        }

        return 
            string.Equals(participant.DnType, CallControlConstants.RoutePointType, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<bool> TryRouteToControlDnAndAnswerAsync(
        string sessionId,
        SoftphoneSession session,
        IReadOnlyList<ParticipantReference> attemptCandidates,
        CancellationToken cancellationToken)
    {
        var controlDn = session.ControlDn?.Trim();
        if (string.IsNullOrWhiteSpace(controlDn))
        {
            return false;
        }

        var ringingCandidate = attemptCandidates.FirstOrDefault(candidate => IsRingingParticipant(candidate.Status));
        if (ringingCandidate is null || ringingCandidate.CallId is not long callId)
        {
            return false;
        }

        if (string.Equals(ringingCandidate.Dn, controlDn, StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            await session.ThreeCxClient.ControlParticipantAsync(
                ringingCandidate.Dn,
                ringingCandidate.ParticipantId,
                CallControlConstants.ParticipantActionRouteTo,
                controlDn,
                cancellationToken);
        }
        catch (UpstreamApiException ex)
            when (ex.ErrorCode == (int)HttpStatusCode.UnprocessableEntity
                  || ex.ErrorCode == (int)HttpStatusCode.Forbidden
                  || ex.ErrorCode == (int)HttpStatusCode.NotFound)
        {
            _logger.LogDebug(
                "Route-to-control fallback failed for session {SessionId}. Candidate={ParticipantId}@{Dn}, ControlDn={ControlDn}, Status={StatusCode}",
                sessionId,
                ringingCandidate.ParticipantId,
                ringingCandidate.Dn,
                controlDn,
                ex.ErrorCode);
            return false;
        }

        await Task.Delay(TimeSpan.FromMilliseconds(350), cancellationToken);

        IReadOnlyList<ThreeCxDnInfo> fullInfo;
        try
        {
            fullInfo = await session.ThreeCxClient.GetFullInfoAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to refresh topology after route-to-control fallback for session {SessionId}.", sessionId);
            return false;
        }

        var controlDnInfo = fullInfo.FirstOrDefault(info => string.Equals(info.Dn, controlDn, StringComparison.Ordinal));
        if (controlDnInfo is null)
        {
            return false;
        }

        var routedParticipantId = (controlDnInfo.Participants ?? [])
            .Where(participant => participant.Id.HasValue)
            .Where(participant => participant.CallId == callId)
            .Select(participant => participant.Id!.Value)
            .FirstOrDefault();

        if (routedParticipantId <= 0)
        {
            _logger.LogDebug(
                "Route-to-control fallback did not produce a control DN participant. Session={SessionId}, ControlDn={ControlDn}, CallId={CallId}",
                sessionId,
                controlDn,
                callId);
            return false;
        }

        try
        {
            await session.ThreeCxClient.ControlParticipantAsync(
                controlDn,
                routedParticipantId,
                CallControlConstants.ParticipantActionAnswer,
                null,
                cancellationToken);

            _logger.LogInformation(
                "Route-to-control fallback succeeded. Session={SessionId}, ControlDn={ControlDn}, ParticipantId={ParticipantId}, CallId={CallId}",
                sessionId,
                controlDn,
                routedParticipantId,
                callId);
            return true;
        }
        catch (UpstreamApiException ex)
            when (ex.ErrorCode == (int)HttpStatusCode.UnprocessableEntity
                  || ex.ErrorCode == (int)HttpStatusCode.Forbidden
                  || ex.ErrorCode == (int)HttpStatusCode.NotFound)
        {
            _logger.LogDebug(
                "Answer on routed control participant failed. Session={SessionId}, ControlDn={ControlDn}, ParticipantId={ParticipantId}, Status={StatusCode}",
                sessionId,
                controlDn,
                routedParticipantId,
                ex.ErrorCode);
            return false;
        }
    }

    private async Task<string> ResolveParticipantDnAsync(
        SoftphoneSession session,
        long participantId,
        CancellationToken cancellationToken)
    {
        await session.Gate.WaitAsync(cancellationToken);
        try
        {
            var selectedDn = session.SelectedExtensionDn ?? throw new BadRequestException("Select an extension first.");
            var participant = FindParticipantByIdLocked(
                session,
                participantId,
                selectedDn,
                session.ControlDn);
            return participant?.Dn ?? selectedDn;
        }
        finally
        {
            session.Gate.Release();
        }
    }

    private sealed record ParticipantReference(
        long ParticipantId,
        string Dn,
        string? DnType,
        bool DirectControl,
        long? CallId,
        string? Status);

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

                await ProcessUpsertAsync(sessionId, session, op, wsEvent, payload.Value);
                return;
            }

            if (wsEvent.Event.EventType == ThreeCxEventType.Remove)
            {
                await ProcessRemoveAsync(sessionId, session, op, wsEvent);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to process 3CX websocket event for session {SessionId}. Entity={Entity}", sessionId, wsEvent.Event.Entity);
        }
    }

    private async Task ProcessUpsertAsync(string sessionId, SoftphoneSession session, EntityOperation op, ThreeCxWsEvent wsEvent, JsonElement payload)
    {
        SessionSnapshotResponse snapshot;
        SoftphoneEventEnvelope? envelope = null;
        PbxCallCdrUpdate? cdrUpdate = null;
        ThreeCxParticipant? participantForQueueIngestion = null;
        SoftphoneCallView? callViewForQueueIngestion = null;

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
                    participantForQueueIngestion = participant;
                    callViewForQueueIngestion = callView;
                    envelope = new SoftphoneEventEnvelope
                    {
                        EventType = MapCallEventType(participant.Status),
                        Payload = new
                        {
                            call = callView,
                            sourceExtension = op.Dn
                        }
                    };

                    if (callView is not null)
                    {
                        cdrUpdate = new PbxCallCdrUpdate
                        {
                            OperatorUserId = session.AppUserId,
                            OperatorUsername = session.Username,
                            OperatorExtension = session.OwnedExtensionDn,
                            SourceDn = op.Dn,
                            ParticipantId = callView.ParticipantId,
                            PbxCallId = callView.CallId,
                            PbxLegId = callView.LegId,
                            Status = callView.Status ?? string.Empty,
                            Direction = callView.Direction,
                            RemoteParty = callView.RemoteParty,
                            RemoteName = callView.RemoteName,
                            ConnectedAtUtc = callView.ConnectedAtUtc,
                            OccurredAtUtc = DateTimeOffset.UtcNow,
                            IsEnded = false,
                            EndReason = null,
                            EventType = "pbx.participant.upsert"
                        };
                    }
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
        await PersistPbxCdrUpdateAsync(cdrUpdate);
        await _queueWebSocketIngestionBridge.TryIngestParticipantUpsertAsync(
            session,
            op,
            wsEvent,
            participantForQueueIngestion,
            callViewForQueueIngestion);
    }

    private async Task ProcessRemoveAsync(string sessionId, SoftphoneSession session, EntityOperation op, ThreeCxWsEvent wsEvent)
    {
        SessionSnapshotResponse snapshot;
        SoftphoneEventEnvelope? envelope = null;
        PbxCallCdrUpdate? cdrUpdate = null;
        SoftphoneCallView? removedCallForQueueIngestion = null;

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
                removedCallForQueueIngestion = removed;
                if (removed is not null)
                {
                    envelope = new SoftphoneEventEnvelope
                    {
                        EventType = "call.ended",
                        Payload = new { call = removed, sourceExtension = op.Dn }
                    };

                    cdrUpdate = new PbxCallCdrUpdate
                    {
                        OperatorUserId = session.AppUserId,
                        OperatorUsername = session.Username,
                        OperatorExtension = session.OwnedExtensionDn,
                        SourceDn = op.Dn,
                        ParticipantId = removed.ParticipantId,
                        PbxCallId = removed.CallId,
                        PbxLegId = removed.LegId,
                        Status = "Ended",
                        Direction = removed.Direction,
                        RemoteParty = removed.RemoteParty,
                        RemoteName = removed.RemoteName,
                        ConnectedAtUtc = removed.ConnectedAtUtc,
                        OccurredAtUtc = DateTimeOffset.UtcNow,
                        IsEnded = true,
                        EndReason = "participant_removed",
                        EventType = "pbx.participant.remove"
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
        await PersistPbxCdrUpdateAsync(cdrUpdate);
        await _queueWebSocketIngestionBridge.TryIngestParticipantRemovedAsync(
            session,
            op,
            wsEvent,
            removedCallForQueueIngestion);
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

        if (!IsMonitoredDnLocked(session, dn))
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
        if (!IsMonitoredDnLocked(session, dn))
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
        session.ConnectedAtByParticipant.Clear();

        var selectedDn = session.SelectedExtensionDn;
        if (string.IsNullOrWhiteSpace(selectedDn))
        {
            session.ActiveDeviceId = null;
            return;
        }

        if (!session.TopologyByDn.TryGetValue(selectedDn, out var dnInfo)
            || !string.Equals(dnInfo.Type, CallControlConstants.ExtensionType, StringComparison.OrdinalIgnoreCase))
        {
            session.SelectedExtensionDn = null;
            session.ActiveDeviceId = null;
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

        foreach (var monitoredDn in EnumerateMonitoredDnsLocked(session))
        {
            if (!session.TopologyByDn.TryGetValue(monitoredDn, out var monitoredDnInfo))
            {
                continue;
            }

            foreach (var participant in monitoredDnInfo.Participants.Values)
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
            ControlDn = session.ControlDn,
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
        var participantRef = FindParticipantByIdLocked(
            session,
            participantId,
            session.SelectedExtensionDn,
            session.ControlDn);
        var participantDn = participantRef?.Dn
            ?? participant.Dn
            ?? session.SelectedExtensionDn;
        var participantDnType = participantRef?.DnType ?? participant.PartyDnType;
        var answerable = participantRef is not null
            ? IsAnswerableParticipant(participantRef)
            : participant.DirectControl ?? false;

        return new SoftphoneCallView
        {
            ParticipantId = participantId,
            Dn = participantDn,
            PartyDnType = participantDnType,
            CallId = participant.CallId,
            LegId = participant.LegId,
            Status = participant.Status,
            RemoteParty = participant.PartyCallerId,
            RemoteName = participant.PartyCallerName,
            Direction = direction,
            DirectControl = participant.DirectControl ?? false,
            Answerable = answerable,
            ConnectedAtUtc = session.ConnectedAtByParticipant.TryGetValue(participantId, out var connectedAt)
                ? connectedAt
                : null
        };
    }

    private static IEnumerable<string> EnumerateMonitoredDnsLocked(SoftphoneSession session)
    {
        if (!string.IsNullOrWhiteSpace(session.SelectedExtensionDn))
        {
            yield return session.SelectedExtensionDn!;
        }

        if (!string.IsNullOrWhiteSpace(session.ControlDn)
            && !string.Equals(session.ControlDn, session.SelectedExtensionDn, StringComparison.Ordinal))
        {
            yield return session.ControlDn!;
        }
    }

    private static bool IsMonitoredDnLocked(SoftphoneSession session, string dn)
    {
        foreach (var monitoredDn in EnumerateMonitoredDnsLocked(session))
        {
            if (string.Equals(monitoredDn, dn, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private async Task PersistPbxCdrUpdateAsync(PbxCallCdrUpdate? update)
    {
        if (update is null)
        {
            return;
        }

        try
        {
            await _callCdrService.UpsertPbxCallAsync(update, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to persist PBX CDR update. OperatorUserId={OperatorUserId}, ParticipantId={ParticipantId}, PbxCallId={PbxCallId}",
                update.OperatorUserId,
                update.ParticipantId,
                update.PbxCallId);
        }
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
