import { useEffect, useMemo, useRef, useState } from 'react';
import { CallList } from '../components/CallList';
import { DialPad } from '../components/DialPad';
import { IncomingCallModal } from '../components/IncomingCallModal';
import type {
  BrowserCallView,
  SessionSnapshotResponse,
  SipCallInfo,
  SipRegistrationState,
  SoftphoneEventEnvelope
} from '../domain/softphone';
import type { AudioDeviceOption } from '../hooks/useSoftphoneSession';

interface SoftphonePageProps {
  snapshot: SessionSnapshotResponse;
  browserCalls: BrowserCallView[];
  events: SoftphoneEventEnvelope[];
  busy: boolean;
  pbxBase: string;
  onMakeOutgoingCall: (destination: string) => Promise<void>;
  onMakePbxOutgoingCall: (destination: string) => Promise<void>;
  onAnswer: (callId: string) => Promise<void>;
  onReject: (callId: string) => Promise<void>;
  onEnd: (callId: string) => Promise<void>;
  onSetActivePbxDevice: (deviceId: string) => Promise<void>;
  onRequestMicrophone: () => Promise<void>;
  microphonePermission: 'unknown' | 'granted' | 'denied';
  muted: boolean;
  onMutedChange: (muted: boolean) => void;
  microphoneDevices: AudioDeviceOption[];
  speakerDevices: AudioDeviceOption[];
  selectedMicrophoneDeviceId: string;
  selectedSpeakerDeviceId: string;
  onSelectMicrophoneDevice: (deviceId: string) => Promise<void>;
  onSelectSpeakerDevice: (deviceId: string) => Promise<void>;
  onRefreshDevices: () => Promise<void>;
  onRemoteAudioRef: (element: HTMLAudioElement | null) => void;
  speakerSelectionSupported: boolean;
  sipRegistrationState: SipRegistrationState;
  sipRegistrationError: string | null;
  sipCallInfo: SipCallInfo;
  onAnswerSipCall: () => Promise<void>;
  onRejectSipCall: () => Promise<void>;
  onHangupSipCall: () => Promise<void>;
  onSipRemoteAudioRef: (element: HTMLAudioElement | null) => void;
}

interface ContactDraft {
  fullName: string;
  phoneNumber: string;
  email: string;
  company: string;
  relatedCase: string;
}

type RequestStatus = 'New' | 'In Progress' | 'Waiting' | 'Resolved';
type RequestPriority = 'Low' | 'Medium' | 'High' | 'Critical';

interface RequestDraft {
  subject: string;
  status: RequestStatus;
  priority: RequestPriority;
  assignee: string;
}

function formatElapsed(fromIso: string): string {
  const startMs = Date.parse(fromIso);
  if (Number.isNaN(startMs)) {
    return '--:--';
  }

  const totalSeconds = Math.max(0, Math.floor((Date.now() - startMs) / 1000));
  const minutes = Math.floor(totalSeconds / 60)
    .toString()
    .padStart(2, '0');
  const seconds = (totalSeconds % 60).toString().padStart(2, '0');
  return `${minutes}:${seconds}`;
}

function eventTimeLabel(iso: string): string {
  const value = Date.parse(iso);
  if (Number.isNaN(value)) {
    return iso;
  }

  return new Date(value).toLocaleTimeString();
}

function secondsSince(iso: string): number {
  const value = Date.parse(iso);
  if (Number.isNaN(value)) {
    return 0;
  }

  return Math.max(0, Math.floor((Date.now() - value) / 1000));
}

function formatDuration(seconds: number): string {
  const minutes = Math.floor(seconds / 60)
    .toString()
    .padStart(2, '0');
  const remainder = (seconds % 60).toString().padStart(2, '0');
  return `${minutes}:${remainder}`;
}

function normalizeDialInput(value: string): string {
  return value.replace(/[^\d*#+]/g, '');
}

export function SoftphonePage({
  snapshot,
  browserCalls,
  events,
  busy,
  pbxBase,
  onMakeOutgoingCall,
  onMakePbxOutgoingCall,
  onAnswer,
  onReject,
  onEnd,
  onSetActivePbxDevice,
  onRequestMicrophone,
  microphonePermission,
  muted,
  onMutedChange,
  microphoneDevices,
  speakerDevices,
  selectedMicrophoneDeviceId,
  selectedSpeakerDeviceId,
  onSelectMicrophoneDevice,
  onSelectSpeakerDevice,
  onRefreshDevices,
  onRemoteAudioRef,
  speakerSelectionSupported,
  sipRegistrationState,
  sipRegistrationError,
  sipCallInfo,
  onAnswerSipCall,
  onRejectSipCall,
  onHangupSipCall,
  onSipRemoteAudioRef
}: SoftphonePageProps) {
  const [dialValue, setDialValue] = useState('');
  const [, setTick] = useState(0);
  const pendingOutgoingRef = useRef(false);
  const dialInputRef = useRef<HTMLInputElement | null>(null);
  const [isSoftphoneOpen, setIsSoftphoneOpen] = useState(true);
  const [contactDraft, setContactDraft] = useState<ContactDraft>({
    fullName: '',
    phoneNumber: '',
    email: '',
    company: '',
    relatedCase: ''
  });
  const [requestDraft, setRequestDraft] = useState<RequestDraft>({
    subject: '',
    status: 'New',
    priority: 'Medium',
    assignee: snapshot.username
  });
  const [liveNoteInput, setLiveNoteInput] = useState('');
  const [liveNotes, setLiveNotes] = useState<string[]>([]);
  const [formNotice, setFormNotice] = useState<string | null>(null);

  useEffect(() => {
    const timer = window.setInterval(() => setTick((prev) => prev + 1), 1000);
    return () => window.clearInterval(timer);
  }, []);

  useEffect(() => {
    if (!isSoftphoneOpen) {
      return;
    }

    const onKeyDown = (event: KeyboardEvent) => {
      if (event.altKey || event.ctrlKey || event.metaKey) {
        return;
      }

      const target = event.target as HTMLElement | null;
      const isEditable =
        target instanceof HTMLInputElement || target instanceof HTMLTextAreaElement || Boolean(target?.isContentEditable);
      const isDialInputFocused = target === dialInputRef.current;

      if (isEditable && !isDialInputFocused) {
        return;
      }

      if (/^[0-9*#+]$/.test(event.key)) {
        event.preventDefault();
        setDialValue((previous) => `${previous}${event.key}`);
        return;
      }

      if (event.key === 'Backspace') {
        event.preventDefault();
        setDialValue((previous) => previous.slice(0, -1));
      }
    };

    window.addEventListener('keydown', onKeyDown);
    return () => window.removeEventListener('keydown', onKeyDown);
  }, [isSoftphoneOpen]);

  const connectedCall = useMemo(
    () => browserCalls.find((call) => call.status === 'Connected'),
    [browserCalls]
  );

  const activeCall = useMemo(
    () => browserCalls.find((call) => call.status !== 'Ended') ?? browserCalls[0],
    [browserCalls]
  );

  const incomingCall = useMemo(
    () => browserCalls.find((call) => call.status === 'Ringing' && call.isIncoming) ?? null,
    [browserCalls]
  );

  const pbxHost = useMemo(() => {
    const normalized = pbxBase.trim();
    if (normalized.length === 0) {
      return 'N/A';
    }

    try {
      return new URL(normalized.startsWith('http') ? normalized : `https://${normalized}`).host;
    } catch {
      return normalized;
    }
  }, [pbxBase]);

  const monitorStats = useMemo(() => {
    const activeCalls = browserCalls.filter((call) => call.status !== 'Ended');
    const waitingCalls = activeCalls.filter((call) => call.status === 'Ringing');
    const missedCalls = browserCalls.filter(
      (call) => call.status === 'Ended' && call.isIncoming && !call.answeredAtUtc
    );
    const longestWaitSeconds = waitingCalls.reduce(
      (currentLongest, call) => Math.max(currentLongest, secondsSince(call.createdAtUtc)),
      0
    );

    let queueLoad: 'Low' | 'Medium' | 'High' = 'Low';
    if (waitingCalls.length >= 4) {
      queueLoad = 'High';
    } else if (waitingCalls.length >= 2) {
      queueLoad = 'Medium';
    }

    return {
      activeCalls: activeCalls.length,
      activeAgents: snapshot.wsConnected ? 1 : 0,
      queueLoad,
      waitingCalls: waitingCalls.length,
      missedCalls: missedCalls.length,
      longestWaitSeconds
    };
  }, [browserCalls, snapshot.wsConnected]);

  const queueLoadClass = useMemo(() => {
    if (monitorStats.queueLoad === 'High') {
      return 'status-critical';
    }

    if (monitorStats.queueLoad === 'Medium') {
      return 'status-waiting';
    }

    return 'status-active';
  }, [monitorStats.queueLoad]);

  const microphoneLabel = useMemo(() => {
    switch (microphonePermission) {
      case 'granted':
        return 'Granted';
      case 'denied':
        return 'Denied';
      case 'unknown':
      default:
        return 'Not Requested';
    }
  }, [microphonePermission]);

  const sipRegistrationLabel = useMemo(() => {
    switch (sipRegistrationState) {
      case 'Registered':
        return 'Registered';
      case 'Connecting':
        return 'Connecting';
      case 'Failed':
        return 'Failed';
      case 'Disabled':
      default:
        return 'Disabled';
    }
  }, [sipRegistrationState]);

  const connectedSince = connectedCall?.answeredAtUtc ?? connectedCall?.createdAtUtc ?? null;
  const disableOutbound = busy || microphonePermission === 'denied';
  const disableAnswer = busy || microphonePermission === 'denied';
  const sipRinging = sipCallInfo.state === 'Ringing';
  const sipConnected = sipCallInfo.state === 'Connected';
  const pbxDevices = useMemo(
    () => snapshot.devices.filter((device) => (device.deviceId ?? '').trim().length > 0),
    [snapshot.devices]
  );

  const runOutgoingSubmit = async (operation: (destination: string) => Promise<void>) => {
    const destination = dialValue.trim();
    if (!destination || pendingOutgoingRef.current) {
      return;
    }

    pendingOutgoingRef.current = true;
    try {
      await operation(destination);
      setDialValue('');
    } finally {
      pendingOutgoingRef.current = false;
    }
  };

  const onOutgoingSubmit = async () => {
    await runOutgoingSubmit(onMakeOutgoingCall);
  };

  const onPbxOutgoingSubmit = async () => {
    await runOutgoingSubmit(onMakePbxOutgoingCall);
  };

  const onAddLiveNote = () => {
    const value = liveNoteInput.trim();
    if (!value) {
      return;
    }

    const label = `[${new Date().toLocaleTimeString()}] ${value}`;
    setLiveNotes((previous) => [label, ...previous].slice(0, 8));
    setLiveNoteInput('');
  };

  const onSaveContactProfile = () => {
    setFormNotice('Contact profile updated (frontend preview).');
    window.setTimeout(() => setFormNotice(null), 2400);
  };

  const onCreateRequest = () => {
    if (requestDraft.subject.trim().length === 0) {
      return;
    }

    setFormNotice(`Request created: ${requestDraft.priority} priority, assigned to ${requestDraft.assignee || 'Unassigned'}.`);
    setRequestDraft((previous) => ({
      ...previous,
      subject: ''
    }));
    window.setTimeout(() => setFormNotice(null), 2600);
  };

  return (
    <section className="grid gap-6">
      <div className="grid gap-6 xl:grid-cols-[minmax(0,1.35fr)_22rem]">
        <article className="card">
          <div className="flex flex-wrap items-start justify-between gap-4">
            <div>
              <p className="text-xs font-semibold uppercase tracking-wider text-muted">Operator Workspace</p>
              <h3 className="mt-1 font-display text-xl font-semibold text-ink">Call Control Console</h3>
              <p className="mt-1 text-sm text-muted">Voice bridge, audio routing, and CRM context in one workspace.</p>
            </div>
            <div className="status-strip">
              <span className="status-chip status-info">User: {snapshot.username}</span>
              <span className="status-chip status-info">Extension: {snapshot.selectedExtensionDn}</span>
              <span className="status-chip status-info">PBX: {pbxHost}</span>
              <span className={`status-chip ${snapshot.wsConnected ? 'status-active' : 'status-waiting'}`}>
                Signal: {snapshot.wsConnected ? 'Connected' : 'Reconnecting'}
              </span>
              <span className={`status-chip ${microphonePermission === 'denied' ? 'status-critical' : 'status-info'}`}>
                Mic: {microphoneLabel}
              </span>
              <span className={`status-chip ${sipRegistrationState === 'Registered' ? 'status-active' : 'status-waiting'}`}>
                3CX Voice: {sipRegistrationLabel}
              </span>
            </div>
          </div>

          {sipRegistrationError && (
            <div className="banner-error mt-4">
              <strong>Voice bridge:</strong> {sipRegistrationError}
            </div>
          )}
          {formNotice && <div className="banner-success mt-4">{formNotice}</div>}

          <div className="mt-4 grid gap-4 lg:grid-cols-2">
            <section className="rounded-2xl border border-border bg-white/85 p-4">
              <h4 className="font-display text-base font-semibold text-ink">3CX Call Control Audio</h4>
              <div className="mt-2 grid gap-1 text-sm text-muted">
                <span>Status: {sipCallInfo.state}</span>
                <span>Remote: {sipCallInfo.remoteIdentity || '-'}</span>
              </div>
              <div className="call-actions">
                <button className="primary-button" type="button" disabled={busy || !sipRinging} onClick={() => void onAnswerSipCall()}>
                  Answer
                </button>
                <button className="danger-button" type="button" disabled={busy || !sipRinging} onClick={() => void onRejectSipCall()}>
                  Reject
                </button>
                <button className="secondary-button" type="button" disabled={busy || !sipConnected} onClick={() => void onHangupSipCall()}>
                  Hangup
                </button>
              </div>
            </section>

            <section className="rounded-2xl border border-border bg-white/85 p-4">
              <div className="form-grid">
                <div>
                  <label className="label" htmlFor="activePbxDevice">
                    Active 3CX device
                  </label>
                  <select
                    id="activePbxDevice"
                    className="select"
                    value={snapshot.activeDeviceId ?? ''}
                    onChange={(event) => void onSetActivePbxDevice(event.target.value)}
                  >
                    {pbxDevices.length === 0 && <option value="">No 3CX device</option>}
                    {pbxDevices.map((device) => {
                      const deviceId = (device.deviceId ?? '').trim();
                      const userAgent = (device.userAgent ?? '').trim();
                      const deviceDn = (device.dn ?? '').trim();
                      const labelParts = [userAgent, deviceDn].filter((value) => value.length > 0);
                      const label = labelParts.length > 0 ? `${labelParts.join(' | ')} (${deviceId})` : deviceId;

                      return (
                        <option key={deviceId} value={deviceId}>
                          {label}
                        </option>
                      );
                    })}
                  </select>
                </div>

                <div className="grid gap-3 md:grid-cols-2">
                  <div>
                    <label className="label" htmlFor="microphoneDevice">
                      Microphone
                    </label>
                    <select
                      id="microphoneDevice"
                      className="select"
                      value={selectedMicrophoneDeviceId}
                      onChange={(event) => void onSelectMicrophoneDevice(event.target.value)}
                    >
                      {microphoneDevices.length === 0 && <option value="">No microphone device</option>}
                      {microphoneDevices.map((device) => (
                        <option key={device.deviceId} value={device.deviceId}>
                          {device.label}
                        </option>
                      ))}
                    </select>
                  </div>

                  <div>
                    <label className="label" htmlFor="speakerDevice">
                      Speaker
                    </label>
                    <select
                      id="speakerDevice"
                      className="select"
                      value={selectedSpeakerDeviceId}
                      onChange={(event) => void onSelectSpeakerDevice(event.target.value)}
                    >
                      {speakerDevices.length === 0 && <option value="">No speaker device</option>}
                      {speakerDevices.map((device) => (
                        <option key={device.deviceId} value={device.deviceId}>
                          {device.label}
                        </option>
                      ))}
                    </select>
                  </div>
                </div>

                {!speakerSelectionSupported && (
                  <p className="rounded-xl border border-warning-500/30 bg-warning-100 px-3 py-2 text-xs text-warning-700">
                    Output switch is not supported in this browser. Default speaker remains active.
                  </p>
                )}

                <div className="grid gap-2 md:grid-cols-2">
                  <button className="secondary-button" type="button" disabled={busy} onClick={() => void onRequestMicrophone()}>
                    Enable microphone
                  </button>
                  <button className="secondary-button" type="button" disabled={busy} onClick={() => void onRefreshDevices()}>
                    Refresh audio devices
                  </button>
                </div>
              </div>
            </section>
          </div>
        </article>

        <aside className="space-y-4 xl:sticky xl:top-24 xl:self-start">
          <article className="card border-primary-200/80 bg-gradient-to-b from-white to-surface-2">
            <button
              className="flex w-full items-center justify-between rounded-2xl border border-primary-200 bg-primary-50 px-3 py-2 text-left transition hover:border-primary-300 hover:bg-primary-100"
              type="button"
              onClick={() => setIsSoftphoneOpen((previous) => !previous)}
              aria-expanded={isSoftphoneOpen}
              aria-controls="softphone-body"
            >
              <span>
                <span className="block text-xs font-semibold uppercase tracking-wider text-primary-700">Compact Softphone</span>
                <span className="mt-1 block font-display text-base font-semibold text-ink">
                  {isSoftphoneOpen ? 'Dialer Open' : 'Dialer Closed'}
                </span>
              </span>
              <span
                className={`inline-flex h-8 w-8 items-center justify-center rounded-full border border-primary-200 bg-white text-lg text-primary-700 transition-transform duration-200 ${
                  isSoftphoneOpen ? 'rotate-180' : 'rotate-0'
                }`}
                aria-hidden="true"
              >
                ^
              </span>
            </button>

            <div
              id="softphone-body"
              className={`overflow-hidden transition-all duration-300 ease-out ${
                isSoftphoneOpen ? 'mt-4 max-h-[700px] opacity-100' : 'mt-0 max-h-0 opacity-0'
              }`}
            >
              <div className="space-y-3 border-t border-border pt-4">
                <label className="label" htmlFor="dial-input">
                  Dial number
                </label>
                <input
                  id="dial-input"
                  ref={dialInputRef}
                  className="input"
                  value={dialValue}
                  placeholder="Type number or use keypad"
                  inputMode="tel"
                  onChange={(event) => setDialValue(normalizeDialInput(event.target.value))}
                  onKeyDown={(event) => {
                    if (event.key === 'Enter') {
                      event.preventDefault();
                      void onOutgoingSubmit();
                    }
                  }}
                />

                {connectedSince ? (
                  <p className="text-xs font-medium text-muted">Connected for {formatElapsed(connectedSince)}</p>
                ) : (
                  <p className="text-xs text-muted">Use keyboard digits, `*`, `#`, `+`, and Backspace.</p>
                )}

                <div className="grid grid-cols-2 gap-2">
                  <button className="secondary-button" type="button" onClick={() => setDialValue((previous) => previous.slice(0, -1))}>
                    Backspace
                  </button>
                  <button className="secondary-button" type="button" onClick={() => setDialValue('')}>
                    Clear
                  </button>
                </div>

                <DialPad onDigit={(digit) => setDialValue((previous) => `${previous}${digit}`)} disabled={busy} />

                <div className="grid gap-2">
                  <button className="primary-button" type="button" disabled={disableOutbound} onClick={() => void onOutgoingSubmit()}>
                    Call in-app
                  </button>
                  <button className="primary-button" type="button" disabled={disableOutbound} onClick={() => void onPbxOutgoingSubmit()}>
                    Call via 3CX
                  </button>
                  <button
                    className="secondary-button"
                    type="button"
                    disabled={busy || microphonePermission !== 'granted'}
                    onClick={() => onMutedChange(!muted)}
                  >
                    {muted ? 'Unmute microphone' : 'Mute microphone'}
                  </button>
                  <button
                    className="danger-button"
                    type="button"
                    disabled={busy || !activeCall}
                    onClick={() => activeCall && void onEnd(activeCall.callId)}
                  >
                    End active call
                  </button>
                </div>
              </div>
            </div>
          </article>

          <article className="card">
            <h3 className="font-display text-lg font-semibold text-ink">Queue Snapshot</h3>
            <div className="mt-3 grid gap-2 sm:grid-cols-2 xl:grid-cols-1">
              <div className="rounded-2xl border border-border bg-white/80 p-3">
                <span className="text-xs font-semibold uppercase tracking-wide text-muted">Active calls</span>
                <strong className="mt-1 block font-display text-xl font-semibold text-ink">{monitorStats.activeCalls}</strong>
              </div>
              <div className="rounded-2xl border border-border bg-white/80 p-3">
                <span className="text-xs font-semibold uppercase tracking-wide text-muted">Active agents</span>
                <strong className="mt-1 block font-display text-xl font-semibold text-ink">{monitorStats.activeAgents}</strong>
              </div>
              <div className="rounded-2xl border border-border bg-white/80 p-3">
                <span className="text-xs font-semibold uppercase tracking-wide text-muted">Queue load</span>
                <strong className={`mt-1 block text-base font-semibold ${queueLoadClass}`}>{monitorStats.queueLoad}</strong>
              </div>
              <div className="rounded-2xl border border-border bg-white/80 p-3">
                <span className="text-xs font-semibold uppercase tracking-wide text-muted">Waiting calls</span>
                <strong className="mt-1 block font-display text-xl font-semibold text-ink">{monitorStats.waitingCalls}</strong>
              </div>
              <div className="rounded-2xl border border-border bg-white/80 p-3">
                <span className="text-xs font-semibold uppercase tracking-wide text-muted">Missed calls</span>
                <strong className="mt-1 block font-display text-xl font-semibold text-danger-700">{monitorStats.missedCalls}</strong>
              </div>
              <div className="rounded-2xl border border-border bg-white/80 p-3">
                <span className="text-xs font-semibold uppercase tracking-wide text-muted">Longest wait</span>
                <strong className="mt-1 block font-display text-xl font-semibold text-ink">
                  {formatDuration(monitorStats.longestWaitSeconds)}
                </strong>
              </div>
            </div>
          </article>
        </aside>
      </div>

      <div className="grid gap-6 xl:grid-cols-2">
        <article className="card">
          <h3 className="font-display text-lg font-semibold text-ink">CRM Contact Profile</h3>
          <p className="section-note mt-1">Quickly update caller information while the conversation is active.</p>
          <div className="form-grid mt-4">
            <div className="grid-two">
              <input
                className="input"
                placeholder="Full name"
                value={contactDraft.fullName}
                onChange={(event) => setContactDraft((previous) => ({ ...previous, fullName: event.target.value }))}
              />
              <input
                className="input"
                placeholder="Phone number"
                value={contactDraft.phoneNumber}
                onChange={(event) => setContactDraft((previous) => ({ ...previous, phoneNumber: event.target.value }))}
              />
            </div>
            <div className="grid-two">
              <input
                className="input"
                placeholder="Email"
                type="email"
                value={contactDraft.email}
                onChange={(event) => setContactDraft((previous) => ({ ...previous, email: event.target.value }))}
              />
              <input
                className="input"
                placeholder="Company"
                value={contactDraft.company}
                onChange={(event) => setContactDraft((previous) => ({ ...previous, company: event.target.value }))}
              />
            </div>
            <input
              className="input"
              placeholder="Related request or history note"
              value={contactDraft.relatedCase}
              onChange={(event) => setContactDraft((previous) => ({ ...previous, relatedCase: event.target.value }))}
            />
            <button className="secondary-button w-fit" type="button" onClick={onSaveContactProfile}>
              Save quick edits
            </button>
          </div>
        </article>

        <article className="card">
          <h3 className="font-display text-lg font-semibold text-ink">Request Registration</h3>
          <p className="section-note mt-1">Create and classify request records during active calls.</p>
          <div className="form-grid mt-4">
            <input
              className="input"
              placeholder="Request subject"
              value={requestDraft.subject}
              onChange={(event) => setRequestDraft((previous) => ({ ...previous, subject: event.target.value }))}
            />
            <div className="grid-two">
              <select
                className="select"
                value={requestDraft.status}
                onChange={(event) => setRequestDraft((previous) => ({ ...previous, status: event.target.value as RequestStatus }))}
              >
                <option value="New">New</option>
                <option value="In Progress">In Progress</option>
                <option value="Waiting">Waiting</option>
                <option value="Resolved">Resolved</option>
              </select>
              <select
                className="select"
                value={requestDraft.priority}
                onChange={(event) =>
                  setRequestDraft((previous) => ({ ...previous, priority: event.target.value as RequestPriority }))
                }
              >
                <option value="Low">Low priority</option>
                <option value="Medium">Medium priority</option>
                <option value="High">High priority</option>
                <option value="Critical">Critical</option>
              </select>
            </div>
            <input
              className="input"
              placeholder="Assignee"
              value={requestDraft.assignee}
              onChange={(event) => setRequestDraft((previous) => ({ ...previous, assignee: event.target.value }))}
            />
            <button className="primary-button w-fit" type="button" onClick={onCreateRequest}>
              Create request
            </button>
          </div>

          <div className="mt-4 grid gap-2 rounded-2xl border border-border bg-white/75 p-3">
            <label className="label mb-0" htmlFor="liveNoteInput">
              Live notes
            </label>
            <textarea
              id="liveNoteInput"
              className="textarea"
              placeholder="Write quick notes while handling the call."
              value={liveNoteInput}
              onChange={(event) => setLiveNoteInput(event.target.value)}
              rows={3}
            />
            <button className="secondary-button w-fit" type="button" onClick={onAddLiveNote}>
              Add note
            </button>
            {liveNotes.length > 0 && (
              <div className="notes-list">
                {liveNotes.map((note, index) => (
                  <div key={`${note}-${index}`} className="history-pill">
                    {note}
                  </div>
                ))}
              </div>
            )}
          </div>
        </article>
      </div>

      <div className="grid gap-6 xl:grid-cols-[minmax(0,1.1fr)_minmax(0,0.9fr)]">
        <article className="card">
          <h3 className="font-display text-lg font-semibold text-ink">Live Calls</h3>
          <p className="section-note mt-1">Answer, reject, or end calls with clear status visibility.</p>
          <div className="mt-4">
            <CallList calls={browserCalls} busy={disableAnswer} onAnswer={onAnswer} onReject={onReject} onEnd={onEnd} />
          </div>
        </article>

        <article className="card">
          <h3 className="font-display text-lg font-semibold text-ink">Event Stream</h3>
          <p className="section-note mt-1">Recent voice and session events in chronological order.</p>
          <section className="event-log mt-4">
            {events.length > 0 ? (
              events.map((event, index) => (
                <div className="event-row" key={`${event.eventType}-${event.occurredAtUtc}-${index}`}>
                  <strong className="text-ink">{eventTimeLabel(event.occurredAtUtc)}</strong>
                  <span>{event.eventType}</span>
                </div>
              ))
            ) : (
              <p className="text-sm text-muted">No events recorded yet.</p>
            )}
          </section>
        </article>
      </div>

      <audio ref={onRemoteAudioRef} autoPlay playsInline hidden />
      <audio ref={onSipRemoteAudioRef} autoPlay playsInline hidden />

      {incomingCall && <IncomingCallModal call={incomingCall} busy={disableAnswer} onAnswer={onAnswer} onReject={onReject} />}
    </section>
  );
}
