import type { BrowserCallView } from '../domain/softphone';

interface CallListProps {
  calls: BrowserCallView[];
  busy: boolean;
  onAnswer: (callId: string) => Promise<void>;
  onReject: (callId: string) => Promise<void>;
  onEnd: (callId: string) => Promise<void>;
}

function statusClass(status: BrowserCallView['status']): string {
  switch (status) {
    case 'Ringing':
      return 'status-waiting';
    case 'Connecting':
      return 'status-info';
    case 'Connected':
      return 'status-active';
    case 'Ended':
    default:
      return 'status-critical';
  }
}

function formatCallTime(value: string): string {
  const parsed = Date.parse(value);
  if (Number.isNaN(parsed)) {
    return '-';
  }
  return new Date(parsed).toLocaleTimeString();
}

export function CallList({ calls, busy, onAnswer, onReject, onEnd }: CallListProps) {
  if (calls.length === 0 || calls.every((call) => call.status === 'Ended')) {
    return <p className="text-sm text-muted">No active calls.</p>;
  }

  return (
    <div className="grid gap-3">
      {calls.map((call) => {
        const incoming = call.isIncoming;
        const ringing = call.status === 'Ringing';
        const canAnswer = incoming && ringing;

        return (
          <article key={call.callId} className="rounded-2xl border border-border bg-white/85 p-4 shadow-sm">
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div className="grid gap-1">
                <h4 className="font-display text-base font-semibold text-ink">Extension {call.remoteExtension || 'Unknown'}</h4>
                <span className="text-sm text-muted">{call.remoteUsername || 'Unknown user'}</span>
                <span className="text-xs text-muted">Started: {formatCallTime(call.createdAtUtc)}</span>
              </div>
              <div className="status-strip">
                <span className={`status-chip ${incoming ? 'status-waiting' : 'status-info'}`}>
                  {incoming ? 'Incoming' : 'Outgoing'}
                </span>
                <span className={`status-chip ${statusClass(call.status)}`}>{call.status}</span>
              </div>
            </div>

            {call.endReason && <p className="mt-2 text-sm text-muted">End reason: {call.endReason}</p>}

            <div className="call-actions">
              {canAnswer && call.status !== 'Ended' && (
                <button className="primary-button" type="button" disabled={busy} onClick={() => void onAnswer(call.callId)}>
                  Answer
                </button>
              )}
              {ringing && incoming && call.status !== 'Ended' && (
                <button className="danger-button" type="button" disabled={busy} onClick={() => void onReject(call.callId)}>
                  Reject
                </button>
              )}
              {call.status !== 'Ended' && (
                <button className="danger-button" type="button" disabled={busy} onClick={() => void onEnd(call.callId)}>
                  End
                </button>
              )}
            </div>
          </article>
        );
      })}
    </div>
  );
}
