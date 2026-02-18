import type { BrowserCallView } from '../domain/softphone';

interface IncomingCallModalProps {
  call: BrowserCallView;
  busy: boolean;
  onAnswer: (callId: string) => Promise<void>;
  onReject: (callId: string) => Promise<void>;
}

export function IncomingCallModal({ call, busy, onAnswer, onReject }: IncomingCallModalProps) {
  return (
    <div className="incoming-overlay">
      <section className="incoming-modal">
        <h3>Incoming Call</h3>
        <p>
          <strong>Extension {call.remoteExtension || 'Unknown caller'}</strong>
          {call.remoteUsername ? ` (${call.remoteUsername})` : ''}
        </p>
        <p>Status: {call.status}</p>

        <div className="incoming-actions">
          <button className="primary-button" type="button" disabled={busy} onClick={() => onAnswer(call.callId)}>
            Answer
          </button>
          <button className="danger-button" type="button" disabled={busy} onClick={() => onReject(call.callId)}>
            Reject
          </button>
        </div>
      </section>
    </div>
  );
}
