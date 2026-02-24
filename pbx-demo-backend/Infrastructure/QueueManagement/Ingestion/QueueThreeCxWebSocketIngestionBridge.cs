using System.Text.Json;
using System.Text.Json.Serialization;
using CallControl.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using pbx_demo_backend.Domain.QueueManagement.Contracts;

namespace CallControl.Api.Infrastructure.QueueManagement.Ingestion;

public sealed class QueueThreeCxWebSocketIngestionBridge
{
    private const string QueueWebSocketSource = "ThreeCxWebSocket";
    private static readonly JsonSerializerOptions PayloadJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly QueueEventIdempotencyKeyFactory _idempotencyKeys;
    private readonly ILogger<QueueThreeCxWebSocketIngestionBridge> _logger;

    public QueueThreeCxWebSocketIngestionBridge(
        IServiceScopeFactory scopeFactory,
        QueueEventIdempotencyKeyFactory idempotencyKeys,
        ILogger<QueueThreeCxWebSocketIngestionBridge> logger)
    {
        _scopeFactory = scopeFactory;
        _idempotencyKeys = idempotencyKeys;
        _logger = logger;
    }

    public async Task TryIngestParticipantUpsertAsync(
        SoftphoneSession session,
        EntityOperation op,
        ThreeCxWsEvent wsEvent,
        ThreeCxParticipant? participant,
        SoftphoneCallView? callView,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(wsEvent);

        if (participant is null || !IsParticipantEntity(op))
        {
            return;
        }

        if (!TryGetPbxCallId(participant.CallId ?? callView?.CallId, out var pbxCallId))
        {
            return;
        }

        if (IsKnownNonQueueDn(session, op.Dn))
        {
            return;
        }

        try
        {
            var observedAtUtc = DateTimeOffset.UtcNow;
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PBXDbContext>();
            var eventProcessor = scope.ServiceProvider.GetRequiredService<IQueueEventProcessor>();

            var queueHint = await ResolveQueueHintAsync(db, op.Dn, ct);
            if (queueHint is null)
            {
                return;
            }

            var status = FirstNonEmpty(participant.Status, callView?.Status);
            var correlationKey = BuildCorrelationKey(pbxCallId);
            var payloadJson = JsonSerializer.Serialize(
                new
                {
                    queueIdHint = queueHint.QueueId,
                    pbxCallId,
                    correlationKey,
                    status,
                    caller = FirstNonEmpty(callView?.RemoteParty, participant.PartyCallerId),
                    callee = FirstNonEmpty(participant.Dn, op.Dn),
                    establishedAtUtc = callView?.ConnectedAtUtc,
                    lastChangeStatusAtUtc = observedAtUtc,
                    serverNowUtc = observedAtUtc,
                    observedAtUtc,
                    snapshotKey = Guid.Empty,
                    waitOrder = (int?)null,
                    estimatedOrder = true,
                    rawWebSocketEvent = BuildRawWebSocketEvent(wsEvent),
                    rawParticipant = participant,
                    rawCallView = callView
                },
                PayloadJsonOptions);

            var envelope = new QueueInboundEventEnvelope
            {
                Source = QueueWebSocketSource,
                EventType = QueueIngestionEventTypes.ActiveCallObserved,
                EventAtUtc = observedAtUtc,
                OrderingKey = BuildOrderingKey(pbxCallId),
                IdempotencyKey = _idempotencyKeys.Create(
                    "3cx-ws-participant-upsert",
                    wsEvent.Sequence,
                    wsEvent.Event?.Entity,
                    wsEvent.Event?.EventType.ToString(),
                    queueHint.QueueId,
                    pbxCallId,
                    status),
                PayloadJson = payloadJson
            };

            await eventProcessor.ProcessAsync(envelope, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(
                ex,
                "Queue websocket upsert bridge ignored event. Dn={Dn}, Entity={Entity}, Sequence={Sequence}.",
                op.Dn,
                wsEvent.Event?.Entity,
                wsEvent.Sequence);
        }
    }

    public async Task TryIngestParticipantRemovedAsync(
        SoftphoneSession session,
        EntityOperation op,
        ThreeCxWsEvent wsEvent,
        SoftphoneCallView? removedCall,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(wsEvent);

        if (removedCall is null || !IsParticipantEntity(op))
        {
            return;
        }

        if (!TryGetPbxCallId(removedCall.CallId, out var pbxCallId))
        {
            return;
        }

        if (IsKnownNonQueueDn(session, op.Dn))
        {
            return;
        }

        try
        {
            var observedAtUtc = DateTimeOffset.UtcNow;
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PBXDbContext>();
            var eventProcessor = scope.ServiceProvider.GetRequiredService<IQueueEventProcessor>();

            var queueHint = await ResolveQueueHintAsync(db, op.Dn, ct);
            if (queueHint is null)
            {
                return;
            }

            var correlationKey = BuildCorrelationKey(pbxCallId);
            var payloadJson = JsonSerializer.Serialize(
                new
                {
                    pbxCallId,
                    correlationKey,
                    queueIdHint = queueHint.QueueId,
                    lastKnownStatus = removedCall.Status,
                    lastSeenAtUtc = observedAtUtc,
                    observedAtUtc,
                    rawWebSocketEvent = BuildRawWebSocketEvent(wsEvent),
                    rawRemovedCall = removedCall
                },
                PayloadJsonOptions);

            var envelope = new QueueInboundEventEnvelope
            {
                Source = QueueWebSocketSource,
                EventType = QueueIngestionEventTypes.ActiveCallDisappeared,
                EventAtUtc = observedAtUtc,
                OrderingKey = BuildOrderingKey(pbxCallId),
                IdempotencyKey = _idempotencyKeys.Create(
                    "3cx-ws-participant-remove",
                    wsEvent.Sequence,
                    wsEvent.Event?.Entity,
                    wsEvent.Event?.EventType.ToString(),
                    queueHint.QueueId,
                    pbxCallId),
                PayloadJson = payloadJson
            };

            await eventProcessor.ProcessAsync(envelope, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(
                ex,
                "Queue websocket remove bridge ignored event. Dn={Dn}, Entity={Entity}, Sequence={Sequence}.",
                op.Dn,
                wsEvent.Event?.Entity,
                wsEvent.Sequence);
        }
    }

    private static bool IsParticipantEntity(EntityOperation op)
        => !string.IsNullOrWhiteSpace(op.Dn)
           && string.Equals(op.Type, CallControlConstants.ParticipantEntity, StringComparison.OrdinalIgnoreCase);

    private static bool IsKnownNonQueueDn(SoftphoneSession session, string dn)
    {
        if (!session.TopologyByDn.TryGetValue(dn, out var dnInfo))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(dnInfo.Type))
        {
            return false;
        }

        return !string.Equals(dnInfo.Type, CallControlConstants.RoutePointType, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetPbxCallId(long? rawCallId, out int pbxCallId)
    {
        pbxCallId = 0;
        if (rawCallId is null || rawCallId <= 0 || rawCallId > int.MaxValue)
        {
            return false;
        }

        pbxCallId = (int)rawCallId.Value;
        return true;
    }

    private static string BuildCorrelationKey(int pbxCallId)
        => $"xapi-activecall:{pbxCallId}";

    private static string BuildOrderingKey(int pbxCallId)
        => $"callkey:call:{pbxCallId}";

    private static object BuildRawWebSocketEvent(ThreeCxWsEvent wsEvent)
    {
        return new
        {
            sequence = wsEvent.Sequence,
            @event = new
            {
                eventType = wsEvent.Event?.EventType.ToString(),
                entity = wsEvent.Event?.Entity,
                attachedData = wsEvent.Event?.AttachedData
            }
        };
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    private static async Task<QueueRouteHint?> ResolveQueueHintAsync(PBXDbContext db, string queueDn, CancellationToken ct)
    {
        var normalized = queueDn?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        return await db.Queues
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.QueueNumber == normalized)
            .Select(x => new QueueRouteHint(x.Id, x.QueueNumber))
            .FirstOrDefaultAsync(ct);
    }

    private sealed record QueueRouteHint(long QueueId, string QueueNumber);
}
