using System.Text.Json;
using CallControl.Api.Domain;
using CallControl.Api.Infrastructure.QueueManagement.Persistence;
using Microsoft.EntityFrameworkCore;
using pbx_demo_backend.Domain.QueueManagement.Contracts;
using pbx_demo_backend.Domain.QueueManagement.Domain;

namespace CallControl.Api.Infrastructure.QueueManagement.Ingestion;

public sealed class QueueCallLifecycleManager
{
    private static readonly JsonSerializerOptions PayloadJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly PBXDbContext _db;
    private readonly QueueEventIdempotencyKeyFactory _idempotencyKeys;
    private readonly QueueCallStateMachine _stateMachine = new();
    private readonly ILogger<QueueCallLifecycleManager> _logger;

    public QueueCallLifecycleManager(
        PBXDbContext db,
        QueueEventIdempotencyKeyFactory idempotencyKeys,
        ILogger<QueueCallLifecycleManager> logger)
    {
        _db = db;
        _idempotencyKeys = idempotencyKeys;
        _logger = logger;
    }

    public async Task ApplyAsync(QueueCallEventEntity inboxEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(inboxEvent);

        var workItem = BuildWorkItem(inboxEvent);
        var callEntity = await ResolveOrCreateCallEntityAsync(workItem, ct);
        var aggregate = Rehydrate(callEntity, workItem.EventAtUtc);

        aggregate.SetQueue(workItem.QueueIdHint ?? callEntity.QueueId);
        aggregate.SetSlaThresholdSeconds(callEntity.SlaThresholdSec);
        aggregate.SetPartyInfo(
            workItem.CallerNumber ?? callEntity.CallerNumber,
            workItem.CallerName ?? callEntity.CallerName,
            workItem.CalleeNumber ?? callEntity.CalleeNumber,
            workItem.CalleeName ?? callEntity.CalleeName,
            workItem.Direction ?? callEntity.Direction);

        aggregate.MergeCorrelationIds(
            pbxCallId: workItem.PbxCallId ?? callEntity.PbxCallId,
            cdrId: workItem.CdrId ?? callEntity.CdrId,
            callHistoryId: workItem.CallHistoryId ?? callEntity.CallHistoryId,
            mainCallHistoryId: workItem.MainCallHistoryId ?? callEntity.MainCallHistoryId,
            segmentId: workItem.SegmentId ?? callEntity.CurrentSegmentId);

        if (aggregate.QueueId is not null && callEntity.SlaThresholdSec is null)
        {
            callEntity.SlaThresholdSec = await TryResolveQueueSlaThresholdAsync(aggregate.QueueId.Value, ct);
            aggregate.SetSlaThresholdSeconds(callEntity.SlaThresholdSec);
        }

        foreach (var transition in workItem.Transitions)
        {
            var decision = aggregate.Apply(_stateMachine, transition);
            if (!decision.Accepted)
            {
                _logger.LogWarning(
                    "Queue event transition rejected. EventId={EventId}, CallKey={CallKey}, Transition={TransitionType}.",
                    inboxEvent.Id,
                    workItem.CorrelationKey,
                    transition.TransitionType);
            }
        }

        if (workItem.WaitingMsOverride is not null
            || workItem.RingingMsOverride is not null
            || workItem.TalkingMsOverride is not null
            || workItem.WrapUpMsOverride is not null)
        {
            aggregate.SetFinalDurations(
                waitingMs: workItem.WaitingMsOverride ?? aggregate.Durations.WaitingMs,
                talkingMs: workItem.TalkingMsOverride ?? aggregate.Durations.TalkingMs,
                ringingMs: workItem.RingingMsOverride ?? aggregate.Durations.RingingMs,
                wrapUpMs: workItem.WrapUpMsOverride ?? aggregate.Durations.WrapUpMs);
        }

        if (workItem.ClearReconciliationMarker)
        {
            aggregate.MarkReconciled();
        }

        ApplyProjection(callEntity, aggregate, inboxEvent, workItem);

        inboxEvent.Queue = callEntity.Queue;
        inboxEvent.QueueCall = callEntity;
        inboxEvent.QueueCallId = callEntity.Id > 0 ? callEntity.Id : null;
        inboxEvent.QueueId = callEntity.QueueId;

        var resolvedExtensionId = workItem.ExtensionIdHint
            ?? callEntity.LastAgentExtensionId
            ?? callEntity.AnsweredByExtensionId;

        if (resolvedExtensionId is not null)
        {
            inboxEvent.ExtensionId = resolvedExtensionId;
        }

        await TryWriteAgentActivitiesAsync(inboxEvent, callEntity, workItem, ct);
        await TryWriteWaitingSnapshotAsync(callEntity, workItem, ct);
        WriteOutboxMessages(inboxEvent, callEntity, aggregate, workItem);

        aggregate.ClearDomainEvents();
    }

    private QueueLifecycleWorkItem BuildWorkItem(QueueCallEventEntity inboxEvent)
    {
        var eventType = inboxEvent.EventType?.Trim() ?? string.Empty;

        return eventType switch
        {
            QueueIngestionEventTypes.ActiveCallObserved => BuildFromActiveCallObserved(inboxEvent),
            QueueIngestionEventTypes.ActiveCallDisappeared => BuildFromActiveCallDisappeared(inboxEvent),
            QueueIngestionEventTypes.CallHistorySegmentReconciled => BuildFromCallHistorySegment(inboxEvent),
            QueueIngestionEventTypes.CallLogRecordReconciled => BuildFromCallLogRecord(inboxEvent),
            _ => throw new BadRequestException($"Unsupported queue ingestion event type '{eventType}'.")
        };
    }

    private QueueLifecycleWorkItem BuildFromActiveCallObserved(QueueCallEventEntity inboxEvent)
    {
        var payload = DeserializePayload<QueueActiveCallObservedPayload>(inboxEvent);
        var transitionType = InferTransitionFromActiveStatus(payload.Status, payload.EstablishedAtUtc is not null);
        var eventAtUtc = inboxEvent.EventAtUtc == default ? payload.ObservedAtUtc : inboxEvent.EventAtUtc;

        return new QueueLifecycleWorkItem
        {
            CorrelationKey = payload.CorrelationKey,
            EventAtUtc = eventAtUtc,
            ObservedAtUtc = payload.ObservedAtUtc == default ? eventAtUtc : payload.ObservedAtUtc,
            PbxCallId = payload.PbxCallId,
            CallerNumber = payload.Caller,
            CalleeNumber = payload.Callee,
            RawCurrentJson = inboxEvent.PayloadJson,
            SnapshotKey = payload.SnapshotKey,
            WaitOrder = payload.WaitOrder,
            EstimatedWaitOrder = payload.EstimatedOrder,
            Transitions =
            [
                new QueueCallTransitionCommand
                {
                    TransitionType = transitionType,
                    OccurredAtUtc = eventAtUtc,
                    WaitOrder = payload.WaitOrder,
                    Source = inboxEvent.Source,
                    SequenceNo = inboxEvent.Id,
                    ExternalEventId = payload.PbxCallId.ToString()
                }
            ]
        };
    }

    private QueueLifecycleWorkItem BuildFromActiveCallDisappeared(QueueCallEventEntity inboxEvent)
    {
        var payload = DeserializePayload<QueueActiveCallDisappearedPayload>(inboxEvent);
        var eventAtUtc = inboxEvent.EventAtUtc == default ? payload.ObservedAtUtc : inboxEvent.EventAtUtc;

        return new QueueLifecycleWorkItem
        {
            CorrelationKey = !string.IsNullOrWhiteSpace(payload.CorrelationKey)
                ? payload.CorrelationKey
                : $"xapi-activecall:{payload.PbxCallId}",
            EventAtUtc = eventAtUtc,
            ObservedAtUtc = payload.ObservedAtUtc == default ? eventAtUtc : payload.ObservedAtUtc,
            PbxCallId = payload.PbxCallId,
            QueueIdHint = payload.QueueIdHint,
            RawCurrentJson = inboxEvent.PayloadJson,
            Transitions =
            [
                new QueueCallTransitionCommand
                {
                    TransitionType = QueueCallTransitionType.Completed,
                    OccurredAtUtc = eventAtUtc,
                    QueueId = payload.QueueIdHint,
                    Source = inboxEvent.Source,
                    SequenceNo = inboxEvent.Id,
                    ExternalEventId = payload.PbxCallId?.ToString()
                }
            ]
        };
    }

    private QueueLifecycleWorkItem BuildFromCallHistorySegment(QueueCallEventEntity inboxEvent)
    {
        var payload = DeserializePayload<QueueCallHistoryReconciliationPayload>(inboxEvent);
        var row = payload.Row ?? throw new BadRequestException("CallHistory reconciliation payload row is required.");
        var transitions = new List<QueueCallTransitionCommand>();

        transitions.Add(new QueueCallTransitionCommand
        {
            TransitionType = QueueCallTransitionType.Waiting,
            OccurredAtUtc = row.SegmentStartTime,
            QueueId = payload.QueueIdHint,
            Source = inboxEvent.Source,
            SequenceNo = inboxEvent.Id,
            ExternalEventId = $"seg:{row.SegmentId}"
        });

        var endAtUtc = row.SegmentEndTime >= row.SegmentStartTime ? row.SegmentEndTime : row.SegmentStartTime;
        if (row.CallAnswered == true)
        {
            transitions.Add(new QueueCallTransitionCommand
            {
                TransitionType = QueueCallTransitionType.Answered,
                OccurredAtUtc = endAtUtc,
                QueueId = payload.QueueIdHint,
                Source = inboxEvent.Source,
                SequenceNo = inboxEvent.Id,
                ExternalEventId = $"seg:{row.SegmentId}:ans"
            });
        }
        else if (row.CallAnswered == false)
        {
            transitions.Add(new QueueCallTransitionCommand
            {
                TransitionType = QueueCallTransitionType.Missed,
                OccurredAtUtc = endAtUtc,
                QueueId = payload.QueueIdHint,
                Source = inboxEvent.Source,
                SequenceNo = inboxEvent.Id,
                ExternalEventId = $"seg:{row.SegmentId}:miss"
            });
        }

        return new QueueLifecycleWorkItem
        {
            CorrelationKey = payload.CorrelationKey,
            EventAtUtc = inboxEvent.EventAtUtc == default ? endAtUtc : inboxEvent.EventAtUtc,
            ObservedAtUtc = payload.ObservedAtUtc == default ? endAtUtc : payload.ObservedAtUtc,
            QueueIdHint = payload.QueueIdHint,
            SegmentId = row.SegmentId,
            CallerNumber = row.SrcCallerNumber,
            CallerName = row.SrcDisplayName ?? row.SrcExtendedDisplayName,
            CalleeNumber = row.DstCallerNumber,
            CalleeName = row.DstDisplayName ?? row.DstExtendedDisplayName,
            RawCurrentJson = inboxEvent.PayloadJson,
            WaitingMsOverride = QueueReconciliationMapper.TryParseDurationMs(row.CallTime),
            ClearReconciliationMarker = false,
            Transitions = transitions
        };
    }

    private QueueLifecycleWorkItem BuildFromCallLogRecord(QueueCallEventEntity inboxEvent)
    {
        var payload = DeserializePayload<QueueCallLogReconciliationPayload>(inboxEvent);
        var row = payload.Row ?? throw new BadRequestException("CallLog reconciliation payload row is required.");
        var eventAtUtc = inboxEvent.EventAtUtc == default ? payload.ObservedAtUtc : inboxEvent.EventAtUtc;
        var terminalTransition = InferTerminalTransitionFromCallLog(row);
        var transitions = new List<QueueCallTransitionCommand>();

        if (row.Answered == true && row.StartTime is not null)
        {
            transitions.Add(new QueueCallTransitionCommand
            {
                TransitionType = QueueCallTransitionType.Answered,
                OccurredAtUtc = row.StartTime.Value,
                QueueId = payload.QueueIdHint,
                AgentId = payload.ExtensionIdHint,
                Source = inboxEvent.Source,
                SequenceNo = inboxEvent.Id,
                ExternalEventId = row.CdrId
            });
        }

        transitions.Add(new QueueCallTransitionCommand
        {
            TransitionType = terminalTransition,
            OccurredAtUtc = eventAtUtc,
            QueueId = payload.QueueIdHint,
            AgentId = payload.ExtensionIdHint,
            Source = inboxEvent.Source,
            SequenceNo = inboxEvent.Id,
            ExternalEventId = row.CdrId
        });

        return new QueueLifecycleWorkItem
        {
            CorrelationKey = payload.CorrelationKey,
            EventAtUtc = eventAtUtc,
            ObservedAtUtc = payload.ObservedAtUtc == default ? eventAtUtc : payload.ObservedAtUtc,
            QueueIdHint = payload.QueueIdHint,
            ExtensionIdHint = payload.ExtensionIdHint,
            PbxCallId = row.CallId,
            CdrId = row.CdrId,
            CallHistoryId = row.CallHistoryId,
            MainCallHistoryId = row.MainCallHistoryId,
            SegmentId = row.SegmentId,
            CallerNumber = row.SourceCallerId,
            CallerName = row.SourceDisplayName,
            CalleeNumber = row.DestinationCallerId,
            CalleeName = row.DestinationDisplayName,
            Direction = row.Direction,
            RawCurrentJson = inboxEvent.PayloadJson,
            RingingMsOverride = QueueReconciliationMapper.TryParseDurationMs(row.RingingDuration),
            TalkingMsOverride = QueueReconciliationMapper.TryParseDurationMs(row.TalkingDuration),
            ClearReconciliationMarker = true,
            Transitions = transitions
        };
    }

    private async Task<QueueCallEntity> ResolveOrCreateCallEntityAsync(QueueLifecycleWorkItem workItem, CancellationToken ct)
    {
        QueueCallEntity? entity = null;

        if (!string.IsNullOrWhiteSpace(workItem.CdrId))
        {
            entity = await _db.QueueCalls
                .FirstOrDefaultAsync(x => x.CdrId == workItem.CdrId, ct);
        }

        if (entity is null && !string.IsNullOrWhiteSpace(workItem.CallHistoryId))
        {
            entity = await _db.QueueCalls
                .FirstOrDefaultAsync(x => x.CallHistoryId == workItem.CallHistoryId, ct);
        }

        if (entity is null && !string.IsNullOrWhiteSpace(workItem.MainCallHistoryId))
        {
            entity = await _db.QueueCalls
                .FirstOrDefaultAsync(x => x.MainCallHistoryId == workItem.MainCallHistoryId, ct);
        }

        if (entity is null && workItem.PbxCallId is not null)
        {
            entity = await _db.QueueCalls
                .FirstOrDefaultAsync(x => x.PbxCallId == workItem.PbxCallId, ct);
        }

        if (entity is null)
        {
            entity = await _db.QueueCalls
                .FirstOrDefaultAsync(x => x.CorrelationKey == workItem.CorrelationKey, ct);
        }

        if (entity is not null)
        {
            return entity;
        }

        entity = new QueueCallEntity
        {
            CorrelationKey = workItem.CorrelationKey,
            QueueId = workItem.QueueIdHint,
            PbxCallId = workItem.PbxCallId,
            CdrId = Trim(workItem.CdrId),
            CallHistoryId = Trim(workItem.CallHistoryId),
            MainCallHistoryId = Trim(workItem.MainCallHistoryId),
            CurrentSegmentId = workItem.SegmentId,
            FirstSeenAtUtc = workItem.EventAtUtc,
            LastSeenAtUtc = workItem.ObservedAtUtc == default ? workItem.EventAtUtc : workItem.ObservedAtUtc,
            CurrentStatus = QueueCallLifecycleStatus.Unknown,
            Disposition = QueueCallDisposition.Unknown,
            CallerNumber = Trim(workItem.CallerNumber),
            CallerName = Trim(workItem.CallerName),
            CalleeNumber = Trim(workItem.CalleeNumber),
            CalleeName = Trim(workItem.CalleeName),
            Direction = Trim(workItem.Direction),
            RawCurrentJson = workItem.RawCurrentJson,
            ProjectionVersion = 0
        };

        _db.QueueCalls.Add(entity);
        return entity;
    }

    private QueueCallAggregate Rehydrate(QueueCallEntity entity, DateTimeOffset fallbackAtUtc)
    {
        var firstSeenAtUtc = entity.FirstSeenAtUtc == default ? fallbackAtUtc : entity.FirstSeenAtUtc;
        var aggregate = QueueCallAggregate.Create(new QueueCallKey(entity.CorrelationKey), firstSeenAtUtc);

        aggregate.SetQueue(entity.QueueId);
        aggregate.SetSlaThresholdSeconds(entity.SlaThresholdSec);
        aggregate.SetPartyInfo(entity.CallerNumber, entity.CallerName, entity.CalleeNumber, entity.CalleeName, entity.Direction);
        aggregate.MergeCorrelationIds(
            pbxCallId: entity.PbxCallId,
            cdrId: entity.CdrId,
            callHistoryId: entity.CallHistoryId,
            mainCallHistoryId: entity.MainCallHistoryId,
            segmentId: entity.CurrentSegmentId);

        ReplayProjectionState(aggregate, entity, fallbackAtUtc);
        aggregate.SetFinalDurations(entity.WaitingMs, entity.TalkingMs, entity.RingingMs, entity.WrapUpMs);
        aggregate.ClearDomainEvents();
        return aggregate;
    }

    private void ReplayProjectionState(QueueCallAggregate aggregate, QueueCallEntity entity, DateTimeOffset fallbackAtUtc)
    {
        const string source = "projection-rehydrate";
        var replayCommands = new List<QueueCallTransitionCommand>();
        var baseAtUtc = entity.LastSeenAtUtc == default ? fallbackAtUtc : entity.LastSeenAtUtc;

        if (entity.QueuedAtUtc is not null)
        {
            replayCommands.Add(new QueueCallTransitionCommand
            {
                TransitionType = QueueCallTransitionType.EnteredQueue,
                OccurredAtUtc = entity.QueuedAtUtc.Value,
                QueueId = entity.QueueId,
                Source = source
            });
        }

        if (entity.QueuedAtUtc is not null &&
            entity.CurrentStatus is QueueCallLifecycleStatus.Waiting or QueueCallLifecycleStatus.Ringing or QueueCallLifecycleStatus.Answered or QueueCallLifecycleStatus.Transferred or QueueCallLifecycleStatus.Missed)
        {
            replayCommands.Add(new QueueCallTransitionCommand
            {
                TransitionType = QueueCallTransitionType.Waiting,
                OccurredAtUtc = entity.QueuedAtUtc.Value,
                QueueId = entity.QueueId,
                WaitOrder = entity.WaitOrder,
                Source = source
            });
        }

        if (entity.OfferedToAgentAtUtc is not null)
        {
            replayCommands.Add(new QueueCallTransitionCommand
            {
                TransitionType = QueueCallTransitionType.Ringing,
                OccurredAtUtc = entity.OfferedToAgentAtUtc.Value,
                QueueId = entity.QueueId,
                AgentId = entity.LastAgentExtensionId,
                Source = source
            });
        }

        if (entity.AnsweredAtUtc is not null)
        {
            replayCommands.Add(new QueueCallTransitionCommand
            {
                TransitionType = QueueCallTransitionType.Answered,
                OccurredAtUtc = entity.AnsweredAtUtc.Value,
                QueueId = entity.QueueId,
                AgentId = entity.AnsweredByExtensionId ?? entity.LastAgentExtensionId,
                Source = source
            });
        }

        if (entity.TransferCount > 0)
        {
            var transferBaseAtUtc = entity.AnsweredAtUtc ?? entity.OfferedToAgentAtUtc ?? baseAtUtc;
            for (var i = 0; i < entity.TransferCount; i++)
            {
                replayCommands.Add(new QueueCallTransitionCommand
                {
                    TransitionType = QueueCallTransitionType.Transferred,
                    OccurredAtUtc = transferBaseAtUtc.AddMilliseconds(i),
                    QueueId = entity.QueueId,
                    AgentId = entity.LastAgentExtensionId,
                    Source = source
                });
            }
        }

        if (entity.CurrentStatus == QueueCallLifecycleStatus.Missed && entity.CompletedAtUtc is null && entity.AbandonedAtUtc is null)
        {
            replayCommands.Add(new QueueCallTransitionCommand
            {
                TransitionType = QueueCallTransitionType.Missed,
                OccurredAtUtc = baseAtUtc,
                QueueId = entity.QueueId,
                AgentId = entity.LastAgentExtensionId,
                Source = source
            });
        }

        if (entity.AbandonedAtUtc is not null)
        {
            replayCommands.Add(new QueueCallTransitionCommand
            {
                TransitionType = QueueCallTransitionType.Abandoned,
                OccurredAtUtc = entity.AbandonedAtUtc.Value,
                QueueId = entity.QueueId,
                Source = source
            });
        }

        if (entity.CompletedAtUtc is not null)
        {
            replayCommands.Add(new QueueCallTransitionCommand
            {
                TransitionType = QueueCallTransitionType.Completed,
                OccurredAtUtc = entity.CompletedAtUtc.Value,
                QueueId = entity.QueueId,
                Source = source
            });
        }
        else
        {
            var fallbackTransition = entity.CurrentStatus switch
            {
                QueueCallLifecycleStatus.Waiting => QueueCallTransitionType.Waiting,
                QueueCallLifecycleStatus.Ringing => QueueCallTransitionType.Ringing,
                QueueCallLifecycleStatus.Answered => QueueCallTransitionType.Answered,
                QueueCallLifecycleStatus.Transferred => QueueCallTransitionType.Transferred,
                _ => (QueueCallTransitionType?)null
            };

            if (fallbackTransition is not null)
            {
                replayCommands.Add(new QueueCallTransitionCommand
                {
                    TransitionType = fallbackTransition.Value,
                    OccurredAtUtc = baseAtUtc,
                    QueueId = entity.QueueId,
                    AgentId = entity.LastAgentExtensionId ?? entity.AnsweredByExtensionId,
                    Source = source
                });
            }
        }

        foreach (var command in replayCommands.OrderBy(x => x.OccurredAtUtc).ThenBy(x => (int)x.TransitionType))
        {
            aggregate.Apply(_stateMachine, command);
        }
    }

    private void ApplyProjection(
        QueueCallEntity entity,
        QueueCallAggregate aggregate,
        QueueCallEventEntity inboxEvent,
        QueueLifecycleWorkItem workItem)
    {
        entity.QueueId = aggregate.QueueId;
        entity.PbxCallId ??= workItem.PbxCallId;
        entity.CdrId ??= Trim(workItem.CdrId);
        entity.CallHistoryId ??= Trim(workItem.CallHistoryId);
        entity.MainCallHistoryId ??= Trim(workItem.MainCallHistoryId);
        entity.CurrentSegmentId = aggregate.CorrelationIds.SegmentId ?? entity.CurrentSegmentId ?? workItem.SegmentId;

        entity.CallerNumber = Trim(aggregate.CallerNumber);
        entity.CallerName = Trim(aggregate.CallerName);
        entity.CalleeNumber = Trim(aggregate.CalleeNumber);
        entity.CalleeName = Trim(aggregate.CalleeName);
        entity.Direction = Trim(aggregate.Direction);

        entity.CurrentStatus = aggregate.Status;
        entity.Disposition = aggregate.Disposition;
        entity.WaitOrder = aggregate.WaitOrder;
        entity.TransferCount = aggregate.TransferCount;
        entity.SlaBreached = aggregate.SlaBreached;

        entity.QueuedAtUtc = aggregate.Timeline.QueuedAtUtc;
        entity.OfferedToAgentAtUtc = aggregate.Timeline.OfferedAtUtc;
        entity.AnsweredAtUtc = aggregate.Timeline.AnsweredAtUtc;
        entity.EstablishedAtUtc ??= aggregate.Timeline.AnsweredAtUtc;
        entity.AbandonedAtUtc = aggregate.Timeline.AbandonedAtUtc;
        entity.CompletedAtUtc = aggregate.Timeline.CompletedAtUtc;
        entity.LastSeenAtUtc = MaxUtc(entity.LastSeenAtUtc, workItem.ObservedAtUtc, inboxEvent.EventAtUtc, DateTimeOffset.UtcNow);

        entity.WaitingMs = aggregate.Durations.WaitingMs;
        entity.RingingMs = aggregate.Durations.RingingMs;
        entity.TalkingMs = aggregate.Durations.TalkingMs;
        entity.WrapUpMs = aggregate.Durations.WrapUpMs;
        entity.AnsweredByExtensionId = aggregate.AnsweredByAgentId ?? entity.AnsweredByExtensionId;
        entity.LastAgentExtensionId = aggregate.CurrentAgentId ?? entity.LastAgentExtensionId;
        entity.RawCurrentJson = string.IsNullOrWhiteSpace(workItem.RawCurrentJson) ? entity.RawCurrentJson : workItem.RawCurrentJson;
        entity.ProjectionVersion = Math.Max(0, entity.ProjectionVersion) + 1;
        entity.UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    private async Task TryWriteAgentActivitiesAsync(
        QueueCallEventEntity inboxEvent,
        QueueCallEntity callEntity,
        QueueLifecycleWorkItem workItem,
        CancellationToken ct)
    {
        var activities = new List<(QueueAgentActivityType Type, DateTimeOffset AtUtc, long? DurationMs)>();

        foreach (var transition in workItem.Transitions)
        {
            var mapped = transition.TransitionType switch
            {
                QueueCallTransitionType.Ringing => QueueAgentActivityType.Offer,
                QueueCallTransitionType.Answered => QueueAgentActivityType.Answer,
                QueueCallTransitionType.Missed => QueueAgentActivityType.Missed,
                QueueCallTransitionType.Transferred => QueueAgentActivityType.Transfer,
                QueueCallTransitionType.Completed when callEntity.TalkingMs is not null => QueueAgentActivityType.TalkingEnd,
                _ => (QueueAgentActivityType?)null
            };

            if (mapped is null)
            {
                continue;
            }

            activities.Add((mapped.Value, transition.OccurredAtUtc, mapped == QueueAgentActivityType.TalkingEnd ? callEntity.TalkingMs : null));
        }

        if (activities.Count == 0)
        {
            return;
        }

        var extensionId = workItem.ExtensionIdHint ?? callEntity.LastAgentExtensionId ?? callEntity.AnsweredByExtensionId;
        if (extensionId is null || extensionId.Value <= 0)
        {
            return;
        }

        foreach (var item in activities)
        {
            var idempotencyKey = _idempotencyKeys.CreateAgentActivity(
                item.Type,
                extensionId.Value,
                callEntity.Id > 0 ? callEntity.Id : null,
                inboxEvent.IdempotencyKey);

            var exists = await _db.QueueAgentActivities.AnyAsync(
                x => x.IdempotencyKey == idempotencyKey,
                ct);

            if (exists)
            {
                continue;
            }

            _db.QueueAgentActivities.Add(new QueueAgentActivityEntity
            {
                QueueId = callEntity.QueueId,
                ExtensionId = extensionId.Value,
                QueueCall = callEntity,
                QueueCallId = callEntity.Id > 0 ? callEntity.Id : null,
                ActivityType = item.Type,
                ActivityStatus = callEntity.CurrentStatus.ToString(),
                OccurredAtUtc = item.AtUtc,
                DurationMs = item.DurationMs,
                Source = inboxEvent.Source,
                IdempotencyKey = idempotencyKey,
                RawJson = inboxEvent.PayloadJson
            });
        }
    }

    private async Task TryWriteWaitingSnapshotAsync(
        QueueCallEntity callEntity,
        QueueLifecycleWorkItem workItem,
        CancellationToken ct)
    {
        if (workItem.SnapshotKey is null)
        {
            return;
        }

        if (callEntity.QueueId is null || callEntity.QueueId.Value <= 0)
        {
            return;
        }

        if (callEntity.CurrentStatus != QueueCallLifecycleStatus.Waiting)
        {
            return;
        }

        var waitOrder = workItem.WaitOrder ?? callEntity.WaitOrder;
        if (waitOrder is null || waitOrder.Value < 0)
        {
            return;
        }

        var exists = await _db.QueueWaitingSnapshots.AnyAsync(x =>
            x.QueueId == callEntity.QueueId.Value &&
            x.SnapshotKey == workItem.SnapshotKey.Value &&
            x.WaitOrder == waitOrder.Value, ct);

        if (exists)
        {
            return;
        }

        _db.QueueWaitingSnapshots.Add(new QueueWaitingSnapshotEntity
        {
            QueueId = callEntity.QueueId.Value,
            SnapshotKey = workItem.SnapshotKey.Value,
            CapturedAtUtc = workItem.ObservedAtUtc == default ? DateTimeOffset.UtcNow : workItem.ObservedAtUtc,
            QueueCall = callEntity,
            QueueCallId = callEntity.Id > 0 ? callEntity.Id : null,
            PbxCallId = callEntity.PbxCallId,
            CorrelationKey = callEntity.CorrelationKey,
            WaitOrder = waitOrder.Value,
            WaitingMs = callEntity.WaitingMs,
            CallerNumber = callEntity.CallerNumber,
            CallerName = callEntity.CallerName,
            EstimatedOrder = workItem.EstimatedWaitOrder
        });
    }

    private void WriteOutboxMessages(
        QueueCallEventEntity inboxEvent,
        QueueCallEntity callEntity,
        QueueCallAggregate aggregate,
        QueueLifecycleWorkItem workItem)
    {
        foreach (var domainEvent in aggregate.DomainEvents)
        {
            var topic = MapOutboxTopic(domainEvent);
            var outboxIdempotencyKey = _idempotencyKeys.CreateOutbox(
                topic,
                inboxEvent.IdempotencyKey,
                domainEvent.EventId.ToString("N"));

            var payload = JsonSerializer.Serialize(new
            {
                queueId = callEntity.QueueId,
                queueCallId = callEntity.Id > 0 ? (long?)callEntity.Id : null,
                callKey = callEntity.CorrelationKey,
                inboxEventId = inboxEvent.Id,
                inboxEventType = inboxEvent.EventType,
                idempotencyKey = outboxIdempotencyKey,
                domainEventType = domainEvent.EventType,
                occurredAtUtc = domainEvent.OccurredAtUtc,
                domainEvent
            });

            _db.OutboxMessages.Add(new OutboxMessageEntity
            {
                Topic = topic,
                PayloadJson = payload
            });
        }
    }

    private async Task<int?> TryResolveQueueSlaThresholdAsync(long queueId, CancellationToken ct)
    {
        return await _db.QueueSettings
            .Where(x => x.QueueId == queueId)
            .Select(x => x.SlaTimeSec)
            .FirstOrDefaultAsync(ct);
    }

    private static TPayload DeserializePayload<TPayload>(QueueCallEventEntity inboxEvent)
        where TPayload : class
    {
        try
        {
            var model = JsonSerializer.Deserialize<TPayload>(inboxEvent.PayloadJson, PayloadJsonOptions);
            return model ?? throw new BadRequestException($"Queue event payload deserialized to null for '{inboxEvent.EventType}'.");
        }
        catch (JsonException ex)
        {
            throw new BadRequestException($"Invalid queue event payload for '{inboxEvent.EventType}': {ex.Message}");
        }
    }

    private static QueueCallTransitionType InferTransitionFromActiveStatus(string? status, bool hasEstablishedAt)
    {
        var normalized = status?.Trim().ToLowerInvariant() ?? string.Empty;
        if (normalized.Contains("ring", StringComparison.Ordinal))
        {
            return QueueCallTransitionType.Ringing;
        }

        if (normalized.Contains("transfer", StringComparison.Ordinal))
        {
            return QueueCallTransitionType.Transferred;
        }

        if (normalized.Contains("talk", StringComparison.Ordinal)
            || normalized.Contains("connect", StringComparison.Ordinal)
            || normalized.Contains("establish", StringComparison.Ordinal))
        {
            return QueueCallTransitionType.Answered;
        }

        if (normalized.Contains("queue", StringComparison.Ordinal)
            || normalized.Contains("wait", StringComparison.Ordinal)
            || normalized.Contains("hold", StringComparison.Ordinal))
        {
            return QueueCallTransitionType.Waiting;
        }

        if (normalized.Contains("end", StringComparison.Ordinal)
            || normalized.Contains("complete", StringComparison.Ordinal))
        {
            return QueueCallTransitionType.Completed;
        }

        return hasEstablishedAt ? QueueCallTransitionType.Answered : QueueCallTransitionType.Waiting;
    }

    private static QueueCallTransitionType InferTerminalTransitionFromCallLog(XapiPbxCallLogDataDto row)
    {
        if (row.Answered == true)
        {
            return QueueCallTransitionType.Completed;
        }

        var combined = $"{row.Status} {row.Reason}".Trim().ToLowerInvariant();
        if (combined.Contains("abandon", StringComparison.Ordinal))
        {
            return QueueCallTransitionType.Abandoned;
        }

        if (combined.Contains("miss", StringComparison.Ordinal)
            || combined.Contains("lost", StringComparison.Ordinal)
            || combined.Contains("unanswer", StringComparison.Ordinal))
        {
            return QueueCallTransitionType.Missed;
        }

        return QueueCallTransitionType.Completed;
    }

    private static string MapOutboxTopic(IQueueDomainEvent domainEvent)
    {
        return domainEvent switch
        {
            QueueCallLifecycleChangedDomainEvent => "queue.call.lifecycle.changed",
            QueueCallAnsweredDomainEvent => "queue.call.answered",
            QueueCallTransferredDomainEvent => "queue.call.transferred",
            QueueCallCompletedDomainEvent => "queue.call.completed",
            QueueCallMarkedForReconciliationDomainEvent => "queue.call.reconciliation.required",
            QueueAgentStatusChangedDomainEvent => "queue.agent.status.changed",
            _ => "queue.domain.event"
        };
    }

    private static DateTimeOffset MaxUtc(params DateTimeOffset[] values)
    {
        var max = DateTimeOffset.MinValue;
        foreach (var value in values)
        {
            if (value > max)
            {
                max = value;
            }
        }

        return max == DateTimeOffset.MinValue ? DateTimeOffset.UtcNow : max;
    }

    private static string? Trim(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

internal sealed class QueueLifecycleWorkItem
{
    public string CorrelationKey { get; set; } = string.Empty;
    public DateTimeOffset EventAtUtc { get; set; }
    public DateTimeOffset ObservedAtUtc { get; set; }
    public long? QueueIdHint { get; set; }
    public long? ExtensionIdHint { get; set; }
    public int? PbxCallId { get; set; }
    public string? CdrId { get; set; }
    public string? CallHistoryId { get; set; }
    public string? MainCallHistoryId { get; set; }
    public int? SegmentId { get; set; }
    public string? CallerNumber { get; set; }
    public string? CallerName { get; set; }
    public string? CalleeNumber { get; set; }
    public string? CalleeName { get; set; }
    public string? Direction { get; set; }
    public string? RawCurrentJson { get; set; }
    public Guid? SnapshotKey { get; set; }
    public int? WaitOrder { get; set; }
    public bool EstimatedWaitOrder { get; set; }
    public long? WaitingMsOverride { get; set; }
    public long? RingingMsOverride { get; set; }
    public long? TalkingMsOverride { get; set; }
    public long? WrapUpMsOverride { get; set; }
    public bool ClearReconciliationMarker { get; set; }
    public List<QueueCallTransitionCommand> Transitions { get; set; } = [];
}
