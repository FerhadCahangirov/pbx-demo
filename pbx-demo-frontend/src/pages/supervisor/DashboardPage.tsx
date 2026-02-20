import { useMemo } from 'react';
import type { CrmCallAnalyticsResponse } from '../../domain/crm';
import { formatDurationSeconds, formatUtc } from './shared';

interface DashboardPageProps {
  version: string;
  busy: boolean;
  analyticsDays: number;
  onAnalyticsDaysChange: (days: number) => void;
  onRefresh: () => Promise<void>;
  callAnalytics: CrmCallAnalyticsResponse | null;
  queueLoad: { label: string; tone: string };
  statusChartData: Array<{ label: string; value: number }>;
}

function MetricCard({
  label,
  value,
  className
}: {
  label: string;
  value: string | number;
  className?: string;
}) {
  return (
    <div className={`kpi-card ${className ?? ''}`.trim()}>
      <span className="kpi-label">{label}</span>
      <strong className="kpi-value">{value}</strong>
    </div>
  );
}

export function DashboardPage({
  version,
  busy,
  analyticsDays,
  onAnalyticsDaysChange,
  onRefresh,
  callAnalytics,
  queueLoad,
  statusChartData
}: DashboardPageProps) {
  const operatorCallChart = useMemo(() => {
    if (!callAnalytics || callAnalytics.operatorKpis.length === 0) {
      return [];
    }

    const sorted = [...callAnalytics.operatorKpis]
      .sort((left, right) => right.totalCalls - left.totalCalls)
      .slice(0, 8);
    const maxValue = Math.max(...sorted.map((item) => item.totalCalls), 1);
    return sorted.map((item) => ({
      label: item.operatorDisplayName || item.operatorUsername,
      value: item.totalCalls,
      widthPercent: Math.max(8, Math.round((item.totalCalls / maxValue) * 100))
    }));
  }, [callAnalytics]);

  const statusChartMax = useMemo(
    () => Math.max(1, ...statusChartData.map((item) => item.value)),
    [statusChartData]
  );

  return (
    <section className="supervisor-page-stack">
      <article className="card">
        <div className="analytics-header">
          <div>
            <h3>Dashboard and KPIs</h3>
            <p className="history-summary">3CX Version: {version}</p>
          </div>
          <div className="analytics-actions">
            <select
              className="select analytics-days-select"
              value={analyticsDays}
              onChange={(event) => onAnalyticsDaysChange(Number(event.target.value))}
              disabled={busy}
            >
              <option value={1}>Last 1 day</option>
              <option value={7}>Last 7 days</option>
              <option value={30}>Last 30 days</option>
              <option value={90}>Last 90 days</option>
            </select>
            <button className="primary-button" type="button" disabled={busy} onClick={() => void onRefresh()}>
              Refresh KPIs
            </button>
          </div>
        </div>

        {callAnalytics ? (
          <>
            <p className="history-summary">
              Range: {formatUtc(callAnalytics.periodStartUtc)} - {formatUtc(callAnalytics.periodEndUtc)}
            </p>
            <div className="kpi-grid">
              <MetricCard label="Total Calls" value={callAnalytics.totalCalls} className="kpi-card-info" />
              <MetricCard label="Answered" value={callAnalytics.answeredCalls} className="kpi-card-active" />
              <MetricCard label="Missed" value={callAnalytics.missedCalls} className="kpi-card-critical" />
              <MetricCard label="Active Calls" value={callAnalytics.activeCalls} className="kpi-card-waiting" />
              <MetricCard label="Total Operators" value={callAnalytics.totalOperators} className="kpi-card-info" />
              <MetricCard label="Avg Talk Time" value={formatDurationSeconds(callAnalytics.averageTalkSeconds)} className="kpi-card-info" />
              <MetricCard label="Queue Load" value={queueLoad.label} className={queueLoad.tone} />
            </div>
          </>
        ) : (
          <p>No analytics yet.</p>
        )}
      </article>

      <article className="card">
        <h3>Operator Performance Graph</h3>
        {operatorCallChart.length > 0 ? (
          <div className="graph-list">
            {operatorCallChart.map((item) => (
              <div key={item.label} className="graph-row">
                <span className="graph-label">{item.label}</span>
                <div className="graph-track">
                  <div className="graph-fill graph-fill-primary" style={{ width: `${item.widthPercent}%` }} />
                </div>
                <strong className="graph-value">{item.value}</strong>
              </div>
            ))}
          </div>
        ) : (
          <p>No operator KPI values available for graph rendering.</p>
        )}
      </article>

      <article className="card">
        <h3>CDR Status Analytics Graph</h3>
        {statusChartData.length > 0 ? (
          <div className="graph-list">
            {statusChartData.slice(0, 8).map((item) => {
              const widthPercent = Math.max(8, Math.round((item.value / statusChartMax) * 100));
              return (
                <div key={item.label} className="graph-row">
                  <span className="graph-label">{item.label}</span>
                  <div className="graph-track">
                    <div className="graph-fill graph-fill-secondary" style={{ width: `${widthPercent}%` }} />
                  </div>
                  <strong className="graph-value">{item.value}</strong>
                </div>
              );
            })}
          </div>
        ) : (
          <p>No status data available.</p>
        )}
      </article>
    </section>
  );
}
