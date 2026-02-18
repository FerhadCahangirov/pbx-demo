import type { BrowserCallView } from '../domain/softphone';

interface CallListProps {
  calls: BrowserCallView[];
  busy: boolean;
  onAnswer: (callId: string) => Promise<void>;
  onReject: (callId: string) => Promise<void>;
  onEnd: (callId: string) => Promise<void>;
}

function statusClass(status: BrowserCallView['status']): string {
  const normalized = status.toLowerCase();
  if (normalized === 'ringing') {
    return 'status-ringing';
  }

  if (normalized === 'connecting') {
    return 'status-dialing';
  }

  if (normalized === 'connected') {
    return 'status-connected';
  }

  return '';
}

export function CallList({ calls, busy, onAnswer, onReject, onEnd }: CallListProps) {
  if (calls.length === 0 || calls.every((call) => call.status === 'Ended')) {
    return <p>No active calls.</p>;
  }

  return (
    <div className="call-list">
      {calls.map((call) => {
        const incoming = call.isIncoming;
        const ringing = call.status === 'Ringing';
        const canAnswer = incoming && ringing;

        return (
          <article key={call.callId} className="call-card">
            <h4>Extension {call.remoteExtension || 'Unknown'}</h4>
            <div className="call-meta">
              <span>{call.remoteUsername || 'Unknown user'}</span>
              <strong className={statusClass(call.status)}>
                {call.status} ({incoming ? 'Incoming' : 'Outgoing'})
              </strong>
              {call.endReason && <span>End reason: {call.endReason}</span>}
            </div>

            <div className="incoming-actions">
              {canAnswer && call.status !== 'Ended' && (
                <button className="primary-button" type="button" disabled={busy} onClick={() => onAnswer(call.callId)}>
                  Answer
                </button>
              )}
              {ringing && incoming && call.status !== 'Ended' && (
                <button className="danger-button" type="button" disabled={busy} onClick={() => onReject(call.callId)}>
                  Reject
                </button>
              )}
              {call.status !== 'Ended' && (
                <button className="danger-button" type="button" disabled={busy} onClick={() => onEnd(call.callId)}>
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
