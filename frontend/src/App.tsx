import { useMemo, useState } from 'react';
import { LoginPage } from './pages/LoginPage';
import { SoftphonePage } from './pages/SoftphonePage';
import { useSoftphoneSession } from './hooks/useSoftphoneSession';

export default function App() {
  const {
    state,
    login,
    logout,
    selectExtension,
    makeOutgoingCall,
    answerCall,
    rejectCall,
    endCall,
    requestMicrophone,
    microphonePermission,
    muted,
    setMuted,
    microphoneDevices,
    speakerDevices,
    selectedMicrophoneDeviceId,
    selectedSpeakerDeviceId,
    setMicrophoneDevice,
    setSpeakerDevice,
    refreshMediaDevices,
    setRemoteAudioElement,
    speakerSelectionSupported
  } = useSoftphoneSession();

  const [statusMessage, setStatusMessage] = useState<string | null>(null);

  const bannerMessage = useMemo(() => {
    if (state.errorMessage) {
      return state.errorMessage;
    }

    if (statusMessage) {
      return statusMessage;
    }

    return null;
  }, [state.errorMessage, statusMessage]);

  const showStatus = (message: string) => {
    setStatusMessage(message);
    window.setTimeout(() => setStatusMessage(null), 2500);
  };

  return (
    <div className="app-shell">
      <div className="background-orb background-orb-left" />
      <div className="background-orb background-orb-right" />

      <main className="app-content">
        <header className="app-header">
          <div>
            <h1>Native WebRTC Browser Softphone</h1>
            <p>Calls, answer, and audio media all run directly in this browser endpoint via WebRTC.</p>
          </div>
          {state.auth && (
            <button className="secondary-button" type="button" onClick={logout}>
              Logout
            </button>
          )}
        </header>

        {bannerMessage && <div className="banner-error">{bannerMessage}</div>}

        {!state.auth && <LoginPage loading={state.busy || state.bootstrapLoading} onLogin={login} />}

        {state.auth && state.bootstrapLoading && (
          <section className="card">
            <h2>Loading Session</h2>
            <p>Connecting signaling hub and loading softphone session...</p>
          </section>
        )}

        {state.auth && state.snapshot && !state.snapshot.selectedExtensionDn && (
          <section className="card">
            <h2>Bind Extension</h2>
            <p>Configured extension: {state.snapshot.ownedExtensionDn || 'N/A'}</p>
            <button
              className="primary-button"
              type="button"
              disabled={state.busy || !state.snapshot.ownedExtensionDn}
              onClick={async () => {
                await selectExtension(state.snapshot?.ownedExtensionDn ?? '');
                showStatus(`Extension ${state.snapshot?.ownedExtensionDn} selected`);
              }}
            >
              {state.busy ? 'Binding...' : 'Bind My Extension'}
            </button>
          </section>
        )}

        {state.auth && state.snapshot && state.snapshot.selectedExtensionDn && (
          <SoftphonePage
            snapshot={state.snapshot}
            browserCalls={state.browserCalls}
            events={state.events}
            busy={state.busy}
            pbxBase={state.auth.pbxBase}
            onMakeOutgoingCall={makeOutgoingCall}
            onAnswer={answerCall}
            onReject={rejectCall}
            onEnd={endCall}
            onRequestMicrophone={requestMicrophone}
            microphonePermission={microphonePermission}
            muted={muted}
            onMutedChange={setMuted}
            microphoneDevices={microphoneDevices}
            speakerDevices={speakerDevices}
            selectedMicrophoneDeviceId={selectedMicrophoneDeviceId}
            selectedSpeakerDeviceId={selectedSpeakerDeviceId}
            onSelectMicrophoneDevice={setMicrophoneDevice}
            onSelectSpeakerDevice={setSpeakerDevice}
            onRefreshDevices={refreshMediaDevices}
            onRemoteAudioRef={setRemoteAudioElement}
            speakerSelectionSupported={speakerSelectionSupported}
          />
        )}
      </main>
    </div>
  );
}
