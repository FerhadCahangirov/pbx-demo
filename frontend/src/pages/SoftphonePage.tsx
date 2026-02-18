import { useEffect, useMemo, useRef, useState } from 'react';
import { CallList } from '../components/CallList';
import { DialPad } from '../components/DialPad';
import { IncomingCallModal } from '../components/IncomingCallModal';
import type { BrowserCallView, SessionSnapshotResponse, SoftphoneEventEnvelope } from '../domain/softphone';
import type { AudioDeviceOption } from '../hooks/useSoftphoneSession';

interface SoftphonePageProps {
  snapshot: SessionSnapshotResponse;
  browserCalls: BrowserCallView[];
  events: SoftphoneEventEnvelope[];
  busy: boolean;
  pbxBase: string;
  onMakeOutgoingCall: (destination: string) => Promise<void>;
  onAnswer: (callId: string) => Promise<void>;
  onReject: (callId: string) => Promise<void>;
  onEnd: (callId: string) => Promise<void>;
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

export function SoftphonePage({
  snapshot,
  browserCalls,
  events,
  busy,
  pbxBase,
  onMakeOutgoingCall,
  onAnswer,
  onReject,
  onEnd,
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
  speakerSelectionSupported
}: SoftphonePageProps) {
  const [dialValue, setDialValue] = useState('');
  const [, setTick] = useState(0);
  const pendingOutgoingRef = useRef(false);

  useEffect(() => {
    const timer = window.setInterval(() => setTick((prev) => prev + 1), 1000);
    return () => window.clearInterval(timer);
  }, []);

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

  const onOutgoingSubmit = async () => {
    const destination = dialValue.trim();
    if (!destination || pendingOutgoingRef.current) {
      return;
    }

    pendingOutgoingRef.current = true;
    try {
      await onMakeOutgoingCall(destination);
      setDialValue('');
    } finally {
      pendingOutgoingRef.current = false;
    }
  };

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

  const connectedSince = connectedCall?.answeredAtUtc ?? connectedCall?.createdAtUtc ?? null;
  const disableOutbound = busy || microphonePermission === 'denied';
  const disableAnswer = busy || microphonePermission === 'denied';

  return (
    <section className="softphone-layout">
      <article className="card">
        <div className="status-strip">
          <span className="pill">User: {snapshot.username}</span>
          <span className="pill">Extension: {snapshot.selectedExtensionDn}</span>
          <span className="pill">PBX: {pbxHost}</span>
          <span className="pill">Signal: {snapshot.wsConnected ? 'Connected' : 'Reconnecting...'}</span>
          <span className="pill">Mic Permission: {microphoneLabel}</span>
        </div>

        <div className="form-grid">
          <div className="grid-two">
            <button className="secondary-button" type="button" disabled={busy} onClick={() => void onRequestMicrophone()}>
              Enable Microphone
            </button>
            <button className="secondary-button" type="button" disabled={busy} onClick={() => void onRefreshDevices()}>
              Refresh Audio Devices
            </button>
          </div>

          <div className="grid-two">
            <div>
              <label className="label" htmlFor="microphoneDevice">
                Microphone Device
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
                Speaker Device
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
              {!speakerSelectionSupported && (
                <small>This browser does not allow output-device switching (`setSinkId`). Default speaker is used.</small>
              )}
            </div>
          </div>

          <div className="grid-two">
            <button className="secondary-button" type="button" disabled={busy || microphonePermission !== 'granted'} onClick={() => onMutedChange(!muted)}>
              {muted ? 'Unmute' : 'Mute'}
            </button>
            <button className="secondary-button" type="button" disabled>
              {muted ? 'Mic Muted' : 'Mic Live'}
            </button>
          </div>

          <div className="dialer-display">
            <div>
              <div className="dialer-number">{dialValue || 'Enter extension'}</div>
              {connectedSince && <small>Connected for {formatElapsed(connectedSince)}</small>}
            </div>
            <button className="secondary-button" type="button" onClick={() => setDialValue((prev) => prev.slice(0, -1))}>
              Backspace
            </button>
          </div>

          <DialPad onDigit={(digit) => setDialValue((prev) => `${prev}${digit}`)} />

          <div className="grid-two">
            <button className="primary-button" type="button" disabled={disableOutbound} onClick={() => void onOutgoingSubmit()}>
              Call Extension
            </button>
            <button
              className="danger-button"
              type="button"
              disabled={busy || !activeCall}
              onClick={() => activeCall && void onEnd(activeCall.callId)}
            >
              End Active Call
            </button>
          </div>

          <audio ref={onRemoteAudioRef} autoPlay playsInline hidden />
        </div>
      </article>

      <article className="card">
        <h2>Live Calls</h2>
        <CallList calls={browserCalls} busy={disableAnswer} onAnswer={onAnswer} onReject={onReject} onEnd={onEnd} />

        <section className="event-log">
          {events.map((event, index) => (
            <div className="event-row" key={`${event.eventType}-${event.occurredAtUtc}-${index}`}>
              <strong>{eventTimeLabel(event.occurredAtUtc)}</strong>
              <span>{event.eventType}</span>
            </div>
          ))}
        </section>
      </article>

      {incomingCall && <IncomingCallModal call={incomingCall} busy={disableAnswer} onAnswer={onAnswer} onReject={onReject} />}
    </section>
  );
}
