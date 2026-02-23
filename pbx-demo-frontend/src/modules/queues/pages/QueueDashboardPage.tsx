import { useEffect, useMemo, useState } from 'react';
import {
  QueueBarChart,
  QueueDataTable,
  QueueKpiGrid,
  QueueLineChart,
  QueueNoticeStack,
  QueuePanel,
  formatQueueDurationMs,
  formatQueuePercent,
  formatQueueUtc
} from '../components';
import { useQueueActions } from '../hooks';
import type { QueueAnalyticsQueryModel } from '../models/contracts';
import { useQueueStore } from '../store';

interface QueueDashboardPageProps {
  accessToken: string;
  queueId: number;
  onSelectQueue?: (queueId: number) => void;
}

type RangePreset = '24h' | '7d' | '30d' | '90d';
type BucketValue = 'hour' | 'day' | 'month';

function toErrorMessage(error: unknown, fallback: string): string {
  if (error instanceof Error && error.message.trim().length > 0) {
    return error.message;
  }

  return fallback;
}

function rangePresetToQuery(preset: RangePreset, bucket: BucketValue, slaThresholdSec: number | null): QueueAnalyticsQueryModel {
  const now = new Date();
  const from = new Date(now);

  if (preset === '24h') {
    from.setHours(now.getHours() - 24);
  } else if (preset === '7d') {
    from.setDate(now.getDate() - 7);
  } else if (preset === '30d') {
    from.setDate(now.getDate() - 30);
  } else {
    from.setDate(now.getDate() - 90);
  }

  return {
    fromUtc: from.toISOString(),
    toUtc: now.toISOString(),
    bucket,
    slaThresholdSec,
    timeZoneId: Intl.DateTimeFormat().resolvedOptions().timeZone || 'UTC'
  };
}

function parseCommaSeparatedIds(raw: string): number[] {
  return raw
    .split(',')
    .map((value) => Number(value.trim()))
    .filter((value, index, all) => Number.isFinite(value) && value > 0 && all.indexOf(value) === index);
}

function inferTone(label: string | null | undefined): 'active' | 'warning' | 'critical' | 'info' {
  const value = (label ?? '').toLowerCase();
  if (value.includes('critical') || value.includes('severe') || value.includes('high')) {
    return 'critical';
  }
  if (value.includes('warn') || value.includes('moderate') || value.includes('medium')) {
    return 'warning';
  }
  if (value.includes('healthy') || value.includes('low') || value.includes('stable')) {
    return 'active';
  }
  return 'info';
}

export function QueueDashboardPage({ accessToken, queueId, onSelectQueue }: QueueDashboardPageProps) {
  const actions = useQueueActions(accessToken);
  const queueList = useQueueStore((state) => state.queueList);
  const analytics = useQueueStore((state) => state.analyticsByQueueId[queueId]);
  const comparison = useQueueStore((state) => state.comparison);
  const requests = useQueueStore((state) => state.requests);

  const [rangePreset, setRangePreset] = useState<RangePreset>('24h');
  const [bucket, setBucket] = useState<BucketValue>('hour');
  const [slaThresholdInput, setSlaThresholdInput] = useState<string>('');
  const [compareQueueIdsText, setCompareQueueIdsText] = useState<string>(queueId > 0 ? String(queueId) : '');
  const [pageError, setPageError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  useEffect(() => {
    setCompareQueueIdsText((previous) => {
      const ids = parseCommaSeparatedIds(previous);
      if (ids.includes(queueId) || queueId <= 0) {
        return previous;
      }
      return previous.trim().length > 0 ? `${queueId}, ${previous}` : String(queueId);
    });
  }, [queueId]);

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
          setPageError(toErrorMessage(error, 'Failed to load queues for dashboard filters.'));
        }
      }
    };

    void loadQueues();
    return () => {
      mounted = false;
    };
  }, [accessToken]);

  useEffect(() => {
    if (!notice) {
      return;
    }
    const timer = window.setTimeout(() => setNotice(null), 3000);
    return () => window.clearTimeout(timer);
  }, [notice]);

  const selectedSlaThreshold = useMemo(() => {
    const trimmed = slaThresholdInput.trim();
    if (trimmed.length === 0) {
      return null;
    }
    const parsed = Number(trimmed);
    if (!Number.isFinite(parsed) || parsed < 0) {
      return null;
    }
    return Math.trunc(parsed);
  }, [slaThresholdInput]);

  const analyticsQuery = useMemo(
    () => rangePresetToQuery(rangePreset, bucket, selectedSlaThreshold),
    [rangePreset, bucket, selectedSlaThreshold]
  );

  const loadDashboard = async () => {
    setPageError(null);
    setNotice(null);

    if (!Number.isFinite(queueId) || queueId <= 0) {
      setPageError('Choose a valid queue.');
      return;
    }

    try {
      await actions.loadQueueAnalytics(queueId, analyticsQuery);

      const compareIds = parseCommaSeparatedIds(compareQueueIdsText);
      if (compareIds.length > 0) {
        await actions.loadQueueComparison(compareIds, analyticsQuery);
      }

      setNotice('Queue analytics loaded.');
    } catch (error) {
      setPageError(toErrorMessage(error, `Failed to load analytics for queue ${queueId}.`));
    }
  };

  useEffect(() => {
    if (queueId > 0) {
      void loadDashboard();
    }
  }, [queueId]);

  const notices = useMemo(() => {
    const items: Array<{ id: string; tone: 'error' | 'warning' | 'success'; message: string }> = [];
    if (pageError) {
      items.push({ id: 'page-error', tone: 'error' as const, message: pageError });
    }
    if (requests.queueAnalytics.errorMessage && requests.queueAnalytics.errorMessage !== pageError) {
      items.push({ id: 'analytics-error', tone: 'error' as const, message: requests.queueAnalytics.errorMessage });
    }
    if (requests.queueComparison.errorMessage) {
      items.push({ id: 'comparison-error', tone: 'error' as const, message: requests.queueComparison.errorMessage });
    }
    if (notice) {
      items.push({ id: 'notice', tone: 'success' as const, message: notice });
    }
    items.push({
      id: 'chart-lib-todo',
      tone: 'warning' as const,
      message:
        'TODO(FE): Repo does not currently include Recharts/Chart.js. Using lightweight SVG charts as a temporary implementation.'
    });
    return items;
  }, [notice, pageError, requests.queueAnalytics.errorMessage, requests.queueComparison.errorMessage]);

  const queueOptions = queueList?.items ?? [];
  const kpiItems = analytics
    ? [
        { id: 'total', label: 'Total Calls', value: analytics.totalCalls },
        { id: 'active', label: 'Answered Calls', value: analytics.answeredCalls, tone: 'active' as const },
        { id: 'missed', label: 'Missed Calls', value: analytics.missedCalls, tone: analytics.missedCalls > 0 ? 'warning' as const : 'info' as const },
        { id: 'abandoned', label: 'Abandoned Calls', value: analytics.abandonedCalls, tone: analytics.abandonedCalls > 0 ? 'critical' as const : 'info' as const },
        { id: 'avg-wait', label: 'Avg Wait', value: formatQueueDurationMs(analytics.averageWaitingMsAll) },
        { id: 'avg-talk', label: 'Avg Talk', value: formatQueueDurationMs(analytics.averageTalkingMs) },
        { id: 'sla', label: 'SLA Adherence', value: formatQueuePercent(analytics.slaPct), tone: analytics.slaPct != null && analytics.slaPct < 80 ? 'warning' as const : 'active' as const },
        { id: 'congestion', label: 'Congestion Index', value: analytics.queueCongestionIndex?.toFixed(2) ?? 'N/A', tone: inferTone(analytics.realtimeClassification) },
        { id: 'peak', label: 'Peak Concurrency', value: analytics.peakConcurrency ?? 'N/A', hint: analytics.peakConcurrencyAtUtc ? `At ${formatQueueUtc(analytics.peakConcurrencyAtUtc)}` : null }
      ]
    : [];

  const waitingTrendPoints = (analytics?.timeSeries ?? []).map((bucketRow) => ({
    label: bucketRow.bucketLabel,
    value: bucketRow.averageWaitingMs ?? null
  }));
  const slaTrendPoints = (analytics?.timeSeries ?? []).map((bucketRow) => ({
    label: bucketRow.bucketLabel,
    value: bucketRow.slaPct ?? null
  }));
  const callsBarPoints = (analytics?.timeSeries ?? []).map((bucketRow) => ({
    label: bucketRow.bucketLabel,
    value: bucketRow.totalCalls
  }));

  return (
    <section className="supervisor-page-stack">
      <QueuePanel title="Queues / Analytics Dashboard" subtitle="Queue KPIs, trends, and comparisons from queue analytics endpoints.">
        <div className="form-grid">
          <div className="grid-three">
            <div>
              <label className="label" htmlFor="queue-analytics-queue">Queue</label>
              <select
                id="queue-analytics-queue"
                className="select"
                value={queueId > 0 ? String(queueId) : ''}
                onChange={(event) => {
                  const nextId = Number(event.target.value);
                  if (Number.isFinite(nextId) && nextId > 0) {
                    onSelectQueue?.(nextId);
                  }
                }}
              >
                {queueOptions.length === 0 && <option value="">No queues loaded</option>}
                {queueOptions.map((queue) => (
                  <option key={queue.id} value={queue.id}>
                    {queue.queueNumber} - {queue.name}
                  </option>
                ))}
              </select>
            </div>

            <div>
              <label className="label" htmlFor="queue-analytics-range">Time Range</label>
              <select
                id="queue-analytics-range"
                className="select"
                value={rangePreset}
                onChange={(event) => setRangePreset(event.target.value as RangePreset)}
              >
                <option value="24h">Last 24 Hours</option>
                <option value="7d">Last 7 Days</option>
                <option value="30d">Last 30 Days</option>
                <option value="90d">Last 90 Days</option>
              </select>
            </div>

            <div>
              <label className="label" htmlFor="queue-analytics-bucket">Bucket</label>
              <select
                id="queue-analytics-bucket"
                className="select"
                value={bucket}
                onChange={(event) => setBucket(event.target.value as BucketValue)}
              >
                <option value="hour">Hour</option>
                <option value="day">Day</option>
                <option value="month">Month</option>
              </select>
            </div>
          </div>

          <div className="grid-two">
            <div>
              <label className="label" htmlFor="queue-analytics-sla">SLA Threshold Override (sec)</label>
              <input
                id="queue-analytics-sla"
                className="input"
                type="number"
                min={0}
                step={1}
                value={slaThresholdInput}
                placeholder="Optional"
                onChange={(event) => setSlaThresholdInput(event.target.value)}
              />
            </div>
            <div>
              <label className="label" htmlFor="queue-analytics-compare">Compare Queue IDs</label>
              <input
                id="queue-analytics-compare"
                className="input"
                value={compareQueueIdsText}
                placeholder="e.g. 1,2,3"
                onChange={(event) => setCompareQueueIdsText(event.target.value)}
              />
            </div>
          </div>

          <div className="grid-two">
            <button
              className="primary-button"
              type="button"
              onClick={() => void loadDashboard()}
              disabled={requests.queueAnalytics.loading || requests.queueComparison.loading}
            >
              Load Analytics
            </button>
            <div className="status-strip">
              <span className={`status-chip ${requests.queueAnalytics.loading ? 'status-waiting' : 'status-active'}`}>
                Analytics: {requests.queueAnalytics.loading ? 'Loading' : 'Ready'}
              </span>
              <span className={`status-chip ${requests.queueComparison.loading ? 'status-waiting' : 'status-info'}`}>
                Compare: {requests.queueComparison.loading ? 'Loading' : 'Idle'}
              </span>
            </div>
          </div>

          <QueueNoticeStack notices={notices} />
        </div>
      </QueuePanel>

      <QueuePanel title="KPI Snapshot" subtitle={analytics ? `Generated ${formatQueueUtc(analytics.generatedAtUtc)}` : 'Load analytics to display KPI summary.'}>
        {analytics ? <QueueKpiGrid items={kpiItems} columns={4} /> : <div className="text-sm text-muted">No analytics loaded yet.</div>}
      </QueuePanel>

      <QueuePanel title="Trend Charts" subtitle="Average wait, SLA adherence, and call volume by selected bucket.">
        <div className="grid gap-3 xl:grid-cols-3">
          <QueueLineChart
            title="Average Wait Time"
            subtitle="Derived from queue analytics time series"
            points={waitingTrendPoints}
            formatter={(value) => formatQueueDurationMs(value)}
          />
          <QueueLineChart
            title="SLA Adherence"
            subtitle="Percent within SLA threshold"
            points={slaTrendPoints}
            formatter={(value) => `${value.toFixed(1)}%`}
          />
          <QueueBarChart
            title="Total Calls per Bucket"
            subtitle="Traffic volume over selected time range"
            points={callsBarPoints}
            formatter={(value) => String(Math.round(value))}
          />
        </div>
      </QueuePanel>

      <QueuePanel title="Agent Rankings" subtitle="Ranked agents returned by analytics engine.">
        <QueueDataTable
          rows={analytics?.agentRankings ?? []}
          rowKey={(item) => `${item.queueId}:${item.agentId}`}
          emptyMessage="No agent ranking data for the selected period."
          columns={[
            { key: 'rank', header: 'Rank', cell: (item) => <span className="font-semibold text-ink">#{item.rank}</span> },
            { key: 'agent', header: 'Agent', cell: (item) => `${item.extensionNumber} ${item.displayName ? `- ${item.displayName}` : ''}`.trim() },
            { key: 'answered', header: 'Answered', cell: (item) => item.answeredCalls },
            { key: 'wait', header: 'Avg Wait', cell: (item) => formatQueueDurationMs(item.averageWaitingMs) },
            { key: 'talk', header: 'Avg Talk', cell: (item) => formatQueueDurationMs(item.averageTalkingMs) },
            { key: 'sla', header: 'SLA %', cell: (item) => formatQueuePercent(item.slaCompliancePct) },
            { key: 'util', header: 'Utilization %', cell: (item) => formatQueuePercent(item.utilizationPct) },
            { key: 'score', header: 'Score', cell: (item) => item.agentRankingScore.toFixed(3) }
          ]}
        />
      </QueuePanel>

      <QueuePanel title="Queue Comparison" subtitle="Results from GET /api/queue-analytics/compare for selected queue IDs.">
        <QueueDataTable
          rows={comparison?.queues ?? []}
          rowKey={(item) => String(item.queueId)}
          emptyMessage="No comparison data loaded."
          columns={[
            { key: 'queue', header: 'Queue ID', cell: (item) => item.queueId },
            { key: 'rank', header: 'Rank', cell: (item) => item.comparisonScore?.rank ?? 'N/A' },
            { key: 'mqi', header: 'MQI', cell: (item) => item.comparisonScore?.mqi?.toFixed(3) ?? 'N/A' },
            { key: 'sla', header: 'SLA %', cell: (item) => formatQueuePercent(item.slaPct) },
            { key: 'avg-wait', header: 'Avg Wait', cell: (item) => formatQueueDurationMs(item.averageWaitingMsAll) },
            { key: 'abandon', header: 'Abandon %', cell: (item) => formatQueuePercent(item.abandonmentRatePct) },
            { key: 'calls', header: 'Total Calls', cell: (item) => item.totalCalls }
          ]}
        />
      </QueuePanel>
    </section>
  );
}
