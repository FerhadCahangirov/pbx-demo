import { useEffect, useMemo, useState } from 'react';
import { QueueDataTable, QueueKpiGrid, QueueNoticeStack, QueuePanel } from '../components';
import { useQueueActions } from '../hooks';
import type { QueueModel } from '../models/contracts';
import { useQueueStore } from '../store';

interface QueueDetailsPageProps {
  accessToken: string;
  queueId: number;
  onEditQueue?: (queueId: number) => void;
  onOpenLiveMonitoring?: (queueId: number) => void;
  onOpenAnalytics?: (queueId: number) => void;
  onOpenCallHistory?: (queueId: number) => void;
}

function toErrorMessage(error: unknown, fallback: string): string {
  if (error instanceof Error && error.message.trim().length > 0) {
    return error.message;
  }

  return fallback;
}

function getSettingRows(queue: QueueModel): Array<{ key: string; value: string }> {
  const settings = queue.settings;
  const rows: Array<{ key: string; value: string }> = [
    { key: 'Agent Availability Mode', value: String(Boolean(settings.agentAvailabilityMode)) },
    { key: 'Announcement Interval (sec)', value: settings.announcementIntervalSec?.toString() ?? 'N/A' },
    { key: 'Announce Queue Position', value: String(Boolean(settings.announceQueuePosition)) },
    { key: 'Callback Enable Time (sec)', value: settings.callbackEnableTimeSec?.toString() ?? 'N/A' },
    { key: 'Callback Prefix', value: settings.callbackPrefix ?? 'N/A' },
    { key: 'Ring Timeout (sec)', value: settings.ringTimeoutSec?.toString() ?? 'N/A' },
    { key: 'Master Timeout (sec)', value: settings.masterTimeoutSec?.toString() ?? 'N/A' },
    { key: 'Max Callers In Queue', value: settings.maxCallersInQueue?.toString() ?? 'N/A' },
    { key: 'SLA Time (sec)', value: settings.slaTimeSec?.toString() ?? 'N/A' },
    { key: 'Wrap Up Time (sec)', value: settings.wrapUpTimeSec?.toString() ?? 'N/A' },
    { key: 'Priority Queue', value: String(Boolean(settings.priorityQueue)) },
    { key: 'Enable Intro', value: String(Boolean(settings.enableIntro)) },
    { key: 'Prompt Set', value: settings.promptSet ?? 'N/A' },
    { key: 'Recording Mode', value: settings.recordingMode ?? 'N/A' },
    { key: 'Polling Strategy', value: settings.pollingStrategy ?? 'N/A' },
    { key: 'Notify Codes', value: settings.notifyCodes.length > 0 ? settings.notifyCodes.join(', ') : 'N/A' }
  ];

  return rows;
}

export function QueueDetailsPage({
  accessToken,
  queueId,
  onEditQueue,
  onOpenLiveMonitoring,
  onOpenAnalytics,
  onOpenCallHistory
}: QueueDetailsPageProps) {
  const actions = useQueueActions(accessToken);
  const queue = useQueueStore((state) => state.queuesById[queueId]);
  const queueDetailRequest = useQueueStore((state) => state.requests.queueDetail);
  const [pageError, setPageError] = useState<string | null>(null);

  useEffect(() => {
    let mounted = true;
    const load = async () => {
      if (!queueId || queueId <= 0) {
        setPageError('Queue ID is required.');
        return;
      }

      try {
        await actions.loadQueue(queueId);
        if (!mounted) {
          return;
        }
        setPageError(null);
      } catch (error) {
        if (mounted) {
          setPageError(toErrorMessage(error, `Failed to load queue ${queueId}.`));
        }
      }
    };

    void load();
    return () => {
      mounted = false;
    };
  }, [accessToken, queueId]);

  const notices = useMemo(() => {
    const items: Array<{ id: string; tone: 'error'; message: string }> = [];
    if (pageError) {
      items.push({ id: 'page-error', tone: 'error' as const, message: pageError });
    } else if (queueDetailRequest.errorMessage) {
      items.push({ id: 'request-error', tone: 'error' as const, message: queueDetailRequest.errorMessage });
    }
    return items;
  }, [pageError, queueDetailRequest.errorMessage]);

  const kpis = useMemo(() => {
    if (!queue) {
      return [];
    }

    return [
      { id: 'queue-id', label: 'Queue ID', value: queue.id },
      { id: 'pbx-id', label: 'PBX Queue ID', value: queue.pbxQueueId },
        {
          id: 'registered',
          label: 'Registration',
          value: queue.isRegistered ? 'Registered' : 'Unregistered',
          tone: queue.isRegistered ? ('active' as const) : ('warning' as const)
        },
        { id: 'agents', label: 'Agents', value: queue.agents.length },
        { id: 'managers', label: 'Managers', value: queue.managers.length },
        {
          id: 'sla',
          label: 'SLA Threshold',
          value: queue.settings.slaTimeSec != null ? `${queue.settings.slaTimeSec}s` : 'N/A',
          hint: queue.settings.slaTimeSec != null ? 'Configured in queue settings' : null
        }
      ];
  }, [queue]);

  return (
    <section className="supervisor-page-stack">
      <QueuePanel
        title="Queues / Details"
        subtitle={queue ? `Queue ${queue.queueNumber} (${queue.name})` : 'Loading queue details'}
        actions={
          <div className="flex flex-wrap gap-2">
            <button className="secondary-button" type="button" onClick={() => onEditQueue?.(queueId)} disabled={!queue}>
              Edit
            </button>
            <button className="secondary-button" type="button" onClick={() => onOpenLiveMonitoring?.(queueId)} disabled={!queue}>
              Live
            </button>
            <button className="secondary-button" type="button" onClick={() => onOpenAnalytics?.(queueId)} disabled={!queue}>
              Analytics
            </button>
            <button className="secondary-button" type="button" onClick={() => onOpenCallHistory?.(queueId)} disabled={!queue}>
              History
            </button>
          </div>
        }
      >
        <div className="status-strip">
          <span className={`status-chip ${queueDetailRequest.loading ? 'status-waiting' : 'status-active'}`}>
            {queueDetailRequest.loading ? 'Loading' : 'Ready'}
          </span>
          {queue && <span className="status-chip status-info">Queue #{queue.queueNumber}</span>}
        </div>
        <div className="mt-3">
          <QueueNoticeStack notices={notices} />
        </div>
      </QueuePanel>

      <QueuePanel title="Summary" subtitle="Queue identity, registration, and assignment summary.">
        {queue ? <QueueKpiGrid items={kpis} columns={3} /> : <div className="text-sm text-muted">Loading queue details...</div>}
      </QueuePanel>

      <QueuePanel title="Agent Assignments" subtitle="Current queue members and skill groups.">
        <QueueDataTable
          rows={queue?.agents ?? []}
          rowKey={(item, index) => `${item.extensionNumber}:${index}`}
          emptyMessage="No agents assigned."
          columns={[
            { key: 'extension', header: 'Extension', cell: (item) => item.extensionNumber },
            { key: 'name', header: 'Display Name', cell: (item) => item.displayName ?? 'N/A' },
            { key: 'skill', header: 'Skill Group', cell: (item) => item.skillGroup ?? 'N/A' }
          ]}
        />
      </QueuePanel>

      <QueuePanel title="Manager Assignments" subtitle="Queue supervisors/managers configured for this queue.">
        <QueueDataTable
          rows={queue?.managers ?? []}
          rowKey={(item, index) => `${item.extensionNumber}:${index}`}
          emptyMessage="No managers assigned."
          columns={[
            { key: 'extension', header: 'Extension', cell: (item) => item.extensionNumber },
            { key: 'name', header: 'Display Name', cell: (item) => item.displayName ?? 'N/A' }
          ]}
        />
      </QueuePanel>

      <QueuePanel title="Settings" subtitle="3CX queue settings currently returned by the backend queue API.">
        <QueueDataTable
          rows={queue ? getSettingRows(queue) : []}
          rowKey={(item) => item.key}
          emptyMessage={queue ? 'No settings returned.' : 'Loading settings...'}
          compact
          columns={[
            { key: 'key', header: 'Setting', cell: (item) => <span className="font-semibold text-ink">{item.key}</span> },
            { key: 'value', header: 'Value', cell: (item) => item.value }
          ]}
        />
      </QueuePanel>
    </section>
  );
}
