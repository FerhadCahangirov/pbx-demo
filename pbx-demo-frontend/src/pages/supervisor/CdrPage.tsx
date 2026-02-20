import type { CrmCallHistoryItemResponse } from '../../domain/crm';
import type { CdrMetaState } from './shared';
import { formatDurationSeconds, formatUtc } from './shared';

interface CdrPageProps {
  busy: boolean;
  callHistory: CrmCallHistoryItemResponse[];
  callHistoryMeta: CdrMetaState;
  onRefresh: () => Promise<void>;
  onChangeTake: (take: number) => Promise<void>;
  onPreviousPage: () => Promise<void>;
  onNextPage: () => Promise<void>;
}

export function CdrPage({
  busy,
  callHistory,
  callHistoryMeta,
  onRefresh,
  onChangeTake,
  onPreviousPage,
  onNextPage
}: CdrPageProps) {
  const from = callHistoryMeta.totalCount === 0 ? 0 : callHistoryMeta.skip + 1;
  const to = Math.min(callHistoryMeta.skip + callHistoryMeta.take, callHistoryMeta.totalCount);

  return (
    <section className="supervisor-page-stack">
      <article className="card">
        <div className="analytics-header">
          <div>
            <h3>Call Detail Records (CDR)</h3>
            <p className="history-summary">
              Showing {from}-{to} of {callHistoryMeta.totalCount}
            </p>
          </div>
          <div className="analytics-actions">
            <select
              className="select analytics-days-select"
              value={callHistoryMeta.take}
              disabled={busy}
              onChange={(event) => void onChangeTake(Number(event.target.value))}
            >
              <option value={10}>10 rows</option>
              <option value={25}>25 rows</option>
              <option value={50}>50 rows</option>
            </select>
            <button className="secondary-button" type="button" disabled={busy} onClick={() => void onRefresh()}>
              Refresh CDR
            </button>
            <button
              className="secondary-button"
              type="button"
              disabled={busy || callHistoryMeta.skip <= 0}
              onClick={() => void onPreviousPage()}
            >
              Previous
            </button>
            <button
              className="secondary-button"
              type="button"
              disabled={busy || callHistoryMeta.skip + callHistoryMeta.take >= callHistoryMeta.totalCount}
              onClick={() => void onNextPage()}
            >
              Next
            </button>
          </div>
        </div>
      </article>

      <article className="card">
        <div className="supervisor-table">
          {callHistory.map((call) => (
            <div key={call.id} className="supervisor-row history-row">
              <div>
                <strong>{call.operatorDisplayName}</strong> ({call.operatorUsername})<br />
                {call.source} | {call.direction} | Status: {call.status} | Ext: {call.operatorExtension || 'N/A'}<br />
                Remote: {call.remoteName || call.remoteParty || '-'} {call.endReason ? `| End: ${call.endReason}` : ''}
              </div>
              <div>
                Start: {formatUtc(call.startedAtUtc)}<br />
                End: {formatUtc(call.endedAtUtc)}<br />
                Talk: {formatDurationSeconds(call.talkDurationSeconds)}
              </div>
              {call.statusHistory.length > 0 && (
                <div className="history-timeline">
                  {call.statusHistory.map((item, index) => (
                    <span key={`${call.id}-${index}`} className="history-pill">
                      {item.status} @ {formatUtc(item.occurredAtUtc)}
                    </span>
                  ))}
                </div>
              )}
            </div>
          ))}
          {callHistory.length === 0 && <p>No CDR rows found for the current filter.</p>}
        </div>
      </article>
    </section>
  );
}
