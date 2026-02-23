import { useEffect, useRef } from 'react';
import type {
  CreateQueueRequestModel,
  QueueAnalyticsComparisonModel,
  QueueAnalyticsOverviewModel,
  QueueAnalyticsQueryModel,
  QueueListQueryModel,
  QueueLiveSnapshotModel,
  QueueModel,
  QueueOutboxPublishResultModel,
  QueuePagedResult,
  QueueSnapshotPublishAcceptedModel,
  UpdateQueueRequestModel
} from '../models/contracts';
import {
  createQueueApiClient,
  createQueueSignalRClient,
  type QueueApiClient,
  type QueueSignalRClient,
  type QueueSignalRClientOptions
} from '../services';
import { useQueueStore } from '../store';

function toErrorMessage(error: unknown, fallback: string): string {
  if (error instanceof Error && error.message.trim().length > 0) {
    return error.message;
  }

  return fallback;
}

function useBoundQueueApiClient(accessToken: string): () => QueueApiClient {
  const clientRef = useRef<{ accessToken: string; client: QueueApiClient } | null>(null);

  return () => {
    if (clientRef.current && clientRef.current.accessToken === accessToken) {
      return clientRef.current.client;
    }

    const client = createQueueApiClient(accessToken);
    clientRef.current = { accessToken, client };
    return client;
  };
}

export function useQueueModuleState() {
  return useQueueStore();
}

export function useQueueSelectors() {
  const queueList = useQueueStore((state) => state.queueList);
  const queueListQuery = useQueueStore((state) => state.queueListQuery);
  const activeQueueId = useQueueStore((state) => state.activeQueueId);
  const connection = useQueueStore((state) => state.connection);
  const requests = useQueueStore((state) => state.requests);

  return {
    queueList,
    queueListQuery,
    activeQueueId,
    connection,
    requests
  };
}

export function useQueueActions(accessToken: string) {
  const getApiClient = useBoundQueueApiClient(accessToken);

  const setQueueList = useQueueStore((state) => state.setQueueList);
  const upsertQueue = useQueueStore((state) => state.upsertQueue);
  const removeQueue = useQueueStore((state) => state.removeQueue);
  const setLiveSnapshot = useQueueStore((state) => state.setLiveSnapshot);
  const setQueueAnalytics = useQueueStore((state) => state.setQueueAnalytics);
  const setQueueComparison = useQueueStore((state) => state.setQueueComparison);
  const setRequestLoading = useQueueStore((state) => state.setRequestLoading);
  const setRequestError = useQueueStore((state) => state.setRequestError);

  return {
    async loadQueueList(query: QueueListQueryModel): Promise<QueuePagedResult<QueueModel>> {
      setRequestLoading('queueList', true);
      setRequestError('queueList', null);
      try {
        const result = await getApiClient().listQueues(query);
        setQueueList(result);
        return result;
      } catch (error) {
        const message = toErrorMessage(error, 'Failed to load queue list.');
        setRequestError('queueList', message);
        throw error;
      }
    },

    async loadQueue(queueId: number): Promise<QueueModel> {
      setRequestLoading('queueDetail', true);
      setRequestError('queueDetail', null);
      try {
        const queue = await getApiClient().getQueue(queueId);
        upsertQueue(queue);
        setRequestLoading('queueDetail', false);
        return queue;
      } catch (error) {
        const message = toErrorMessage(error, 'Failed to load queue.');
        setRequestError('queueDetail', message);
        throw error;
      }
    },

    async createQueue(request: CreateQueueRequestModel): Promise<QueueModel> {
      setRequestLoading('queueMutation', true);
      setRequestError('queueMutation', null);
      try {
        const created = await getApiClient().createQueue(request);
        upsertQueue(created);
        setRequestLoading('queueMutation', false);
        return created;
      } catch (error) {
        const message = toErrorMessage(error, 'Failed to create queue.');
        setRequestError('queueMutation', message);
        throw error;
      }
    },

    async updateQueue(queueId: number, request: UpdateQueueRequestModel): Promise<QueueModel> {
      setRequestLoading('queueMutation', true);
      setRequestError('queueMutation', null);
      try {
        const updated = await getApiClient().updateQueue(queueId, request);
        upsertQueue(updated);
        setRequestLoading('queueMutation', false);
        return updated;
      } catch (error) {
        const message = toErrorMessage(error, 'Failed to update queue.');
        setRequestError('queueMutation', message);
        throw error;
      }
    },

    async deleteQueue(queueId: number): Promise<void> {
      setRequestLoading('queueMutation', true);
      setRequestError('queueMutation', null);
      try {
        await getApiClient().deleteQueue(queueId);
        removeQueue(queueId);
        setRequestLoading('queueMutation', false);
      } catch (error) {
        const message = toErrorMessage(error, 'Failed to delete queue.');
        setRequestError('queueMutation', message);
        throw error;
      }
    },

    async loadQueueLiveSnapshot(queueId: number): Promise<QueueLiveSnapshotModel> {
      setRequestLoading('queueLive', true);
      setRequestError('queueLive', null);
      try {
        const snapshot = await getApiClient().getQueueLiveSnapshot(queueId);
        setLiveSnapshot(snapshot);
        return snapshot;
      } catch (error) {
        const message = toErrorMessage(error, 'Failed to load queue live snapshot.');
        setRequestError('queueLive', message);
        throw error;
      }
    },

    async publishQueueLiveSnapshot(queueId: number): Promise<QueueSnapshotPublishAcceptedModel> {
      setRequestLoading('queueLive', true);
      setRequestError('queueLive', null);
      try {
        const response = await getApiClient().publishQueueLiveSnapshot(queueId);
        setRequestLoading('queueLive', false);
        return response;
      } catch (error) {
        const message = toErrorMessage(error, 'Failed to publish queue live snapshot.');
        setRequestError('queueLive', message);
        throw error;
      }
    },

    async publishQueueOutboxBatch(): Promise<QueueOutboxPublishResultModel> {
      setRequestLoading('queueLive', true);
      setRequestError('queueLive', null);
      try {
        const response = await getApiClient().publishQueueOutboxBatch();
        setRequestLoading('queueLive', false);
        return response;
      } catch (error) {
        const message = toErrorMessage(error, 'Failed to publish queue outbox batch.');
        setRequestError('queueLive', message);
        throw error;
      }
    },

    async loadQueueAnalytics(queueId: number, query: QueueAnalyticsQueryModel): Promise<QueueAnalyticsOverviewModel> {
      setRequestLoading('queueAnalytics', true);
      setRequestError('queueAnalytics', null);
      try {
        const result = await getApiClient().getQueueAnalytics(queueId, query);
        setQueueAnalytics(result);
        return result;
      } catch (error) {
        const message = toErrorMessage(error, 'Failed to load queue analytics.');
        setRequestError('queueAnalytics', message);
        throw error;
      }
    },

    async loadQueueComparison(queueIds: number[], query: QueueAnalyticsQueryModel): Promise<QueueAnalyticsComparisonModel> {
      setRequestLoading('queueComparison', true);
      setRequestError('queueComparison', null);
      try {
        const result = await getApiClient().compareQueues(queueIds, query);
        setQueueComparison(result);
        return result;
      } catch (error) {
        const message = toErrorMessage(error, 'Failed to load queue comparison analytics.');
        setRequestError('queueComparison', message);
        throw error;
      }
    }
  };
}

export interface UseQueueSignalRBridgeOptions extends QueueSignalRClientOptions {
  enabled?: boolean;
  autoStart?: boolean;
}

export interface QueueSignalRBridgeHandle {
  client: QueueSignalRClient | null;
  connection: ReturnType<typeof useQueueStore.getState>['connection'];
}

export function useQueueSignalRBridge(
  accessToken: string | null | undefined,
  options: UseQueueSignalRBridgeOptions = {}
): QueueSignalRBridgeHandle {
  const connection = useQueueStore((state) => state.connection);
  const setConnection = useQueueStore((state) => state.setConnection);
  const applyQueueWaitingListUpdated = useQueueStore((state) => state.applyQueueWaitingListUpdated);
  const applyQueueActiveCallsUpdated = useQueueStore((state) => state.applyQueueActiveCallsUpdated);
  const applyQueueAgentStatusChanged = useQueueStore((state) => state.applyQueueAgentStatusChanged);
  const applyQueueStatsUpdated = useQueueStore((state) => state.applyQueueStatsUpdated);

  const clientRef = useRef<QueueSignalRClient | null>(null);
  const tokenRef = useRef<string | null>(null);

  useEffect(() => {
    const enabled = options.enabled ?? true;
    if (!enabled || !accessToken) {
      if (clientRef.current) {
        const closingClient = clientRef.current;
        clientRef.current = null;
        tokenRef.current = null;
        void closingClient.stop().catch(() => undefined);
      }
      setConnection({
        status: 'disconnected',
        connectionId: null,
        errorMessage: null
      });
      return;
    }

    const tokenChanged = tokenRef.current !== accessToken;
    if (!clientRef.current || tokenChanged) {
      if (clientRef.current) {
        const oldClient = clientRef.current;
        void oldClient.stop().catch(() => undefined);
      }

      const nextClient = createQueueSignalRClient(accessToken, { forceWebSockets: options.forceWebSockets });
      tokenRef.current = accessToken;
      clientRef.current = nextClient;

      const unsubs = [
        nextClient.on('QueueWaitingListUpdated', (message) => {
          applyQueueWaitingListUpdated(message);
        }),
        nextClient.on('QueueActiveCallsUpdated', (message) => {
          applyQueueActiveCallsUpdated(message);
        }),
        nextClient.on('QueueAgentStatusChanged', (message) => {
          applyQueueAgentStatusChanged(message);
        }),
        nextClient.on('QueueStatsUpdated', (message) => {
          applyQueueStatsUpdated(message);
        }),
        nextClient.onConnection((snapshot) => {
          setConnection(snapshot);
        })
      ];

      if (options.autoStart ?? true) {
        void nextClient.start().catch((error) => {
          setConnection({
            status: 'disconnected',
            connectionId: null,
            errorMessage: toErrorMessage(error, 'Queue SignalR connection failed.')
          });
        });
      }

      return () => {
        for (const unsub of unsubs) {
          unsub();
        }
      };
    }

    if ((options.autoStart ?? true) && clientRef.current) {
      void clientRef.current.start().catch((error) => {
        setConnection({
          status: 'disconnected',
          connectionId: null,
          errorMessage: toErrorMessage(error, 'Queue SignalR connection failed.')
        });
      });
    }

    return;
  }, [
    accessToken,
    options.autoStart,
    options.enabled,
    options.forceWebSockets,
    applyQueueActiveCallsUpdated,
    applyQueueAgentStatusChanged,
    applyQueueStatsUpdated,
    applyQueueWaitingListUpdated,
    setConnection
  ]);

  useEffect(() => {
    return () => {
      if (clientRef.current) {
        const client = clientRef.current;
        clientRef.current = null;
        tokenRef.current = null;
        void client.stop().catch(() => undefined);
      }
    };
  }, []);

  return {
    client: clientRef.current,
    connection
  };
}

export interface UseQueueSubscriptionOptions {
  enabled?: boolean;
  requestSnapshotOnSubscribe?: boolean;
}

export function useQueueLiveSubscription(
  client: QueueSignalRClient | null,
  queueId: number | null | undefined,
  options: UseQueueSubscriptionOptions = {}
): void {
  const markQueueSubscribed = useQueueStore((state) => state.markQueueSubscribed);
  const markQueueUnsubscribed = useQueueStore((state) => state.markQueueUnsubscribed);
  const setRequestError = useQueueStore((state) => state.setRequestError);

  useEffect(() => {
    const enabled = options.enabled ?? true;
    if (!client || !enabled || !queueId || queueId <= 0) {
      return;
    }

    let mounted = true;

    const subscribe = async () => {
      try {
        await client.start();
        await client.subscribeQueue(queueId);
        markQueueSubscribed(queueId);

        if (options.requestSnapshotOnSubscribe ?? true) {
          await client.requestQueueSnapshot(queueId);
        }
      } catch (error) {
        if (!mounted) {
          return;
        }

        setRequestError('queueSignalR', toErrorMessage(error, `Failed to subscribe queue ${queueId}.`));
      }
    };

    void subscribe();

    return () => {
      mounted = false;
      markQueueUnsubscribed(queueId);
      void client.unsubscribeQueue(queueId).catch(() => undefined);
    };
  }, [
    client,
    markQueueSubscribed,
    markQueueUnsubscribed,
    options.enabled,
    options.requestSnapshotOnSubscribe,
    queueId,
    setRequestError
  ]);
}

export function useQueueDashboardSubscription(
  client: QueueSignalRClient | null,
  enabled = true
): void {
  const setDashboardSubscribed = useQueueStore((state) => state.setDashboardSubscribed);
  const setRequestError = useQueueStore((state) => state.setRequestError);

  useEffect(() => {
    if (!client || !enabled) {
      return;
    }

    let mounted = true;

    const subscribe = async () => {
      try {
        await client.start();
        await client.subscribeDashboard();
        setDashboardSubscribed(true);
      } catch (error) {
        if (!mounted) {
          return;
        }

        setRequestError('queueSignalR', toErrorMessage(error, 'Failed to subscribe queue dashboard.'));
      }
    };

    void subscribe();

    return () => {
      mounted = false;
      setDashboardSubscribed(false);
      void client.unsubscribeDashboard().catch(() => undefined);
    };
  }, [client, enabled, setDashboardSubscribed, setRequestError]);
}
