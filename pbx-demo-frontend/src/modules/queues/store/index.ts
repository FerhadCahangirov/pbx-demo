import { create } from 'zustand';
import type {
  QueueActiveCallsUpdatedMessage,
  QueueAgentLiveStatusModel,
  QueueAgentStatusChangedMessage,
  QueueAnalyticsComparisonModel,
  QueueAnalyticsOverviewModel,
  QueueListQueryModel,
  QueueLiveSnapshotModel,
  QueueModel,
  QueuePagedResult,
  QueueStatsSummaryModel,
  QueueStatsUpdatedMessage,
  QueueWaitingListUpdatedMessage
} from '../models/contracts';
import type { QueueSignalRConnectionSnapshot } from '../services';

export type QueueRequestScope =
  | 'queueList'
  | 'queueDetail'
  | 'queueMutation'
  | 'queueLive'
  | 'queueAnalytics'
  | 'queueComparison'
  | 'queueSignalR';

export interface QueueRequestState {
  loading: boolean;
  errorMessage: string | null;
  lastSuccessAtUtc?: string | null;
}

export interface QueueModuleState {
  queueListQuery: QueueListQueryModel;
  queueList: QueuePagedResult<QueueModel> | null;
  queuesById: Record<number, QueueModel>;
  liveSnapshotsByQueueId: Record<number, QueueLiveSnapshotModel>;
  analyticsByQueueId: Record<number, QueueAnalyticsOverviewModel>;
  comparison: QueueAnalyticsComparisonModel | null;
  dashboardAgentStatuses: QueueAgentLiveStatusModel[];
  activeQueueId: number | null;
  subscribedQueueIds: number[];
  isDashboardSubscribed: boolean;
  connection: QueueSignalRConnectionSnapshot;
  requests: Record<QueueRequestScope, QueueRequestState>;
  setQueueListQuery(query: Partial<QueueListQueryModel>): void;
  setActiveQueueId(queueId: number | null): void;
  setQueueList(result: QueuePagedResult<QueueModel>): void;
  upsertQueue(queue: QueueModel): void;
  removeQueue(queueId: number): void;
  setLiveSnapshot(snapshot: QueueLiveSnapshotModel): void;
  setQueueAnalytics(overview: QueueAnalyticsOverviewModel): void;
  setQueueComparison(comparison: QueueAnalyticsComparisonModel | null): void;
  setRequestLoading(scope: QueueRequestScope, loading: boolean): void;
  setRequestError(scope: QueueRequestScope, errorMessage: string | null): void;
  setConnection(snapshot: QueueSignalRConnectionSnapshot): void;
  markQueueSubscribed(queueId: number): void;
  markQueueUnsubscribed(queueId: number): void;
  setDashboardSubscribed(isSubscribed: boolean): void;
  applyQueueWaitingListUpdated(message: QueueWaitingListUpdatedMessage): void;
  applyQueueActiveCallsUpdated(message: QueueActiveCallsUpdatedMessage): void;
  applyQueueAgentStatusChanged(message: QueueAgentStatusChangedMessage): void;
  applyQueueStatsUpdated(message: QueueStatsUpdatedMessage): void;
  reset(): void;
}

const DEFAULT_QUEUE_LIST_QUERY: QueueListQueryModel = {
  page: 1,
  pageSize: 20,
  search: null,
  isRegistered: null,
  queueNumber: null,
  sortBy: 'name',
  sortDescending: false
};

const DEFAULT_CONNECTION: QueueSignalRConnectionSnapshot = {
  status: 'disconnected',
  connectionId: null,
  errorMessage: null
};

function createRequestState(): Record<QueueRequestScope, QueueRequestState> {
  return {
    queueList: { loading: false, errorMessage: null, lastSuccessAtUtc: null },
    queueDetail: { loading: false, errorMessage: null, lastSuccessAtUtc: null },
    queueMutation: { loading: false, errorMessage: null, lastSuccessAtUtc: null },
    queueLive: { loading: false, errorMessage: null, lastSuccessAtUtc: null },
    queueAnalytics: { loading: false, errorMessage: null, lastSuccessAtUtc: null },
    queueComparison: { loading: false, errorMessage: null, lastSuccessAtUtc: null },
    queueSignalR: { loading: false, errorMessage: null, lastSuccessAtUtc: null }
  };
}

function createInitialState(): Omit<
  QueueModuleState,
  | 'setQueueListQuery'
  | 'setActiveQueueId'
  | 'setQueueList'
  | 'upsertQueue'
  | 'removeQueue'
  | 'setLiveSnapshot'
  | 'setQueueAnalytics'
  | 'setQueueComparison'
  | 'setRequestLoading'
  | 'setRequestError'
  | 'setConnection'
  | 'markQueueSubscribed'
  | 'markQueueUnsubscribed'
  | 'setDashboardSubscribed'
  | 'applyQueueWaitingListUpdated'
  | 'applyQueueActiveCallsUpdated'
  | 'applyQueueAgentStatusChanged'
  | 'applyQueueStatsUpdated'
  | 'reset'
> {
  return {
    queueListQuery: { ...DEFAULT_QUEUE_LIST_QUERY },
    queueList: null,
    queuesById: {},
    liveSnapshotsByQueueId: {},
    analyticsByQueueId: {},
    comparison: null,
    dashboardAgentStatuses: [],
    activeQueueId: null,
    subscribedQueueIds: [],
    isDashboardSubscribed: false,
    connection: { ...DEFAULT_CONNECTION },
    requests: createRequestState()
  };
}

function nowUtcIso(): string {
  return new Date().toISOString();
}

function mergeQueuesById(previous: Record<number, QueueModel>, items: QueueModel[]): Record<number, QueueModel> {
  const next = { ...previous };
  for (const item of items) {
    next[item.id] = item;
  }
  return next;
}

function upsertAgentStatus(
  collection: QueueAgentLiveStatusModel[],
  message: QueueAgentStatusChangedMessage
): QueueAgentLiveStatusModel[] {
  const nextStatus: QueueAgentLiveStatusModel = {
    agentId: message.agentId,
    extensionNumber: message.extensionNumber,
    displayName: null,
    queueStatus: message.queueStatus,
    activityType: message.activityType,
    currentCallKey: message.currentCallKey ?? null,
    atUtc: message.atUtc
  };

  const existingIndex = collection.findIndex((item) => item.agentId === message.agentId);
  if (existingIndex < 0) {
    return [nextStatus, ...collection].sort(compareAgentStatusDesc);
  }

  const next = [...collection];
  next[existingIndex] = {
    ...next[existingIndex],
    ...nextStatus,
    displayName: next[existingIndex].displayName ?? null
  };
  next.sort(compareAgentStatusDesc);
  return next;
}

function compareAgentStatusDesc(left: QueueAgentLiveStatusModel, right: QueueAgentLiveStatusModel): number {
  const rightTime = Date.parse(right.atUtc);
  const leftTime = Date.parse(left.atUtc);
  if (Number.isFinite(rightTime) && Number.isFinite(leftTime) && rightTime !== leftTime) {
    return rightTime - leftTime;
  }

  return left.agentId - right.agentId;
}

function patchSnapshotStats(snapshot: QueueLiveSnapshotModel, stats: QueueStatsSummaryModel, asOfUtc: string): QueueLiveSnapshotModel {
  return {
    ...snapshot,
    asOfUtc,
    stats
  };
}

function patchSnapshotWaiting(snapshot: QueueLiveSnapshotModel, message: QueueWaitingListUpdatedMessage): QueueLiveSnapshotModel {
  return {
    ...snapshot,
    asOfUtc: message.asOfUtc,
    version: Math.max(snapshot.version, message.version),
    waitingCalls: message.waitingCalls
  };
}

function patchSnapshotActive(snapshot: QueueLiveSnapshotModel, message: QueueActiveCallsUpdatedMessage): QueueLiveSnapshotModel {
  return {
    ...snapshot,
    asOfUtc: message.asOfUtc,
    version: Math.max(snapshot.version, message.version),
    activeCalls: message.activeCalls
  };
}

function patchSnapshotAgentStatus(snapshot: QueueLiveSnapshotModel, message: QueueAgentStatusChangedMessage): QueueLiveSnapshotModel {
  return {
    ...snapshot,
    agentStatuses: upsertAgentStatus(snapshot.agentStatuses, message)
  };
}

function createEmptySnapshot(queueId: number, asOfUtc: string, version = 0): QueueLiveSnapshotModel {
  return {
    queueId,
    asOfUtc,
    version,
    waitingCalls: [],
    activeCalls: [],
    agentStatuses: [],
    stats: {
      queueId,
      asOfUtc,
      waitingCount: 0,
      activeCount: 0,
      loggedInAgents: 0,
      availableAgents: 0,
      averageWaitingMs: null,
      slaPct: null,
      answeredCount: 0,
      abandonedCount: 0
    }
  };
}

export const useQueueStore = create<QueueModuleState>((set) => ({
  ...createInitialState(),

  setQueueListQuery(query) {
    set((state) => ({
      queueListQuery: {
        ...state.queueListQuery,
        ...query
      }
    }));
  },

  setActiveQueueId(queueId) {
    set({ activeQueueId: queueId });
  },

  setQueueList(result) {
    set((state) => ({
      queueList: result,
      queuesById: mergeQueuesById(state.queuesById, result.items),
      requests: {
        ...state.requests,
        queueList: {
          loading: false,
          errorMessage: null,
          lastSuccessAtUtc: nowUtcIso()
        }
      }
    }));
  },

  upsertQueue(queue) {
    set((state) => {
      const currentItems = state.queueList?.items ?? [];
      const existingIndex = currentItems.findIndex((item) => item.id === queue.id);
      const nextItems =
        state.queueList === null
          ? null
          : existingIndex < 0
            ? [queue, ...currentItems]
            : currentItems.map((item) => (item.id === queue.id ? queue : item));

      return {
        queuesById: {
          ...state.queuesById,
          [queue.id]: queue
        },
        queueList:
          nextItems === null
            ? state.queueList
            : {
                ...state.queueList,
                items: nextItems
              }
      };
    });
  },

  removeQueue(queueId) {
    set((state) => {
      const nextQueuesById = { ...state.queuesById };
      delete nextQueuesById[queueId];

      const nextQueueList =
        state.queueList === null
          ? null
          : {
              ...state.queueList,
              items: state.queueList.items.filter((item) => item.id !== queueId),
              totalCount:
                typeof state.queueList.totalCount === 'number'
                  ? Math.max(0, state.queueList.totalCount - (state.queueList.items.some((item) => item.id === queueId) ? 1 : 0))
                  : state.queueList.totalCount
            };

      const nextSnapshots = { ...state.liveSnapshotsByQueueId };
      delete nextSnapshots[queueId];

      const nextAnalytics = { ...state.analyticsByQueueId };
      delete nextAnalytics[queueId];

      return {
        queuesById: nextQueuesById,
        queueList: nextQueueList,
        liveSnapshotsByQueueId: nextSnapshots,
        analyticsByQueueId: nextAnalytics,
        subscribedQueueIds: state.subscribedQueueIds.filter((id) => id !== queueId),
        activeQueueId: state.activeQueueId === queueId ? null : state.activeQueueId
      };
    });
  },

  setLiveSnapshot(snapshot) {
    set((state) => ({
      liveSnapshotsByQueueId: {
        ...state.liveSnapshotsByQueueId,
        [snapshot.queueId]: snapshot
      },
      dashboardAgentStatuses: mergeDashboardStatuses(state.dashboardAgentStatuses, snapshot.agentStatuses),
      requests: {
        ...state.requests,
        queueLive: {
          loading: false,
          errorMessage: null,
          lastSuccessAtUtc: nowUtcIso()
        }
      }
    }));
  },

  setQueueAnalytics(overview) {
    set((state) => ({
      analyticsByQueueId: {
        ...state.analyticsByQueueId,
        [overview.queueId]: overview
      },
      requests: {
        ...state.requests,
        queueAnalytics: {
          loading: false,
          errorMessage: null,
          lastSuccessAtUtc: nowUtcIso()
        }
      }
    }));
  },

  setQueueComparison(comparison) {
    set((state) => ({
      comparison,
      requests: {
        ...state.requests,
        queueComparison: {
          loading: false,
          errorMessage: null,
          lastSuccessAtUtc: comparison ? nowUtcIso() : state.requests.queueComparison.lastSuccessAtUtc ?? null
        }
      }
    }));
  },

  setRequestLoading(scope, loading) {
    set((state) => ({
      requests: {
        ...state.requests,
        [scope]: {
          ...state.requests[scope],
          loading
        }
      }
    }));
  },

  setRequestError(scope, errorMessage) {
    set((state) => ({
      requests: {
        ...state.requests,
        [scope]: {
          ...state.requests[scope],
          loading: false,
          errorMessage
        }
      }
    }));
  },

  setConnection(snapshot) {
    set((state) => ({
      connection: snapshot,
      requests: {
        ...state.requests,
        queueSignalR: {
          loading: snapshot.status === 'connecting' || snapshot.status === 'reconnecting',
          errorMessage: snapshot.errorMessage ?? null,
          lastSuccessAtUtc:
            snapshot.status === 'connected'
              ? nowUtcIso()
              : state.requests.queueSignalR.lastSuccessAtUtc ?? null
        }
      }
    }));
  },

  markQueueSubscribed(queueId) {
    if (!Number.isFinite(queueId) || queueId <= 0) {
      return;
    }

    set((state) => ({
      subscribedQueueIds: state.subscribedQueueIds.includes(queueId)
        ? state.subscribedQueueIds
        : [...state.subscribedQueueIds, queueId].sort((a, b) => a - b)
    }));
  },

  markQueueUnsubscribed(queueId) {
    set((state) => ({
      subscribedQueueIds: state.subscribedQueueIds.filter((id) => id !== queueId)
    }));
  },

  setDashboardSubscribed(isDashboardSubscribed) {
    set({ isDashboardSubscribed });
  },

  applyQueueWaitingListUpdated(message) {
    set((state) => {
      const current = state.liveSnapshotsByQueueId[message.queueId]
        ?? createEmptySnapshot(message.queueId, message.asOfUtc, message.version);

      return {
        liveSnapshotsByQueueId: {
          ...state.liveSnapshotsByQueueId,
          [message.queueId]: patchSnapshotWaiting(current, message)
        }
      };
    });
  },

  applyQueueActiveCallsUpdated(message) {
    set((state) => {
      const current = state.liveSnapshotsByQueueId[message.queueId]
        ?? createEmptySnapshot(message.queueId, message.asOfUtc, message.version);

      return {
        liveSnapshotsByQueueId: {
          ...state.liveSnapshotsByQueueId,
          [message.queueId]: patchSnapshotActive(current, message)
        }
      };
    });
  },

  applyQueueAgentStatusChanged(message) {
    set((state) => {
      const queueId = message.queueId ?? null;
      const nextSnapshots =
        queueId && state.liveSnapshotsByQueueId[queueId]
          ? {
              ...state.liveSnapshotsByQueueId,
              [queueId]: patchSnapshotAgentStatus(state.liveSnapshotsByQueueId[queueId], message)
            }
          : queueId
            ? {
                ...state.liveSnapshotsByQueueId,
                [queueId]: patchSnapshotAgentStatus(createEmptySnapshot(queueId, message.atUtc, 0), message)
            }
          : state.liveSnapshotsByQueueId;

      return {
        liveSnapshotsByQueueId: nextSnapshots,
        dashboardAgentStatuses: upsertAgentStatus(state.dashboardAgentStatuses, message)
      };
    });
  },

  applyQueueStatsUpdated(message) {
    set((state) => {
      const current = state.liveSnapshotsByQueueId[message.queueId]
        ?? createEmptySnapshot(message.queueId, message.asOfUtc, 0);

      return {
        liveSnapshotsByQueueId: {
          ...state.liveSnapshotsByQueueId,
          [message.queueId]: patchSnapshotStats(current, message.stats, message.asOfUtc)
        }
      };
    });
  },

  reset() {
    set(createInitialState());
  }
}));

function mergeDashboardStatuses(
  previous: QueueAgentLiveStatusModel[],
  incoming: QueueAgentLiveStatusModel[]
): QueueAgentLiveStatusModel[] {
  let next = [...previous];
  for (const status of incoming) {
    const existingIndex = next.findIndex((item) => item.agentId === status.agentId);
    if (existingIndex < 0) {
      next.push(status);
      continue;
    }

    next[existingIndex] = {
      ...next[existingIndex],
      ...status
    };
  }

  next = next.sort(compareAgentStatusDesc);
  return next;
}

export const queueStoreDefaults = {
  queueListQuery: DEFAULT_QUEUE_LIST_QUERY
} as const;
