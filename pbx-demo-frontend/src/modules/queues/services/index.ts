import {
  HubConnectionBuilder,
  HttpTransportType,
  LogLevel,
  HubConnectionState
} from '@microsoft/signalr';
import { buildApiUrl, requestJson, requestNoContent } from '../../../services/httpClient';
import type {
  CreateQueueRequestModel,
  QueueActiveCallsUpdatedMessage,
  QueueAgentStatusChangedMessage,
  QueueAnalyticsComparisonModel,
  QueueAnalyticsOverviewModel,
  QueueAnalyticsQueryModel,
  QueueCallHistoryItemModel,
  QueueCallHistoryQueryModel,
  QueueLiveSnapshotModel,
  QueueListQueryModel,
  QueueModel,
  QueueOutboxPublishResultModel,
  QueuePagedResult,
  QueueSnapshotPublishAcceptedModel,
  QueueStatsUpdatedMessage,
  QueueWaitingListUpdatedMessage,
  UpdateQueueRequestModel
} from '../models/contracts';

export interface QueueApiClient {
  listQueues(query: QueueListQueryModel): Promise<QueuePagedResult<QueueModel>>;
  getQueue(queueId: number): Promise<QueueModel>;
  createQueue(request: CreateQueueRequestModel): Promise<QueueModel>;
  updateQueue(queueId: number, request: UpdateQueueRequestModel): Promise<QueueModel>;
  deleteQueue(queueId: number): Promise<void>;
  getQueueCallHistory(queueId: number, query: QueueCallHistoryQueryModel): Promise<QueuePagedResult<QueueCallHistoryItemModel>>;
  getQueueLiveSnapshot(queueId: number): Promise<QueueLiveSnapshotModel>;
  publishQueueLiveSnapshot(queueId: number): Promise<QueueSnapshotPublishAcceptedModel>;
  publishQueueOutboxBatch(): Promise<QueueOutboxPublishResultModel>;
  getQueueAnalytics(queueId: number, query: QueueAnalyticsQueryModel): Promise<QueueAnalyticsOverviewModel>;
  compareQueues(queueIds: number[], query: QueueAnalyticsQueryModel): Promise<QueueAnalyticsComparisonModel>;
}

export interface QueueSignalRClientOptions {
  forceWebSockets?: boolean;
}

export type QueueSignalRConnectionStatus = 'disconnected' | 'connecting' | 'connected' | 'reconnecting';

export interface QueueSignalRConnectionSnapshot {
  status: QueueSignalRConnectionStatus;
  connectionId?: string | null;
  errorMessage?: string | null;
}

export interface QueueHubEventMap {
  QueueWaitingListUpdated: QueueWaitingListUpdatedMessage;
  QueueActiveCallsUpdated: QueueActiveCallsUpdatedMessage;
  QueueAgentStatusChanged: QueueAgentStatusChangedMessage;
  QueueStatsUpdated: QueueStatsUpdatedMessage;
}

export type QueueHubEventName = keyof QueueHubEventMap;
export type QueueHubEventHandler<TEventName extends QueueHubEventName> = (message: QueueHubEventMap[TEventName]) => void;
export type QueueSignalRConnectionListener = (snapshot: QueueSignalRConnectionSnapshot) => void;

export const QUEUE_CALL_HISTORY_API_TODO_MESSAGE =
  'TODO(BE): Missing queue call-history endpoint. Implement GET /api/queues/{queueId}/history with QueueCallHistoryQuery filters.';

export interface QueueSignalRClient {
  start(): Promise<void>;
  stop(): Promise<void>;
  subscribeQueue(queueId: number): Promise<void>;
  unsubscribeQueue(queueId: number): Promise<void>;
  subscribeDashboard(): Promise<void>;
  unsubscribeDashboard(): Promise<void>;
  requestQueueSnapshot(queueId: number): Promise<void>;
  publishQueueSnapshot(queueId: number): Promise<void>;
  on<TEventName extends QueueHubEventName>(eventName: TEventName, handler: QueueHubEventHandler<TEventName>): () => void;
  onConnection(listener: QueueSignalRConnectionListener): () => void;
  getConnectionSnapshot(): QueueSignalRConnectionSnapshot;
}

type QueryValue = string | number | boolean | null | undefined;

function appendQueryValue(params: URLSearchParams, key: string, value: QueryValue | QueryValue[]): void {
  if (Array.isArray(value)) {
    for (const item of value) {
      appendQueryValue(params, key, item);
    }
    return;
  }

  if (value === null || value === undefined) {
    return;
  }

  if (typeof value === 'string') {
    const trimmed = value.trim();
    if (trimmed.length === 0) {
      return;
    }
    params.append(key, trimmed);
    return;
  }

  params.append(key, String(value));
}

function withQuery(path: string, paramsBuilder: (params: URLSearchParams) => void): string {
  const params = new URLSearchParams();
  paramsBuilder(params);
  const queryString = params.toString();
  if (queryString.length === 0) {
    return path;
  }

  return `${path}?${queryString}`;
}

function buildQueueListPath(query: QueueListQueryModel): string {
  return withQuery('/api/queues', (params) => {
    appendQueryValue(params, 'search', query.search);
    appendQueryValue(params, 'isRegistered', query.isRegistered);
    appendQueryValue(params, 'queueNumber', query.queueNumber);
    appendQueryValue(params, 'page', query.page);
    appendQueryValue(params, 'pageSize', query.pageSize);
    appendQueryValue(params, 'sortBy', query.sortBy);
    appendQueryValue(params, 'sortDescending', query.sortDescending);
  });
}

function buildQueueAnalyticsPath(queueId: number, query: QueueAnalyticsQueryModel): string {
  return withQuery(`/api/queue-analytics/${queueId}`, (params) => {
    appendQueryValue(params, 'fromUtc', query.fromUtc);
    appendQueryValue(params, 'toUtc', query.toUtc);
    appendQueryValue(params, 'bucket', query.bucket);
    appendQueryValue(params, 'slaThresholdSec', query.slaThresholdSec);
    appendQueryValue(params, 'timeZoneId', query.timeZoneId);
  });
}

function buildQueueAnalyticsComparePath(queueIds: number[], query: QueueAnalyticsQueryModel): string {
  return withQuery('/api/queue-analytics/compare', (params) => {
    appendQueryValue(
      params,
      'queueId',
      queueIds.filter((id) => Number.isFinite(id) && id > 0)
    );
    appendQueryValue(params, 'fromUtc', query.fromUtc);
    appendQueryValue(params, 'toUtc', query.toUtc);
    appendQueryValue(params, 'bucket', query.bucket);
    appendQueryValue(params, 'slaThresholdSec', query.slaThresholdSec);
    appendQueryValue(params, 'timeZoneId', query.timeZoneId);
  });
}

function assertPositiveQueueId(queueId: number): void {
  if (!Number.isFinite(queueId) || queueId <= 0) {
    throw new Error('queueId must be a positive number.');
  }
}

function toConnectionStatus(state: HubConnectionState): QueueSignalRConnectionStatus {
  switch (state) {
    case HubConnectionState.Connected:
      return 'connected';
    case HubConnectionState.Connecting:
      return 'connecting';
    case HubConnectionState.Reconnecting:
      return 'reconnecting';
    case HubConnectionState.Disconnected:
    default:
      return 'disconnected';
  }
}

export function createQueueApiClient(accessToken: string): QueueApiClient {
  return {
    listQueues(query) {
      return requestJson<QueuePagedResult<QueueModel>>(buildQueueListPath(query), { method: 'GET' }, accessToken);
    },

    getQueue(queueId) {
      assertPositiveQueueId(queueId);
      return requestJson<QueueModel>(`/api/queues/${queueId}`, { method: 'GET' }, accessToken);
    },

    createQueue(request) {
      return requestJson<QueueModel>(
        '/api/queues',
        {
          method: 'POST',
          body: JSON.stringify(request)
        },
        accessToken
      );
    },

    updateQueue(queueId, request) {
      assertPositiveQueueId(queueId);
      return requestJson<QueueModel>(
        `/api/queues/${queueId}`,
        {
          method: 'PATCH',
          body: JSON.stringify(request)
        },
        accessToken
      );
    },

    deleteQueue(queueId) {
      assertPositiveQueueId(queueId);
      return requestNoContent(`/api/queues/${queueId}`, { method: 'DELETE' }, accessToken);
    },

    async getQueueCallHistory(queueId, _query) {
      assertPositiveQueueId(queueId);
      throw new Error(QUEUE_CALL_HISTORY_API_TODO_MESSAGE);
    },

    getQueueLiveSnapshot(queueId) {
      assertPositiveQueueId(queueId);
      return requestJson<QueueLiveSnapshotModel>(`/api/queue-live/${queueId}/snapshot`, { method: 'GET' }, accessToken);
    },

    publishQueueLiveSnapshot(queueId) {
      assertPositiveQueueId(queueId);
      return requestJson<QueueSnapshotPublishAcceptedModel>(`/api/queue-live/${queueId}/publish`, { method: 'POST' }, accessToken);
    },

    publishQueueOutboxBatch() {
      return requestJson<QueueOutboxPublishResultModel>('/api/queue-live/outbox/publish', { method: 'POST' }, accessToken);
    },

    getQueueAnalytics(queueId, query) {
      assertPositiveQueueId(queueId);
      return requestJson<QueueAnalyticsOverviewModel>(buildQueueAnalyticsPath(queueId, query), { method: 'GET' }, accessToken);
    },

    compareQueues(queueIds, query) {
      return requestJson<QueueAnalyticsComparisonModel>(buildQueueAnalyticsComparePath(queueIds, query), { method: 'GET' }, accessToken);
    }
  };
}

export function createQueueSignalRClient(
  accessToken: string,
  options: QueueSignalRClientOptions = {}
): QueueSignalRClient {
  const transportOptions = options.forceWebSockets
    ? {
        transport: HttpTransportType.WebSockets,
        skipNegotiation: true
      }
    : {};

  const connection = new HubConnectionBuilder()
    .withUrl(buildApiUrl('/hubs/queue'), {
      accessTokenFactory: () => accessToken,
      withCredentials: false,
      ...transportOptions
    })
    .withAutomaticReconnect([0, 1000, 3000, 6000, 10000])
    .configureLogging(LogLevel.Warning)
    .build();

  const eventListeners: {
    [K in QueueHubEventName]: Set<QueueHubEventHandler<K>>;
  } = {
    QueueWaitingListUpdated: new Set(),
    QueueActiveCallsUpdated: new Set(),
    QueueAgentStatusChanged: new Set(),
    QueueStatsUpdated: new Set()
  };

  const connectionListeners = new Set<QueueSignalRConnectionListener>();
  let connectionSnapshot: QueueSignalRConnectionSnapshot = {
    status: 'disconnected',
    connectionId: null,
    errorMessage: null
  };

  const notifyConnection = () => {
    for (const listener of connectionListeners) {
      listener(connectionSnapshot);
    }
  };

  const setConnectionSnapshot = (next: QueueSignalRConnectionSnapshot) => {
    connectionSnapshot = next;
    notifyConnection();
  };

  const dispatch = <TEventName extends QueueHubEventName>(
    eventName: TEventName,
    message: QueueHubEventMap[TEventName]
  ) => {
    for (const listener of eventListeners[eventName]) {
      listener(message);
    }
  };

  connection.on('QueueWaitingListUpdated', (message: QueueWaitingListUpdatedMessage) => {
    dispatch('QueueWaitingListUpdated', message);
  });
  connection.on('QueueActiveCallsUpdated', (message: QueueActiveCallsUpdatedMessage) => {
    dispatch('QueueActiveCallsUpdated', message);
  });
  connection.on('QueueAgentStatusChanged', (message: QueueAgentStatusChangedMessage) => {
    dispatch('QueueAgentStatusChanged', message);
  });
  connection.on('QueueStatsUpdated', (message: QueueStatsUpdatedMessage) => {
    dispatch('QueueStatsUpdated', message);
  });

  connection.onreconnecting((error) => {
    setConnectionSnapshot({
      status: 'reconnecting',
      connectionId: connection.connectionId,
      errorMessage: error instanceof Error ? error.message : null
    });
  });

  connection.onreconnected(() => {
    setConnectionSnapshot({
      status: 'connected',
      connectionId: connection.connectionId,
      errorMessage: null
    });
  });

  connection.onclose((error) => {
    setConnectionSnapshot({
      status: 'disconnected',
      connectionId: connection.connectionId,
      errorMessage: error instanceof Error ? error.message : null
    });
  });

  return {
    async start() {
      if (connection.state === 'Connected' || connection.state === 'Connecting' || connection.state === 'Reconnecting') {
        setConnectionSnapshot({
          status: toConnectionStatus(connection.state),
          connectionId: connection.connectionId,
          errorMessage: null
        });
        return;
      }

      setConnectionSnapshot({
        status: 'connecting',
        connectionId: connection.connectionId,
        errorMessage: null
      });

      try {
        await connection.start();
        setConnectionSnapshot({
          status: 'connected',
          connectionId: connection.connectionId,
          errorMessage: null
        });
      } catch (error) {
        setConnectionSnapshot({
          status: 'disconnected',
          connectionId: connection.connectionId,
          errorMessage: error instanceof Error ? error.message : 'Failed to connect queue hub.'
        });
        throw error;
      }
    },

    async stop() {
      if (connection.state === 'Disconnected') {
        setConnectionSnapshot({
          status: 'disconnected',
          connectionId: connection.connectionId,
          errorMessage: null
        });
        return;
      }

      await connection.stop();
      setConnectionSnapshot({
        status: 'disconnected',
        connectionId: connection.connectionId,
        errorMessage: null
      });
    },

    subscribeQueue(queueId) {
      assertPositiveQueueId(queueId);
      return connection.invoke('SubscribeQueue', queueId);
    },

    unsubscribeQueue(queueId) {
      assertPositiveQueueId(queueId);
      return connection.invoke('UnsubscribeQueue', queueId);
    },

    subscribeDashboard() {
      return connection.invoke('SubscribeDashboard');
    },

    unsubscribeDashboard() {
      return connection.invoke('UnsubscribeDashboard');
    },

    requestQueueSnapshot(queueId) {
      assertPositiveQueueId(queueId);
      return connection.invoke('RequestQueueSnapshot', queueId);
    },

    publishQueueSnapshot(queueId) {
      assertPositiveQueueId(queueId);
      return connection.invoke('PublishQueueSnapshot', queueId);
    },

    on(eventName, handler) {
      const typedSet = eventListeners[eventName] as Set<typeof handler>;
      typedSet.add(handler);
      return () => {
        typedSet.delete(handler);
      };
    },

    onConnection(listener) {
      connectionListeners.add(listener);
      listener(connectionSnapshot);
      return () => {
        connectionListeners.delete(listener);
      };
    },

    getConnectionSnapshot() {
      return connectionSnapshot;
    }
  };
}
