import { useEffect, useMemo, useRef, useState } from 'react';
import type { QueueListQueryModel, QueueLiveSnapshotModel, QueueModel } from '../models/contracts';
import { useQueueActions, useQueueDashboardSubscription, useQueueSignalRBridge } from '../hooks';
import { buildQueueRoutePath } from '../routes';
import { queueStoreDefaults, useQueueStore } from '../store';

interface QueueListPageProps {
  accessToken: string;
  initialQuery?: Partial<QueueListQueryModel>;
  enableRealtime?: boolean;
  onCreateQueue?: () => void;
  onEditQueue?: (queueId: number) => void;
  onOpenQueueDetails?: (queueId: number) => void;
  onOpenLiveMonitoring?: (queueId: number) => void;
  onOpenAnalytics?: (queueId: number) => void;
}

type RegistrationFilterValue = 'all' | 'registered' | 'unregistered';
type QueueListSortKey = NonNullable<QueueListQueryModel['sortBy']>;

interface QueueListFilterDraft {
  search: string;
  queueNumber: string;
  isRegistered: RegistrationFilterValue;
  sortBy: QueueListSortKey;
  sortDescending: boolean;
  pageSize: number;
}

function createFilterDraft(query: QueueListQueryModel): QueueListFilterDraft {
  return {
    search: query.search ?? '',
    queueNumber: query.queueNumber ?? '',
    isRegistered: query.isRegistered === true ? 'registered' : query.isRegistered === false ? 'unregistered' : 'all',
    sortBy: (query.sortBy ?? 'name') as QueueListSortKey,
    sortDescending: query.sortDescending,
    pageSize: query.pageSize
  };
}

function coerceRegistrationFilter(value: RegistrationFilterValue): boolean | null {
  if (value === 'registered') {
    return true;
  }

  if (value === 'unregistered') {
    return false;
  }

  return null;
}

function formatRelativeTime(isoUtc?: string | null): string {
  if (!isoUtc) {
    return 'N/A';
  }

  const date = Date.parse(isoUtc);
  if (!Number.isFinite(date)) {
    return isoUtc;
  }

  const deltaMs = Date.now() - date;
  const deltaSec = Math.round(deltaMs / 1000);
  if (Math.abs(deltaSec) < 60) {
    return `${deltaSec}s ago`;
  }

  const deltaMin = Math.round(deltaSec / 60);
  if (Math.abs(deltaMin) < 60) {
    return `${deltaMin}m ago`;
  }

  const deltaHours = Math.round(deltaMin / 60);
  if (Math.abs(deltaHours) < 24) {
    return `${deltaHours}h ago`;
  }

  const deltaDays = Math.round(deltaHours / 24);
  return `${deltaDays}d ago`;
}

function getLiveStats(snapshot?: QueueLiveSnapshotModel): { waiting: number | null; active: number | null; asOfUtc?: string | null } {
  if (!snapshot) {
    return {
      waiting: null,
      active: null,
      asOfUtc: null
    };
  }

  return {
    waiting: snapshot.stats?.waitingCount ?? snapshot.waitingCalls.length,
    active: snapshot.stats?.activeCount ?? snapshot.activeCalls.length,
    asOfUtc: snapshot.asOfUtc
  };
}

export function QueueListPage({
  accessToken,
  initialQuery,
  enableRealtime = true,
  onCreateQueue,
  onEditQueue,
  onOpenQueueDetails,
  onOpenLiveMonitoring,
  onOpenAnalytics
}: QueueListPageProps) {
  const queueList = useQueueStore((state) => state.queueList);
  const activeQuery = useQueueStore((state) => state.queueListQuery);
  const liveSnapshotsByQueueId = useQueueStore((state) => state.liveSnapshotsByQueueId);
  const dashboardAgentStatuses = useQueueStore((state) => state.dashboardAgentStatuses);
  const requests = useQueueStore((state) => state.requests);
  const setQueueListQuery = useQueueStore((state) => state.setQueueListQuery);
  const setActiveQueueId = useQueueStore((state) => state.setActiveQueueId);

  const actions = useQueueActions(accessToken);
  const { client, connection } = useQueueSignalRBridge(accessToken, {
    enabled: enableRealtime,
    autoStart: enableRealtime
  });
  useQueueDashboardSubscription(client, enableRealtime);

  const [draft, setDraft] = useState<QueueListFilterDraft>(() => createFilterDraft(activeQuery));
  const [notice, setNotice] = useState<string | null>(null);
  const initializedRef = useRef(false);

  useEffect(() => {
    if (initializedRef.current) {
      return;
    }

    initializedRef.current = true;
    if (!initialQuery) {
      return;
    }

    setQueueListQuery({
      ...initialQuery,
      page: initialQuery.page ?? activeQuery.page ?? 1
    });
  }, [activeQuery.page, initialQuery, setQueueListQuery]);

  useEffect(() => {
    setDraft((previous) => {
      const next = createFilterDraft(activeQuery);
      const same =
        previous.search === next.search &&
        previous.queueNumber === next.queueNumber &&
        previous.isRegistered === next.isRegistered &&
        previous.sortBy === next.sortBy &&
        previous.sortDescending === next.sortDescending &&
        previous.pageSize === next.pageSize;
      return same ? previous : next;
    });
  }, [activeQuery]);

  useEffect(() => {
    let mounted = true;

    const load = async () => {
      try {
        await actions.loadQueueList(activeQuery);
      } catch {
        if (!mounted) {
          return;
        }
      }
    };

    void load();
    return () => {
      mounted = false;
    };
  }, [
    accessToken,
    activeQuery.isRegistered,
    activeQuery.page,
    activeQuery.pageSize,
    activeQuery.queueNumber,
    activeQuery.search,
    activeQuery.sortBy,
    activeQuery.sortDescending
  ]);

  useEffect(() => {
    if (!notice) {
      return;
    }

    const timer = window.setTimeout(() => setNotice(null), 2800);
    return () => window.clearTimeout(timer);
  }, [notice]);

  const totalCount = queueList?.totalCount ?? queueList?.items.length ?? 0;
  const totalPages = Math.max(1, Math.ceil(totalCount / Math.max(1, activeQuery.pageSize)));
  const currentPage = Math.max(1, activeQuery.page);
  const canGoPrevious = currentPage > 1;
  const canGoNext =
    typeof queueList?.totalCount === 'number'
      ? currentPage < totalPages
      : (queueList?.items.length ?? 0) >= activeQuery.pageSize;

  const queueRows = queueList?.items ?? [];
  const queryListError = requests.queueList.errorMessage;

  const rowSummaries = useMemo(() => {
    return queueRows.map((queue) => ({
      queue,
      live: getLiveStats(liveSnapshotsByQueueId[queue.id])
    }));
  }, [queueRows, liveSnapshotsByQueueId]);

  const applyFilters = () => {
    setQueueListQuery({
      page: 1,
      pageSize: draft.pageSize,
      search: draft.search.trim() || null,
      queueNumber: draft.queueNumber.trim() || null,
      isRegistered: coerceRegistrationFilter(draft.isRegistered),
      sortBy: draft.sortBy,
      sortDescending: draft.sortDescending
    });
  };

  const resetFilters = () => {
    const defaults = queueStoreDefaults.queueListQuery;
    setQueueListQuery({
      ...defaults,
      page: 1
    });
    setDraft(createFilterDraft({ ...defaults, page: 1 }));
  };

  const reload = async () => {
    try {
      await actions.loadQueueList(activeQuery);
      setNotice('Queue list refreshed.');
    } catch {
    }
  };

  const deleteQueue = async (queue: QueueModel) => {
    const confirmed = window.confirm(`Delete queue ${queue.queueNumber} (${queue.name})?`);
    if (!confirmed) {
      return;
    }

    try {
      await actions.deleteQueue(queue.id);
      await actions.loadQueueList(activeQuery);
      setNotice(`Queue ${queue.queueNumber} deleted.`);
    } catch {
    }
  };

  const navigateWithFallback = (handler: ((queueId: number) => void) | undefined, fallbackPath: string, queueId: number) => {
    setActiveQueueId(queueId);
    if (handler) {
      handler(queueId);
      return;
    }

    window.history.pushState({}, '', fallbackPath);
    window.dispatchEvent(new PopStateEvent('popstate'));
  };

  return (
    <section className="supervisor-page-stack">
      <article className="card">
        <div className="analytics-header">
          <div>
            <h3>Queues / List</h3>
            <p className="history-summary">Search, sort, paginate, and monitor queue inventory with live dashboard subscription.</p>
          </div>
          <div className="status-strip">
            <span className={`status-chip ${requests.queueList.loading ? 'status-waiting' : 'status-active'}`}>
              {requests.queueList.loading ? 'Loading list' : 'List ready'}
            </span>
            <span className={`status-chip ${connection.status === 'connected' ? 'status-active' : 'status-waiting'}`}>
              Queue Hub: {connection.status}
            </span>
            <span className="status-chip status-info">Live agents: {dashboardAgentStatuses.length}</span>
            {requests.queueList.lastSuccessAtUtc && (
              <span className="status-chip status-info">Updated: {formatRelativeTime(requests.queueList.lastSuccessAtUtc)}</span>
            )}
          </div>
        </div>
        {(queryListError || notice) && (
          <div className="mt-3 grid gap-2">
            {queryListError && <div className="banner-error">{queryListError}</div>}
            {notice && <div className="banner-success">{notice}</div>}
          </div>
        )}
      </article>

      <article className="card">
        <div className="form-grid">
          <div className="grid-three">
            <div>
              <label className="label" htmlFor="queue-search-input">
                Search
              </label>
              <input
                id="queue-search-input"
                className="input"
                value={draft.search}
                placeholder="Queue name or number"
                onChange={(event) => setDraft((previous) => ({ ...previous, search: event.target.value }))}
                onKeyDown={(event) => {
                  if (event.key === 'Enter') {
                    applyFilters();
                  }
                }}
              />
            </div>

            <div>
              <label className="label" htmlFor="queue-number-filter">
                Queue Number
              </label>
              <input
                id="queue-number-filter"
                className="input"
                value={draft.queueNumber}
                placeholder="e.g. 800"
                onChange={(event) => setDraft((previous) => ({ ...previous, queueNumber: event.target.value }))}
                onKeyDown={(event) => {
                  if (event.key === 'Enter') {
                    applyFilters();
                  }
                }}
              />
            </div>

            <div>
              <label className="label" htmlFor="queue-registered-filter">
                Registration
              </label>
              <select
                id="queue-registered-filter"
                className="select"
                value={draft.isRegistered}
                onChange={(event) =>
                  setDraft((previous) => ({
                    ...previous,
                    isRegistered: event.target.value as RegistrationFilterValue
                  }))
                }
              >
                <option value="all">All</option>
                <option value="registered">Registered</option>
                <option value="unregistered">Unregistered</option>
              </select>
            </div>
          </div>

          <div className="grid-three">
            <div>
              <label className="label" htmlFor="queue-sort-by">
                Sort By
              </label>
              <select
                id="queue-sort-by"
                className="select"
                value={draft.sortBy}
                onChange={(event) =>
                  setDraft((previous) => ({
                    ...previous,
                    sortBy: event.target.value as QueueListSortKey
                  }))
                }
              >
                <option value="name">Name</option>
                <option value="queueNumber">Queue Number</option>
                <option value="id">ID</option>
                <option value="isRegistered">Registration</option>
              </select>
            </div>

            <div>
              <label className="label" htmlFor="queue-sort-direction">
                Sort Direction
              </label>
              <select
                id="queue-sort-direction"
                className="select"
                value={draft.sortDescending ? 'desc' : 'asc'}
                onChange={(event) =>
                  setDraft((previous) => ({
                    ...previous,
                    sortDescending: event.target.value === 'desc'
                  }))
                }
              >
                <option value="asc">Ascending</option>
                <option value="desc">Descending</option>
              </select>
            </div>

            <div>
              <label className="label" htmlFor="queue-page-size">
                Page Size
              </label>
              <select
                id="queue-page-size"
                className="select"
                value={draft.pageSize}
                onChange={(event) =>
                  setDraft((previous) => ({
                    ...previous,
                    pageSize: Number(event.target.value)
                  }))
                }
              >
                {[10, 20, 50, 100].map((pageSize) => (
                  <option key={pageSize} value={pageSize}>
                    {pageSize} rows
                  </option>
                ))}
              </select>
            </div>
          </div>

          <div className="grid-three">
            <button className="primary-button" type="button" onClick={applyFilters} disabled={requests.queueList.loading}>
              Apply Filters
            </button>
            <button className="secondary-button" type="button" onClick={resetFilters} disabled={requests.queueList.loading}>
              Reset Filters
            </button>
            <button className="secondary-button" type="button" onClick={() => void reload()} disabled={requests.queueList.loading}>
              Refresh
            </button>
          </div>

          <div className="grid-three">
            <button
              className="primary-button"
              type="button"
              onClick={() => {
                if (onCreateQueue) {
                  onCreateQueue();
                  return;
                }

                window.history.pushState({}, '', buildQueueRoutePath('queue-create'));
                window.dispatchEvent(new PopStateEvent('popstate'));
              }}
            >
              Create Queue
            </button>
            <div className="status-chip status-info">Page {currentPage} / {totalPages}</div>
            <div className="status-chip status-info">Total {totalCount}</div>
          </div>
        </div>
      </article>

      <article className="card supervisor-table">
        <div className="analytics-header">
          <div>
            <h4>Queue Rows</h4>
            <p className="history-summary">
              Showing {queueRows.length} row(s) on page {currentPage}. Realtime counters update when Queue Hub dashboard events arrive.
            </p>
          </div>
          <div className="analytics-actions">
            <button
              className="secondary-button"
              type="button"
              disabled={!canGoPrevious || requests.queueList.loading}
              onClick={() => setQueueListQuery({ page: Math.max(1, currentPage - 1) })}
            >
              Previous
            </button>
            <button
              className="secondary-button"
              type="button"
              disabled={!canGoNext || requests.queueList.loading}
              onClick={() => setQueueListQuery({ page: currentPage + 1 })}
            >
              Next
            </button>
          </div>
        </div>

        {queueRows.length === 0 && !requests.queueList.loading && (
          <div className="rounded-2xl border border-border bg-white/80 p-4 text-sm text-muted">
            No queues matched the current filters.
          </div>
        )}

        <div className="grid gap-3">
          {rowSummaries.map(({ queue, live }) => (
            <div key={queue.id} className="supervisor-row">
              <div className="grid gap-2">
                <div className="flex flex-wrap items-center gap-2">
                  <strong className="text-ink">{queue.name}</strong>
                  <span className={`status-chip ${queue.isRegistered ? 'status-active' : 'status-waiting'}`}>
                    {queue.isRegistered ? 'Registered' : 'Unregistered'}
                  </span>
                  <span className="status-chip status-info">Queue #{queue.queueNumber}</span>
                  <span className="status-chip status-info">ID {queue.id}</span>
                  <span className="status-chip status-info">PBX {queue.pbxQueueId}</span>
                </div>

                <div className="flex flex-wrap gap-2 text-sm text-muted-strong">
                  <span>Agents: {queue.agents.length}</span>
                  <span>Managers: {queue.managers.length}</span>
                  <span>Live Waiting: {live.waiting ?? 'N/A'}</span>
                  <span>Live Active: {live.active ?? 'N/A'}</span>
                  <span>Live AsOf: {formatRelativeTime(live.asOfUtc)}</span>
                </div>

                {queue.settings.slaTimeSec != null && (
                  <div className="text-sm text-muted">
                    SLA Threshold: {queue.settings.slaTimeSec}s
                    {queue.settings.maxCallersInQueue != null ? ` | Max Callers: ${queue.settings.maxCallersInQueue}` : ''}
                    {queue.settings.ringTimeoutSec != null ? ` | Ring Timeout: ${queue.settings.ringTimeoutSec}s` : ''}
                  </div>
                )}
              </div>

              <div className="row-actions">
                <button
                  className="secondary-button"
                  type="button"
                  onClick={() =>
                    navigateWithFallback(
                      onOpenQueueDetails,
                      buildQueueRoutePath('queue-details', { queueId: queue.id }),
                      queue.id
                    )
                  }
                >
                  Details
                </button>
                <button
                  className="secondary-button"
                  type="button"
                  onClick={() => {
                    setActiveQueueId(queue.id);
                    onEditQueue?.(queue.id);
                  }}
                >
                  Edit
                </button>
                <button
                  className="secondary-button"
                  type="button"
                  onClick={() =>
                    navigateWithFallback(
                      onOpenLiveMonitoring,
                      buildQueueRoutePath('queue-live-monitor', { queueId: queue.id }),
                      queue.id
                    )
                  }
                >
                  Live
                </button>
                <button
                  className="secondary-button"
                  type="button"
                  onClick={() =>
                    navigateWithFallback(
                      onOpenAnalytics,
                      buildQueueRoutePath('queue-analytics', { queueId: queue.id }),
                      queue.id
                    )
                  }
                >
                  Analytics
                </button>
                <button className="danger-button" type="button" onClick={() => void deleteQueue(queue)} disabled={requests.queueMutation.loading}>
                  Delete
                </button>
              </div>
            </div>
          ))}
        </div>
      </article>
    </section>
  );
}
