import { useEffect, useMemo, useState } from 'react';
import {
  QueueDataTable,
  QueueKpiGrid,
  QueueNoticeStack,
  QueuePanel,
  formatQueueDurationMs,
  formatQueuePercent,
  formatQueueUtc
} from '../components';
import { useQueueActions, useQueueLiveSubscription, useQueueSignalRBridge } from '../hooks';
import { useQueueStore } from '../store';

interface QueueLiveMonitoringPageProps {
  accessToken: string;
  queueId: number;
  onSelectQueue?: (queueId: number) => void;
}

function toErrorMessage(error: unknown, fallback: string): string {
  if (error instanceof Error && error.message.trim().length > 0) {
    return error.message;
  }

  return fallback;
}

export function QueueLiveMonitoringPage({ accessToken, queueId, onSelectQueue }: QueueLiveMonitoringPageProps) {
  const actions = useQueueActions(accessToken);
  const { client, connection } = useQueueSignalRBridge(accessToken, { enabled: true, autoStart: true });
  useQueueLiveSubscription(client, queueId, { enabled: queueId > 0, requestSnapshotOnSubscribe: true });

  const queueList = useQueueStore((state) => state.queueList);
  const queue = useQueueStore((state) => state.queuesById[queueId]);
  const snapshot = useQueueStore((state) => state.liveSnapshotsByQueueId[queueId]);
  const requests = useQueueStore((state) => state.requests);

  const [pageError, setPageError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  useEffect(() => {
    let mounted = true;
    const loadQueues = async () => {
      try {
        await actions.loadQueueList({
          page: 1,
          pageSize: 200,
          search: null,
          isRegistered: null,
          queueNumber: null,
          sortBy: 'name',
          sortDescending: false
        });
      } catch (error) {
        if (mounted) {
          setPageError(toErrorMessage(error, 'Failed to load queue list.'));
        }
      }
    };

    void loadQueues();
    return () => {
      mounted = false;
    };
  }, [accessToken]);

  useEffect(() => {
    let mounted = true;
    const loadQueueState = async () => {
      if (!queueId || queueId <= 0) {
        setPageError('Choose a valid queue.');
        return;
      }

      try {
        await Promise.all([actions.loadQueue(queueId), actions.loadQueueLiveSnapshot(queueId)]);
        if (mounted) {
          setPageError(null);
        }
      } catch (error) {
        if (mounted) {
          setPageError(toErrorMessage(error, `Failed to load live snapshot for queue ${queueId}.`));
        }
      }
    };

    void loadQueueState();
    return () => {
      mounted = false;
    };
  }, [accessToken, queueId]);

  useEffect(() => {
    if (!notice) {
      return;
    }
    const timer = window.setTimeout(() => setNotice(null), 2800);
    return () => window.clearTimeout(timer);
  }, [notice]);

  const notices = useMemo(() => {
    const items: Array<{ id: string; tone: 'error' | 'success' | 'warning'; message: string }> = [];
    if (pageError) {
      items.push({ id: 'page-error', tone: 'error', message: pageError });
    }
    if (requests.queueLive.errorMessage && requests.queueLive.errorMessage !== pageError) {
      items.push({ id: 'queue-live-error', tone: 'error', message: requests.queueLive.errorMessage });
    }
    if (requests.queueSignalR.errorMessage) {
      items.push({ id: 'signalr-error', tone: 'error', message: requests.queueSignalR.errorMessage });
    }
    if (notice) {
      items.push({ id: 'notice', tone: 'success', message: notice });
    }
    items.push({
      id: 'active-call-caller-todo',
      tone: 'warning',
      message: 'TODO(BE): Queue active-call live DTO does not expose caller name/number yet, so active call rows show call key/PBX call id only.'
    });
    return items;
  }, [notice, pageError, requests.queueLive.errorMessage, requests.queueSignalR.errorMessage]);

  const kpiItems = snapshot
    ? [
        { id: 'waiting', label: 'Waiting Calls', value: snapshot.stats.waitingCount, tone: snapshot.stats.waitingCount > 0 ? 'warning' as const : 'active' as const },
        { id: 'active', label: 'Active Calls', value: snapshot.stats.activeCount, tone: snapshot.stats.activeCount > 0 ? 'active' as const : 'info' as const },
        { id: 'agents-logged', label: 'Logged In Agents', value: snapshot.stats.loggedInAgents },
        { id: 'agents-available', label: 'Available Agents', value: snapshot.stats.availableAgents },
        { id: 'avg-wait', label: 'Average Waiting', value: formatQueueDurationMs(snapshot.stats.averageWaitingMs) },
        { id: 'sla', label: 'SLA %', value: formatQueuePercent(snapshot.stats.slaPct) },
        { id: 'answered', label: 'Answered Count', value: snapshot.stats.answeredCount },
        { id: 'abandoned', label: 'Abandoned Count', value: snapshot.stats.abandonedCount, tone: snapshot.stats.abandonedCount > 0 ? 'critical' as const : 'info' as const }
      ]
    : [];

  const refreshSnapshot = async () => {
    setPageError(null);
    try {
      await actions.loadQueueLiveSnapshot(queueId);
      setNotice('Live snapshot refreshed.');
    } catch (error) {
      setPageError(toErrorMessage(error, 'Failed to refresh live snapshot.'));
    }
  };

  const requestSnapshotViaHub = async () => {
    setPageError(null);
    if (!client) {
      setPageError('Queue hub client is not ready.');
      return;
    }

    try {
      await client.start();
      await client.requestQueueSnapshot(queueId);
      setNotice('Snapshot requested via Queue Hub.');
    } catch (error) {
      setPageError(toErrorMessage(error, 'Queue hub snapshot request failed.'));
    }
  };

  const publishSnapshot = async () => {
    setPageError(null);
    try {
      await actions.publishQueueLiveSnapshot(queueId);
      setNotice('Snapshot publish requested.');
    } catch (error) {
      setPageError(toErrorMessage(error, 'Snapshot publish failed (check Supervisor policy/auth).'));
    }
  };

  return (
    <section className="supervisor-page-stack">
      <QueuePanel title="Queues / Live Monitoring" subtitle="Realtime queue calls and agent activity from queue snapshot + QueueHub events.">
        <div className="form-grid">
          <div className="grid-three">
            <div>
              <label className="label" htmlFor="queue-live-select">Queue</label>
              <select
                id="queue-live-select"
                className="select"
                value={queueId > 0 ? String(queueId) : ''}
                onChange={(event) => {
                  const nextId = Number(event.target.value);
                  if (Number.isFinite(nextId) && nextId > 0) {
                    onSelectQueue?.(nextId);
                  }
                }}
              >
                {(queueList?.items ?? []).map((item) => (
                  <option key={item.id} value={item.id}>
                    {item.queueNumber} - {item.name}
                  </option>
                ))}
              </select>
            </div>

            <div className="status-strip">
              <span className={`status-chip ${connection.status === 'connected' ? 'status-active' : 'status-waiting'}`}>
                Queue Hub: {connection.status}
              </span>
              <span className={`status-chip ${requests.queueLive.loading ? 'status-waiting' : 'status-info'}`}>
                Snapshot: {requests.queueLive.loading ? 'Loading' : 'Ready'}
              </span>
            </div>

            <div className="text-sm text-muted">
              {snapshot ? `As of ${formatQueueUtc(snapshot.asOfUtc)} (v${snapshot.version})` : 'No snapshot loaded'}
            </div>
          </div>

          <div className="grid-three">
            <button className="primary-button" type="button" onClick={() => void refreshSnapshot()} disabled={requests.queueLive.loading}>
              Refresh Snapshot
            </button>
            <button className="secondary-button" type="button" onClick={() => void requestSnapshotViaHub()}>
              Request via Hub
            </button>
            <button className="secondary-button" type="button" onClick={() => void publishSnapshot()} disabled={requests.queueLive.loading}>
              Publish Snapshot
            </button>
          </div>

          <QueueNoticeStack notices={notices} />
        </div>
      </QueuePanel>

      <QueuePanel
        title="Live KPI Snapshot"
        subtitle={queue ? `Queue ${queue.queueNumber} - ${queue.name}` : 'Current queue statistics from live snapshot'}
      >
        {snapshot ? <QueueKpiGrid items={kpiItems} columns={4} /> : <div className="text-sm text-muted">No live snapshot available yet.</div>}
      </QueuePanel>

      <QueuePanel title="Active Calls" subtitle="Active queue calls updated by Queue Hub events and snapshot refreshes.">
        <QueueDataTable
          rows={snapshot?.activeCalls ?? []}
          rowKey={(item) => item.callKey}
          emptyMessage="No active queue calls."
          columns={[
            { key: 'callKey', header: 'Call Key', cell: (item) => <span className="font-mono text-xs">{item.callKey}</span> },
            { key: 'status', header: 'Status', cell: (item) => item.status },
            { key: 'caller', header: 'PBX Call ID', cell: (item) => item.pbxCallId ?? 'N/A' },
            { key: 'agent', header: 'Agent', cell: (item) => item.agentExtension ?? 'Unassigned' },
            { key: 'talking', header: 'Call Duration', cell: (item) => formatQueueDurationMs(item.talkingMs) }
          ]}
        />
      </QueuePanel>

      <QueuePanel title="Waiting Calls" subtitle="Queue callers currently waiting in order.">
        <QueueDataTable
          rows={snapshot?.waitingCalls ?? []}
          rowKey={(item) => item.callKey}
          emptyMessage="No waiting calls."
          columns={[
            { key: 'order', header: 'Order', cell: (item) => item.waitOrder },
            { key: 'caller', header: 'Caller', cell: (item) => `${item.callerNumber ?? 'Unknown'}${item.callerName ? ` - ${item.callerName}` : ''}` },
            { key: 'queueCallId', header: 'Queue Call ID', cell: (item) => item.queueCallId ?? 'N/A' },
            { key: 'wait', header: 'Wait Duration', cell: (item) => formatQueueDurationMs(item.waitingMs) },
            { key: 'estimated', header: 'Estimated Order', cell: (item) => (item.estimatedOrder ? 'Yes' : 'No') }
          ]}
        />
      </QueuePanel>

      <QueuePanel title="Agent Statuses" subtitle="Latest per-agent activity delivered through Queue Hub events.">
        <QueueDataTable
          rows={snapshot?.agentStatuses ?? []}
          rowKey={(item) => `${item.agentId}:${item.atUtc}`}
          emptyMessage="No agent status events available."
          columns={[
            { key: 'agent', header: 'Agent', cell: (item) => `${item.extensionNumber}${item.displayName ? ` - ${item.displayName}` : ''}` },
            { key: 'queueStatus', header: 'Queue Status', cell: (item) => item.queueStatus },
            { key: 'activity', header: 'Activity', cell: (item) => item.activityType },
            { key: 'callKey', header: 'Current Call', cell: (item) => item.currentCallKey ?? 'N/A' },
            { key: 'at', header: 'Updated At', cell: (item) => formatQueueUtc(item.atUtc) }
          ]}
        />
      </QueuePanel>
    </section>
  );
}
