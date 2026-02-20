import type { BrowserCallView } from '../domain/softphone';

interface IncomingCallModalProps {
  call: BrowserCallView;
  busy: boolean;
  onAnswer: (callId: string) => Promise<void>;
  onReject: (callId: string) => Promise<void>;
}

export function IncomingCallModal({ call, busy, onAnswer, onReject }: IncomingCallModalProps) {
  return (
    <div className="incoming-toast" role="status" aria-live="polite">
      <div className="flex items-start justify-between gap-3">
        <div>
          <h3 className="font-display text-lg font-semibold text-ink">Incoming call</h3>
          <p className="mt-1 text-sm text-muted">
            <strong className="font-semibold text-ink">Extension {call.remoteExtension || 'Unknown caller'}</strong>
            {call.remoteUsername ? ` (${call.remoteUsername})` : ''}
          </p>
          <p className="mt-1 text-sm text-muted">Status: {call.status}</p>
        </div>
        <span className="status-chip status-waiting">Ringing</span>
      </div>

      <div className="call-actions">
        <button className="primary-button" type="button" disabled={busy} onClick={() => void onAnswer(call.callId)}>
          Answer
        </button>
        <button className="danger-button" type="button" disabled={busy} onClick={() => void onReject(call.callId)}>
          Reject
        </button>
      </div>
    </div>
  );
}
