import { useEffect, useMemo, useState } from 'react';
import { QueueDataTable, QueueNoticeStack, QueuePanel, formatQueueUtc } from '../components';
import { useQueueActions } from '../hooks';
import type { QueueCallHistoryItemModel, QueueCallHistoryQueryModel } from '../models/contracts';
import { QUEUE_CALL_HISTORY_API_TODO_MESSAGE } from '../services';
import { useQueueStore } from '../store';

interface QueueCallHistoryPageProps {
  accessToken: string;
  queueId: number;
}

interface QueueCallHistoryFilterDraft {
  fromDate: string;
  toDate: string;
  agentId: string;
  disposition: string;
  search: string;
  page: number;
  pageSize: number;
}

function createDefaultFilterDraft(): QueueCallHistoryFilterDraft {
  const now = new Date();
  const from = new Date(now);
  from.setDate(now.getDate() - 7);
  return {
    fromDate: from.toISOString().slice(0, 10),
    toDate: now.toISOString().slice(0, 10),
    agentId: '',
    disposition: '',
    search: '',
    page: 1,
    pageSize: 25
  };
}

function toQueryDraft(draft: QueueCallHistoryFilterDraft): QueueCallHistoryQueryModel {
  const fromUtc = draft.fromDate ? new Date(`${draft.fromDate}T00:00:00`).toISOString() : null;
  const toUtc = draft.toDate ? new Date(`${draft.toDate}T23:59:59.999`).toISOString() : null;
  const agentIdRaw = draft.agentId.trim();
  const parsedAgentId = agentIdRaw.length > 0 ? Number(agentIdRaw) : null;

  return {
    fromUtc,
    toUtc,
    agentId: Number.isFinite(parsedAgentId) && (parsedAgentId ?? 0) > 0 ? Math.trunc(parsedAgentId as number) : null,
    disposition: draft.disposition.trim() || null,
    search: draft.search.trim() || null,
    page: draft.page,
    pageSize: draft.pageSize
  };
}

function toErrorMessage(error: unknown, fallback: string): string {
  if (error instanceof Error && error.message.trim().length > 0) {
    return error.message;
  }

  return fallback;
}

export function QueueCallHistoryPage({ accessToken, queueId }: QueueCallHistoryPageProps) {
  const actions = useQueueActions(accessToken);
  const queue = useQueueStore((state) => state.queuesById[queueId]);
  const queueDetailRequest = useQueueStore((state) => state.requests.queueDetail);
  const [filters, setFilters] = useState<QueueCallHistoryFilterDraft>(() => createDefaultFilterDraft());
  const [pageError, setPageError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  useEffect(() => {
    let mounted = true;
    const loadQueue = async () => {
      if (!queueId || queueId <= 0) {
        setPageError('Queue ID is required.');
        return;
      }

      try {
        await actions.loadQueue(queueId);
        if (mounted) {
          setPageError(null);
        }
      } catch (error) {
        if (mounted) {
          setPageError(toErrorMessage(error, `Failed to load queue ${queueId}.`));
        }
      }
    };

    void loadQueue();
    return () => {
      mounted = false;
    };
  }, [accessToken, queueId]);

  useEffect(() => {
    if (!notice) {
      return;
    }
    const timer = window.setTimeout(() => setNotice(null), 3000);
    return () => window.clearTimeout(timer);
  }, [notice]);

  const notices = useMemo(() => {
    const items: Array<{ id: string; tone: 'error' | 'warning' | 'success'; message: string }> = [];
    if (pageError) {
      items.push({ id: 'page-error', tone: 'error', message: pageError });
    }
    if (queueDetailRequest.errorMessage && queueDetailRequest.errorMessage !== pageError) {
      items.push({ id: 'queue-error', tone: 'error', message: queueDetailRequest.errorMessage });
    }
    items.push({ id: 'history-todo', tone: 'warning', message: QUEUE_CALL_HISTORY_API_TODO_MESSAGE });
    if (notice) {
      items.push({ id: 'notice', tone: 'success', message: notice });
    }
    return items;
  }, [notice, pageError, queueDetailRequest.errorMessage]);

  const queryPreview = useMemo(() => toQueryDraft(filters), [filters]);
  const rows: QueueCallHistoryItemModel[] = [];

  return (
    <section className="supervisor-page-stack">
      <QueuePanel
        title="Queues / Call History"
        subtitle={queue ? `Queue ${queue.queueNumber} (${queue.name})` : 'Queue-specific call history'}
      >
        <div className="status-strip">
          <span className={`status-chip ${queueDetailRequest.loading ? 'status-waiting' : 'status-active'}`}>
            {queueDetailRequest.loading ? 'Loading queue' : 'Queue ready'}
          </span>
          <span className="status-chip status-info">Queue ID: {queueId}</span>
        </div>
        <div className="mt-3">
          <QueueNoticeStack notices={notices} />
        </div>
      </QueuePanel>

      <QueuePanel title="Filters" subtitle="Prepared UI and query model for queue call history endpoint (backend TODO).">
        <div className="form-grid">
          <div className="grid-three">
            <div>
              <label className="label" htmlFor="queue-history-from">From</label>
              <input
                id="queue-history-from"
                className="input"
                type="date"
                value={filters.fromDate}
                onChange={(event) => setFilters((prev) => ({ ...prev, fromDate: event.target.value, page: 1 }))}
              />
            </div>
            <div>
              <label className="label" htmlFor="queue-history-to">To</label>
              <input
                id="queue-history-to"
                className="input"
                type="date"
                value={filters.toDate}
                onChange={(event) => setFilters((prev) => ({ ...prev, toDate: event.target.value, page: 1 }))}
              />
            </div>
            <div>
              <label className="label" htmlFor="queue-history-agent">Agent ID</label>
              <input
                id="queue-history-agent"
                className="input"
                value={filters.agentId}
                placeholder="Optional"
                onChange={(event) => setFilters((prev) => ({ ...prev, agentId: event.target.value, page: 1 }))}
              />
            </div>
          </div>

          <div className="grid-three">
            <div>
              <label className="label" htmlFor="queue-history-disposition">Disposition</label>
              <input
                id="queue-history-disposition"
                className="input"
                value={filters.disposition}
                placeholder="Answered / Missed / Abandoned"
                onChange={(event) => setFilters((prev) => ({ ...prev, disposition: event.target.value, page: 1 }))}
              />
            </div>
            <div>
              <label className="label" htmlFor="queue-history-search">Search</label>
              <input
                id="queue-history-search"
                className="input"
                value={filters.search}
                placeholder="Caller number / agent / outcome"
                onChange={(event) => setFilters((prev) => ({ ...prev, search: event.target.value, page: 1 }))}
              />
            </div>
            <div>
              <label className="label" htmlFor="queue-history-page-size">Page Size</label>
              <select
                id="queue-history-page-size"
                className="select"
                value={filters.pageSize}
                onChange={(event) => setFilters((prev) => ({ ...prev, pageSize: Number(event.target.value), page: 1 }))}
              >
                {[25, 50, 100].map((size) => (
                  <option key={size} value={size}>
                    {size}
                  </option>
                ))}
              </select>
            </div>
          </div>

          <div className="grid-two">
            <button
              className="primary-button"
              type="button"
              onClick={() => setNotice('Frontend filters are ready. Waiting for backend call-history endpoint implementation.')}
            >
              Apply Filters (Placeholder)
            </button>
            <div className="text-xs text-muted">
              Prepared query:
              <pre className="mt-1 overflow-x-auto rounded-xl border border-border bg-surface/60 p-2 text-[11px]">
                {JSON.stringify(queryPreview, null, 2)}
              </pre>
            </div>
          </div>
        </div>
      </QueuePanel>

      <QueuePanel title="Call History Results" subtitle="Table shape implemented; awaiting backend endpoint to populate rows.">
        <QueueDataTable
          rows={rows}
          rowKey={(item) => item.id}
          emptyMessage="No queue call history data is available yet because the backend queue-history endpoint is not implemented."
          columns={[
            { key: 'start', header: 'Call Start', cell: (item) => formatQueueUtc(item.callStartUtc) },
            { key: 'end', header: 'Call End', cell: (item) => formatQueueUtc(item.callEndUtc) },
            { key: 'caller', header: 'Caller', cell: (item) => `${item.callerNumber ?? 'Unknown'}${item.callerName ? ` - ${item.callerName}` : ''}` },
            { key: 'agent', header: 'Agent', cell: (item) => item.agentExtension ?? item.agentDisplayName ?? 'N/A' },
            { key: 'duration', header: 'Duration', cell: (item) => (item.durationMs != null ? `${Math.round(item.durationMs / 1000)}s` : 'N/A') },
            { key: 'outcome', header: 'Outcome', cell: (item) => item.outcome ?? item.disposition ?? 'N/A' }
          ]}
        />
      </QueuePanel>
    </section>
  );
}
