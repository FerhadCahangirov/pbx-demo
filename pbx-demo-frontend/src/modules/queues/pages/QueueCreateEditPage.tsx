import { useEffect, useMemo, useState } from 'react';
import { useQueueActions } from '../hooks';
import type {
  CreateQueueRequestModel,
  QueueAgentAssignmentModel,
  QueueManagerAssignmentModel,
  QueueModel,
  QueueSettingsModel,
  UpdateQueueRequestModel
} from '../models/contracts';
import { useQueueStore } from '../store';

interface QueueCreateEditPageProps {
  accessToken: string;
  mode: 'create' | 'edit';
  queueId?: number | null;
  onSaved?: (queue: QueueModel) => void;
  onCancel?: () => void;
}

interface QueueEditorDraft {
  queueNumber: string;
  name: string;
  notifyCodesText: string;
  agentsText: string;
  managersText: string;
  replaceAgents: boolean;
  replaceManagers: boolean;
  settings: QueueSettingsModel;
}

type AssignmentSortBy = 'extensionNumber' | 'displayName';

interface ParseIssue {
  lineNumber: number;
  message: string;
}

interface AssignmentPreviewState {
  filter: string;
  sortBy: AssignmentSortBy;
  sortDescending: boolean;
  page: number;
  pageSize: number;
}

interface AssignmentPreviewProps<TItem extends { extensionNumber: string; displayName?: string | null }> {
  title: string;
  subtitle: string;
  items: TItem[];
  issues: ParseIssue[];
  state: AssignmentPreviewState;
  onStateChange: (next: AssignmentPreviewState) => void;
  renderExtra?: (item: TItem) => string | null;
}

function defaultQueueSettings(): QueueSettingsModel {
  return {
    agentAvailabilityMode: false,
    announcementIntervalSec: null,
    announceQueuePosition: false,
    callbackEnableTimeSec: null,
    callbackPrefix: null,
    enableIntro: false,
    greetingFile: null,
    introFile: null,
    onHoldFile: null,
    promptSet: null,
    playFullPrompt: false,
    priorityQueue: false,
    ringTimeoutSec: null,
    masterTimeoutSec: null,
    maxCallersInQueue: null,
    slaTimeSec: null,
    wrapUpTimeSec: null,
    pollingStrategy: null,
    recordingMode: null,
    notifyCodes: [],
    resetStatisticsScheduleEnabled: false,
    resetQueueStatisticsSchedule: null,
    breakRoute: null,
    holidaysRoute: null,
    outOfOfficeRoute: null,
    forwardNoAnswer: null
  };
}

function createDraft(): QueueEditorDraft {
  return {
    queueNumber: '',
    name: '',
    notifyCodesText: '',
    agentsText: '',
    managersText: '',
    replaceAgents: true,
    replaceManagers: true,
    settings: defaultQueueSettings()
  };
}

function cloneSettings(settings: QueueSettingsModel): QueueSettingsModel {
  return {
    ...settings,
    notifyCodes: [...settings.notifyCodes]
  };
}

function draftFromQueue(queue: QueueModel): QueueEditorDraft {
  return {
    queueNumber: queue.queueNumber,
    name: queue.name,
    notifyCodesText: queue.settings.notifyCodes.join(', '),
    agentsText: queue.agents
      .map((item) => [item.extensionNumber, item.displayName ?? '', item.skillGroup ?? ''].join('|').replace(/\|+$/, ''))
      .join('\n'),
    managersText: queue.managers
      .map((item) => [item.extensionNumber, item.displayName ?? ''].join('|').replace(/\|+$/, ''))
      .join('\n'),
    replaceAgents: true,
    replaceManagers: true,
    settings: cloneSettings(queue.settings)
  };
}

function parseOptionalNumber(value: string): number | null {
  if (value.trim().length === 0) {
    return null;
  }

  const parsed = Number(value);
  if (!Number.isFinite(parsed)) {
    return null;
  }

  return Math.max(0, Math.trunc(parsed));
}

function parseNotifyCodes(raw: string): string[] {
  return raw
    .split(',')
    .map((value) => value.trim())
    .filter((value) => value.length > 0);
}

function parseAgentAssignments(raw: string, existing: QueueAgentAssignmentModel[] = []): { items: QueueAgentAssignmentModel[]; issues: ParseIssue[] } {
  const existingByExtension = new Map(existing.map((item) => [item.extensionNumber, item]));
  const seen = new Set<string>();
  const items: QueueAgentAssignmentModel[] = [];
  const issues: ParseIssue[] = [];

  raw.split(/\r?\n/).forEach((line, index) => {
    const trimmed = line.trim();
    if (trimmed.length === 0) {
      return;
    }

    const [extensionNumber, displayName, skillGroup] = trimmed.split('|').map((part) => part.trim());
    if (!extensionNumber) {
      issues.push({ lineNumber: index + 1, message: 'Extension number is required.' });
      return;
    }

    if (seen.has(extensionNumber)) {
      issues.push({ lineNumber: index + 1, message: `Duplicate extension ${extensionNumber}.` });
      return;
    }

    seen.add(extensionNumber);
    const previous = existingByExtension.get(extensionNumber);
    items.push({
      extensionId: previous?.extensionId ?? null,
      extensionNumber,
      displayName: displayName || previous?.displayName || null,
      skillGroup: skillGroup || previous?.skillGroup || null
    });
  });

  return { items, issues };
}

function parseManagerAssignments(raw: string, existing: QueueManagerAssignmentModel[] = []): { items: QueueManagerAssignmentModel[]; issues: ParseIssue[] } {
  const existingByExtension = new Map(existing.map((item) => [item.extensionNumber, item]));
  const seen = new Set<string>();
  const items: QueueManagerAssignmentModel[] = [];
  const issues: ParseIssue[] = [];

  raw.split(/\r?\n/).forEach((line, index) => {
    const trimmed = line.trim();
    if (trimmed.length === 0) {
      return;
    }

    const [extensionNumber, displayName] = trimmed.split('|').map((part) => part.trim());
    if (!extensionNumber) {
      issues.push({ lineNumber: index + 1, message: 'Extension number is required.' });
      return;
    }

    if (seen.has(extensionNumber)) {
      issues.push({ lineNumber: index + 1, message: `Duplicate extension ${extensionNumber}.` });
      return;
    }

    seen.add(extensionNumber);
    const previous = existingByExtension.get(extensionNumber);
    items.push({
      extensionId: previous?.extensionId ?? null,
      extensionNumber,
      displayName: displayName || previous?.displayName || null
    });
  });

  return { items, issues };
}

function sortAssignments<TItem extends { extensionNumber: string; displayName?: string | null }>(
  items: TItem[],
  state: AssignmentPreviewState
): TItem[] {
  const filter = state.filter.trim().toLowerCase();
  const filtered = filter.length === 0
    ? items
    : items.filter((item) => `${item.extensionNumber} ${item.displayName ?? ''}`.toLowerCase().includes(filter));

  return [...filtered].sort((left, right) => {
    const leftKey = (state.sortBy === 'extensionNumber' ? left.extensionNumber : left.displayName ?? '').toLowerCase();
    const rightKey = (state.sortBy === 'extensionNumber' ? right.extensionNumber : right.displayName ?? '').toLowerCase();
    const value = leftKey.localeCompare(rightKey, undefined, { numeric: true, sensitivity: 'base' });
    if (value !== 0) {
      return state.sortDescending ? -value : value;
    }

    const fallback = left.extensionNumber.localeCompare(right.extensionNumber, undefined, {
      numeric: true,
      sensitivity: 'base'
    });
    return state.sortDescending ? -fallback : fallback;
  });
}

function toErrorMessage(error: unknown, fallback: string): string {
  if (error instanceof Error && error.message.trim().length > 0) {
    return error.message;
  }

  return fallback;
}

function AssignmentPreview<TItem extends { extensionNumber: string; displayName?: string | null }>(
  props: AssignmentPreviewProps<TItem>
) {
  const sorted = useMemo(() => sortAssignments(props.items, props.state), [props.items, props.state]);
  const totalPages = Math.max(1, Math.ceil(sorted.length / props.state.pageSize));
  const page = Math.min(Math.max(1, props.state.page), totalPages);
  const rows = sorted.slice((page - 1) * props.state.pageSize, page * props.state.pageSize);

  useEffect(() => {
    if (page !== props.state.page) {
      props.onStateChange({ ...props.state, page });
    }
  }, [page, props]);

  return (
    <div className="rounded-2xl border border-border bg-white/80 p-4">
      <div className="analytics-header">
        <div>
          <h4>{props.title}</h4>
          <p className="history-summary">{props.subtitle}</p>
        </div>
        <div className="status-strip">
          <span className="status-chip status-info">Parsed: {props.items.length}</span>
          <span className={`status-chip ${props.issues.length > 0 ? 'status-critical' : 'status-active'}`}>
            Issues: {props.issues.length}
          </span>
        </div>
      </div>

      <div className="mt-3 form-grid">
        <div className="grid-three">
          <input
            className="input"
            placeholder="Filter extension or name"
            value={props.state.filter}
            onChange={(event) => props.onStateChange({ ...props.state, filter: event.target.value, page: 1 })}
          />
          <select
            className="select"
            value={props.state.sortBy}
            onChange={(event) => props.onStateChange({ ...props.state, sortBy: event.target.value as AssignmentSortBy, page: 1 })}
          >
            <option value="extensionNumber">Extension</option>
            <option value="displayName">Display Name</option>
          </select>
          <select
            className="select"
            value={props.state.sortDescending ? 'desc' : 'asc'}
            onChange={(event) =>
              props.onStateChange({ ...props.state, sortDescending: event.target.value === 'desc', page: 1 })
            }
          >
            <option value="asc">Ascending</option>
            <option value="desc">Descending</option>
          </select>
        </div>

        {props.issues.length > 0 && (
          <div className="banner-error">
            {props.issues.slice(0, 3).map((issue) => `Line ${issue.lineNumber}: ${issue.message}`).join(' | ')}
            {props.issues.length > 3 ? ` | +${props.issues.length - 3} more` : ''}
          </div>
        )}

        <div className="grid gap-2">
          {rows.length === 0 && <div className="text-sm text-muted">No rows match the current filter.</div>}
          {rows.map((item) => {
            const extra = props.renderExtra?.(item);
            return (
              <div
                key={`${props.title}:${item.extensionNumber}`}
                className="flex flex-wrap items-center justify-between gap-2 rounded-xl border border-border/70 bg-surface/70 px-3 py-2 text-sm"
              >
                <div className="flex flex-wrap items-center gap-2">
                  <span className="status-chip status-info">{item.extensionNumber}</span>
                  <span className="text-muted-strong">{item.displayName || 'Unnamed'}</span>
                  {extra && <span className="history-pill">{extra}</span>}
                </div>
              </div>
            );
          })}
        </div>

        <div className="analytics-actions">
          <button
            className="secondary-button"
            type="button"
            disabled={page <= 1}
            onClick={() => props.onStateChange({ ...props.state, page: Math.max(1, page - 1) })}
          >
            Previous
          </button>
          <span className="status-chip status-info">
            Page {page} / {totalPages}
          </span>
          <button
            className="secondary-button"
            type="button"
            disabled={page >= totalPages}
            onClick={() => props.onStateChange({ ...props.state, page: Math.min(totalPages, page + 1) })}
          >
            Next
          </button>
        </div>
      </div>
    </div>
  );
}

function OptionalNumberField(props: {
  id: string;
  label: string;
  value?: number | null;
  onChange: (value: number | null) => void;
}) {
  return (
    <div>
      <label className="label" htmlFor={props.id}>
        {props.label}
      </label>
      <input
        id={props.id}
        className="input"
        type="number"
        min={0}
        step={1}
        value={props.value ?? ''}
        onChange={(event) => props.onChange(parseOptionalNumber(event.target.value))}
      />
    </div>
  );
}

function CheckboxField(props: { label: string; checked: boolean; onChange: (checked: boolean) => void }) {
  return (
    <label className="switch-line rounded-xl border border-border bg-white/80 px-3 py-2 text-sm text-muted-strong">
      <input type="checkbox" checked={props.checked} onChange={(event) => props.onChange(event.target.checked)} />
      <span>{props.label}</span>
    </label>
  );
}

export function QueueCreateEditPage({ accessToken, mode, queueId, onSaved, onCancel }: QueueCreateEditPageProps) {
  const actions = useQueueActions(accessToken);
  const queueDetailRequest = useQueueStore((state) => state.requests.queueDetail);
  const queueMutationRequest = useQueueStore((state) => state.requests.queueMutation);

  const [draft, setDraft] = useState<QueueEditorDraft>(() => createDraft());
  const [loadedQueue, setLoadedQueue] = useState<QueueModel | null>(null);
  const [pageError, setPageError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [agentPreview, setAgentPreview] = useState<AssignmentPreviewState>({
    filter: '',
    sortBy: 'extensionNumber',
    sortDescending: false,
    page: 1,
    pageSize: 8
  });
  const [managerPreview, setManagerPreview] = useState<AssignmentPreviewState>({
    filter: '',
    sortBy: 'extensionNumber',
    sortDescending: false,
    page: 1,
    pageSize: 8
  });

  useEffect(() => {
    if (!notice) {
      return;
    }

    const timer = window.setTimeout(() => setNotice(null), 3000);
    return () => window.clearTimeout(timer);
  }, [notice]);

  useEffect(() => {
    if (mode === 'create') {
      setLoadedQueue(null);
      setDraft(createDraft());
      setPageError(null);
      return;
    }

    if (!queueId || queueId <= 0) {
      setPageError('Queue ID is required for edit mode.');
      return;
    }

    let mounted = true;
    const load = async () => {
      try {
        const queue = await actions.loadQueue(queueId);
        if (!mounted) {
          return;
        }

        setLoadedQueue(queue);
        setDraft(draftFromQueue(queue));
        setAgentPreview((previous) => ({ ...previous, filter: '', page: 1 }));
        setManagerPreview((previous) => ({ ...previous, filter: '', page: 1 }));
        setPageError(null);
      } catch (error) {
        if (mounted) {
          setPageError(toErrorMessage(error, `Failed to load queue ${queueId}.`));
        }
      }
    };

    void load();
    return () => {
      mounted = false;
    };
  }, [accessToken, mode, queueId]);

  const parsedAgents = useMemo(
    () => parseAgentAssignments(draft.agentsText, loadedQueue?.agents ?? []),
    [draft.agentsText, loadedQueue?.agents]
  );
  const parsedManagers = useMemo(
    () => parseManagerAssignments(draft.managersText, loadedQueue?.managers ?? []),
    [draft.managersText, loadedQueue?.managers]
  );

  const busy = queueDetailRequest.loading || queueMutationRequest.loading;
  const errorMessage = pageError ?? queueDetailRequest.errorMessage ?? queueMutationRequest.errorMessage;

  const setSetting = <K extends keyof QueueSettingsModel>(key: K, value: QueueSettingsModel[K]) => {
    setDraft((previous) => ({
      ...previous,
      settings: {
        ...previous.settings,
        [key]: value
      }
    }));
  };

  const submit = async () => {
    setPageError(null);
    setNotice(null);

    const name = draft.name.trim();
    const queueNumber = draft.queueNumber.trim();
    if (!name) {
      setPageError('Queue name is required.');
      return;
    }

    if (mode === 'create' && !queueNumber) {
      setPageError('Queue number is required.');
      return;
    }

    if (parsedAgents.issues.length > 0 || parsedManagers.issues.length > 0) {
      setPageError('Fix assignment parsing issues before saving.');
      return;
    }

    const settings: QueueSettingsModel = {
      ...cloneSettings(draft.settings),
      notifyCodes: parseNotifyCodes(draft.notifyCodesText)
    };

    try {
      if (mode === 'create') {
        const request: CreateQueueRequestModel = {
          number: queueNumber,
          name,
          settings,
          agents: parsedAgents.items,
          managers: parsedManagers.items
        };
        const created = await actions.createQueue(request);
        setLoadedQueue(created);
        setDraft(draftFromQueue(created));
        setNotice(`Queue ${created.queueNumber} created.`);
        onSaved?.(created);
        return;
      }

      if (!queueId || queueId <= 0) {
        setPageError('Queue ID is required for edit mode.');
        return;
      }

      const request: UpdateQueueRequestModel = {
        name,
        settings,
        agents: parsedAgents.items,
        managers: parsedManagers.items,
        replaceAgents: draft.replaceAgents,
        replaceManagers: draft.replaceManagers
      };
      const updated = await actions.updateQueue(queueId, request);
      setLoadedQueue(updated);
      setDraft(draftFromQueue(updated));
      setNotice(`Queue ${updated.queueNumber} updated.`);
      onSaved?.(updated);
    } catch (error) {
      setPageError(toErrorMessage(error, mode === 'create' ? 'Create queue failed.' : 'Update queue failed.'));
    }
  };

  const resetForm = () => {
    if (mode === 'edit' && loadedQueue) {
      setDraft(draftFromQueue(loadedQueue));
      setNotice('Form reset to saved queue values.');
      return;
    }

    setDraft(createDraft());
    setNotice('Create form reset.');
  };

  return (
    <section className="supervisor-page-stack">
      <article className="card">
        <div className="analytics-header">
          <div>
            <h3>{mode === 'create' ? 'Queues / Create' : 'Queues / Edit'}</h3>
            <p className="history-summary">
              Typed queue form with assignment parsing, validation feedback, and preview sorting/pagination.
            </p>
          </div>
          <div className="status-strip">
            <span className={`status-chip ${busy ? 'status-waiting' : 'status-active'}`}>{busy ? 'Working' : 'Ready'}</span>
            <span className="status-chip status-info">Mode: {mode}</span>
            {loadedQueue && <span className="status-chip status-info">Queue #{loadedQueue.queueNumber}</span>}
          </div>
        </div>
        {(errorMessage || notice) && (
          <div className="mt-3 grid gap-2">
            {errorMessage && <div className="banner-error">{errorMessage}</div>}
            {notice && <div className="banner-success">{notice}</div>}
          </div>
        )}
      </article>

      <article className="card">
        <div className="form-grid">
          <div className="grid-two">
            <div>
              <label className="label" htmlFor="queue-edit-number">Queue Number</label>
              <input
                id="queue-edit-number"
                className="input"
                value={draft.queueNumber}
                disabled={mode === 'edit'}
                placeholder="e.g. 800"
                onChange={(event) => setDraft((previous) => ({ ...previous, queueNumber: event.target.value }))}
              />
            </div>
            <div>
              <label className="label" htmlFor="queue-edit-name">Queue Name</label>
              <input
                id="queue-edit-name"
                className="input"
                value={draft.name}
                placeholder="Support Queue"
                onChange={(event) => setDraft((previous) => ({ ...previous, name: event.target.value }))}
              />
            </div>
          </div>

          <div className="grid-three">
            <OptionalNumberField
              id="queue-sla-time"
              label="SLA Time (sec)"
              value={draft.settings.slaTimeSec}
              onChange={(value) => setSetting('slaTimeSec', value)}
            />
            <OptionalNumberField
              id="queue-ring-timeout"
              label="Ring Timeout (sec)"
              value={draft.settings.ringTimeoutSec}
              onChange={(value) => setSetting('ringTimeoutSec', value)}
            />
            <OptionalNumberField
              id="queue-max-callers"
              label="Max Callers In Queue"
              value={draft.settings.maxCallersInQueue}
              onChange={(value) => setSetting('maxCallersInQueue', value)}
            />
          </div>

          <div className="grid-three">
            <OptionalNumberField
              id="queue-wrapup-time"
              label="Wrap Up Time (sec)"
              value={draft.settings.wrapUpTimeSec}
              onChange={(value) => setSetting('wrapUpTimeSec', value)}
            />
            <OptionalNumberField
              id="queue-master-timeout"
              label="Master Timeout (sec)"
              value={draft.settings.masterTimeoutSec}
              onChange={(value) => setSetting('masterTimeoutSec', value)}
            />
            <OptionalNumberField
              id="queue-announcement-interval"
              label="Announcement Interval (sec)"
              value={draft.settings.announcementIntervalSec}
              onChange={(value) => setSetting('announcementIntervalSec', value)}
            />
          </div>

          <div className="grid-three">
            <div>
              <label className="label" htmlFor="queue-notify-codes">Notify Codes</label>
              <input
                id="queue-notify-codes"
                className="input"
                value={draft.notifyCodesText}
                placeholder="Comma separated"
                onChange={(event) => setDraft((previous) => ({ ...previous, notifyCodesText: event.target.value }))}
              />
            </div>
            <div>
              <label className="label" htmlFor="queue-recording-mode">Recording Mode</label>
              <input
                id="queue-recording-mode"
                className="input"
                value={draft.settings.recordingMode ?? ''}
                placeholder="Optional"
                onChange={(event) => setSetting('recordingMode', event.target.value.trim() || null)}
              />
            </div>
            <div>
              <label className="label" htmlFor="queue-polling-strategy">Polling Strategy</label>
              <input
                id="queue-polling-strategy"
                className="input"
                value={draft.settings.pollingStrategy ?? ''}
                placeholder="Optional"
                onChange={(event) => setSetting('pollingStrategy', event.target.value.trim() || null)}
              />
            </div>
          </div>

          <div className="grid-three">
            <CheckboxField
              label="Announce Queue Position"
              checked={Boolean(draft.settings.announceQueuePosition)}
              onChange={(checked) => setSetting('announceQueuePosition', checked)}
            />
            <CheckboxField
              label="Play Full Prompt"
              checked={Boolean(draft.settings.playFullPrompt)}
              onChange={(checked) => setSetting('playFullPrompt', checked)}
            />
            <CheckboxField
              label="Priority Queue"
              checked={Boolean(draft.settings.priorityQueue)}
              onChange={(checked) => setSetting('priorityQueue', checked)}
            />
          </div>

          {mode === 'edit' && (
            <div className="grid-two">
              <CheckboxField
                label="Replace Agents On Update"
                checked={draft.replaceAgents}
                onChange={(checked) => setDraft((previous) => ({ ...previous, replaceAgents: checked }))}
              />
              <CheckboxField
                label="Replace Managers On Update"
                checked={draft.replaceManagers}
                onChange={(checked) => setDraft((previous) => ({ ...previous, replaceManagers: checked }))}
              />
            </div>
          )}
        </div>
      </article>

      <article className="card">
        <div className="form-grid">
          <div className="grid-two">
            <div>
              <label className="label" htmlFor="queue-agent-lines">
                Agents (`extension|displayName|skillGroup`, one line each)
              </label>
              <textarea
                id="queue-agent-lines"
                className="textarea"
                value={draft.agentsText}
                placeholder="101|Alice Smith|Tier1"
                onChange={(event) => setDraft((previous) => ({ ...previous, agentsText: event.target.value }))}
              />
            </div>
            <div>
              <label className="label" htmlFor="queue-manager-lines">
                Managers (`extension|displayName`, one line each)
              </label>
              <textarea
                id="queue-manager-lines"
                className="textarea"
                value={draft.managersText}
                placeholder="201|Supervisor Jane"
                onChange={(event) => setDraft((previous) => ({ ...previous, managersText: event.target.value }))}
              />
            </div>
          </div>

          <div className="grid-two">
            <AssignmentPreview
              title="Agent Preview"
              subtitle="Filtered, sorted, paginated preview of parsed agent assignments."
              items={parsedAgents.items}
              issues={parsedAgents.issues}
              state={agentPreview}
              onStateChange={setAgentPreview}
              renderExtra={(item) => ('skillGroup' in item && item.skillGroup ? `Skill ${item.skillGroup}` : null)}
            />
            <AssignmentPreview
              title="Manager Preview"
              subtitle="Filtered, sorted, paginated preview of parsed manager assignments."
              items={parsedManagers.items}
              issues={parsedManagers.issues}
              state={managerPreview}
              onStateChange={setManagerPreview}
            />
          </div>
        </div>
      </article>

      <article className="card">
        <div className="analytics-header">
          <div>
            <h4>Submit</h4>
            <p className="history-summary">Save a strongly-typed request payload aligned to Batch 1 queue contracts.</p>
          </div>
          <div className="status-strip">
            <span className="status-chip status-info">Notify Codes: {parseNotifyCodes(draft.notifyCodesText).length}</span>
            <span className="status-chip status-info">Agents: {parsedAgents.items.length}</span>
            <span className="status-chip status-info">Managers: {parsedManagers.items.length}</span>
          </div>
        </div>

        <div className="mt-3 grid gap-2 md:grid-cols-3">
          <button className="primary-button" type="button" disabled={busy} onClick={() => void submit()}>
            {mode === 'create' ? 'Create Queue' : 'Save Queue'}
          </button>
          <button className="secondary-button" type="button" disabled={busy} onClick={resetForm}>
            Reset Form
          </button>
          <button className="secondary-button" type="button" disabled={busy} onClick={onCancel}>
            Cancel
          </button>
        </div>
      </article>
    </section>
  );
}
