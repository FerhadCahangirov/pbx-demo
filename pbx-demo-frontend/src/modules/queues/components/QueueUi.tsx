import type { ReactNode } from 'react';

export type QueueNoticeTone = 'info' | 'success' | 'error' | 'warning';

export interface QueueNoticeItem {
  id: string;
  tone: QueueNoticeTone;
  message: string;
}

export function QueuePanel(props: { title?: string; subtitle?: string; actions?: ReactNode; children: ReactNode; className?: string }) {
  return (
    <article className={`card ${props.className ?? ''}`.trim()}>
      {(props.title || props.actions) && (
        <div className="analytics-header">
          <div>
            {props.title && <h3>{props.title}</h3>}
            {props.subtitle && <p className="history-summary">{props.subtitle}</p>}
          </div>
          {props.actions && <div className="analytics-actions">{props.actions}</div>}
        </div>
      )}
      <div className={props.title || props.actions ? 'mt-3' : undefined}>{props.children}</div>
    </article>
  );
}

export function QueueNoticeBanner(props: { tone: QueueNoticeTone; message: string }) {
  const className =
    props.tone === 'error'
      ? 'banner-error'
      : props.tone === 'success'
        ? 'banner-success'
        : props.tone === 'warning'
          ? 'rounded-xl border border-amber-300 bg-amber-50 px-3 py-2 text-sm text-amber-900'
          : 'rounded-xl border border-border bg-white/80 px-3 py-2 text-sm text-muted-strong';

  return <div className={className}>{props.message}</div>;
}

export function QueueNoticeStack(props: { notices: QueueNoticeItem[] }) {
  if (props.notices.length === 0) {
    return null;
  }

  return (
    <div className="grid gap-2">
      {props.notices.map((notice) => (
        <QueueNoticeBanner key={notice.id} tone={notice.tone} message={notice.message} />
      ))}
    </div>
  );
}

export interface QueueKpiCardItem {
  id: string;
  label: string;
  value: string | number;
  hint?: string | null;
  tone?: 'info' | 'active' | 'warning' | 'critical';
}

function kpiToneClass(tone: QueueKpiCardItem['tone']): string {
  switch (tone) {
    case 'active':
      return 'border-emerald-200 bg-emerald-50';
    case 'warning':
      return 'border-amber-200 bg-amber-50';
    case 'critical':
      return 'border-rose-200 bg-rose-50';
    case 'info':
    default:
      return 'border-border bg-white/80';
  }
}

export function QueueKpiGrid(props: { items: QueueKpiCardItem[]; columns?: 2 | 3 | 4 }) {
  const gridClass =
    props.columns === 2
      ? 'md:grid-cols-2'
      : props.columns === 4
        ? 'md:grid-cols-2 xl:grid-cols-4'
        : 'md:grid-cols-2 xl:grid-cols-3';

  return (
    <div className={`grid gap-3 ${gridClass}`}>
      {props.items.map((item) => (
        <div key={item.id} className={`rounded-2xl border p-4 ${kpiToneClass(item.tone)}`}>
          <p className="text-xs font-semibold uppercase tracking-wide text-muted">{item.label}</p>
          <p className="mt-1 text-xl font-semibold text-ink">{item.value}</p>
          {item.hint && <p className="mt-1 text-xs text-muted">{item.hint}</p>}
        </div>
      ))}
    </div>
  );
}

export interface QueueTableColumn<TItem> {
  key: string;
  header: string;
  cell: (item: TItem) => ReactNode;
  headerClassName?: string;
  cellClassName?: string;
}

export function QueueDataTable<TItem>(props: {
  rows: TItem[];
  columns: Array<QueueTableColumn<TItem>>;
  rowKey: (item: TItem, index: number) => string;
  emptyMessage: string;
  compact?: boolean;
}) {
  if (props.rows.length === 0) {
    return <QueueEmptyState message={props.emptyMessage} />;
  }

  return (
    <div className="overflow-x-auto rounded-2xl border border-border bg-white/80">
      <table className={`min-w-full ${props.compact ? 'text-xs' : 'text-sm'}`}>
        <thead className="bg-surface/80 text-left text-muted-strong">
          <tr>
            {props.columns.map((column) => (
              <th key={column.key} className={`px-3 py-2 font-semibold ${column.headerClassName ?? ''}`.trim()}>
                {column.header}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {props.rows.map((item, index) => (
            <tr key={props.rowKey(item, index)} className="border-t border-border/70">
              {props.columns.map((column) => (
                <td key={column.key} className={`px-3 py-2 align-top text-muted-strong ${column.cellClassName ?? ''}`.trim()}>
                  {column.cell(item)}
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

export function QueueEmptyState(props: { message: string; extra?: ReactNode }) {
  return (
    <div className="rounded-2xl border border-border bg-white/80 p-4 text-sm text-muted">
      <p>{props.message}</p>
      {props.extra && <div className="mt-2">{props.extra}</div>}
    </div>
  );
}

export interface QueueChartPoint {
  label: string;
  value: number | null | undefined;
}

function clamp01(value: number): number {
  if (value < 0) {
    return 0;
  }
  if (value > 1) {
    return 1;
  }
  return value;
}

export function QueueLineChart(props: {
  title: string;
  subtitle?: string;
  points: QueueChartPoint[];
  colorClassName?: string;
  formatter?: (value: number) => string;
}) {
  const normalizedValues = props.points
    .map((point) => (typeof point.value === 'number' && Number.isFinite(point.value) ? point.value : null))
    .filter((value): value is number => value !== null);

  if (props.points.length === 0 || normalizedValues.length === 0) {
    return (
      <div className="rounded-2xl border border-border bg-white/80 p-4">
        <p className="text-sm font-semibold text-ink">{props.title}</p>
        {props.subtitle && <p className="text-xs text-muted">{props.subtitle}</p>}
        <p className="mt-2 text-sm text-muted">No chart data.</p>
      </div>
    );
  }

  const min = Math.min(...normalizedValues);
  const max = Math.max(...normalizedValues);
  const range = Math.max(1, max - min);
  const width = 480;
  const height = 180;
  const paddingX = 16;
  const paddingY = 16;
  const innerWidth = width - paddingX * 2;
  const innerHeight = height - paddingY * 2;

  const plotted = props.points.map((point, index) => {
    const x = paddingX + (props.points.length <= 1 ? 0 : (index / (props.points.length - 1)) * innerWidth);
    const raw = typeof point.value === 'number' && Number.isFinite(point.value) ? point.value : min;
    const ratio = clamp01((raw - min) / range);
    const y = paddingY + (1 - ratio) * innerHeight;
    return { x, y, label: point.label, value: raw };
  });

  const path = plotted.map((point, index) => `${index === 0 ? 'M' : 'L'} ${point.x.toFixed(2)} ${point.y.toFixed(2)}`).join(' ');
  const latest = plotted[plotted.length - 1]?.value ?? 0;

  return (
    <div className="rounded-2xl border border-border bg-white/80 p-4">
      <div className="flex items-start justify-between gap-3">
        <div>
          <p className="text-sm font-semibold text-ink">{props.title}</p>
          {props.subtitle && <p className="text-xs text-muted">{props.subtitle}</p>}
        </div>
        <span className={`status-chip ${props.colorClassName ?? 'status-info'}`}>
          {props.formatter ? props.formatter(latest) : latest}
        </span>
      </div>
      <svg viewBox={`0 0 ${width} ${height}`} className="mt-3 h-44 w-full">
        <rect x={0} y={0} width={width} height={height} rx={14} className="fill-slate-50" />
        <path d={`M ${paddingX} ${height - paddingY} L ${width - paddingX} ${height - paddingY}`} className="stroke-slate-200" />
        <path d={path} className="fill-none stroke-primary-600" strokeWidth={2.5} />
        {plotted.map((point) => (
          <circle key={`${point.label}:${point.x}`} cx={point.x} cy={point.y} r={2.8} className="fill-primary-700" />
        ))}
      </svg>
      <div className="mt-2 flex flex-wrap gap-2">
        {props.points.slice(Math.max(0, props.points.length - 4)).map((point, index) => (
          <span key={`${point.label}:${index}`} className="history-pill">
            {point.label}
          </span>
        ))}
      </div>
    </div>
  );
}

export function QueueBarChart(props: {
  title: string;
  subtitle?: string;
  points: QueueChartPoint[];
  formatter?: (value: number) => string;
}) {
  const valid = props.points.filter(
    (point): point is { label: string; value: number } =>
      typeof point.value === 'number' && Number.isFinite(point.value) && point.value >= 0
  );

  if (valid.length === 0) {
    return (
      <div className="rounded-2xl border border-border bg-white/80 p-4">
        <p className="text-sm font-semibold text-ink">{props.title}</p>
        {props.subtitle && <p className="text-xs text-muted">{props.subtitle}</p>}
        <p className="mt-2 text-sm text-muted">No chart data.</p>
      </div>
    );
  }

  const max = Math.max(1, ...valid.map((point) => point.value));

  return (
    <div className="rounded-2xl border border-border bg-white/80 p-4">
      <p className="text-sm font-semibold text-ink">{props.title}</p>
      {props.subtitle && <p className="text-xs text-muted">{props.subtitle}</p>}
      <div className="mt-3 grid gap-2">
        {valid.slice(-12).map((point) => {
          const widthPct = Math.max(4, Math.round((point.value / max) * 100));
          return (
            <div key={point.label} className="grid grid-cols-[minmax(0,120px)_1fr_auto] items-center gap-2 text-xs">
              <span className="truncate text-muted">{point.label}</span>
              <div className="h-2 rounded-full bg-slate-100">
                <div className="h-2 rounded-full bg-secondary-500" style={{ width: `${widthPct}%` }} />
              </div>
              <span className="font-semibold text-ink">{props.formatter ? props.formatter(point.value) : point.value}</span>
            </div>
          );
        })}
      </div>
    </div>
  );
}

export function formatQueueUtc(value: string | null | undefined): string {
  if (!value) {
    return 'N/A';
  }

  const parsed = Date.parse(value);
  if (!Number.isFinite(parsed)) {
    return value;
  }

  return new Date(parsed).toLocaleString();
}

export function formatQueueDurationMs(value: number | null | undefined): string {
  if (typeof value !== 'number' || !Number.isFinite(value) || value < 0) {
    return 'N/A';
  }

  const totalSeconds = Math.round(value / 1000);
  const hours = Math.floor(totalSeconds / 3600);
  const minutes = Math.floor((totalSeconds % 3600) / 60);
  const seconds = totalSeconds % 60;
  if (hours > 0) {
    return `${hours}h ${minutes}m ${seconds}s`;
  }
  if (minutes > 0) {
    return `${minutes}m ${seconds}s`;
  }
  return `${seconds}s`;
}

export function formatQueuePercent(value: number | null | undefined): string {
  if (typeof value !== 'number' || !Number.isFinite(value)) {
    return 'N/A';
  }

  return `${value.toFixed(1)}%`;
}

