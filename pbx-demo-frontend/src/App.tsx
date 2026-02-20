import { useEffect, useMemo, useState } from 'react';
import { LoginPage } from './pages/LoginPage';
import { SoftphonePage } from './pages/SoftphonePage';
import { SupervisorPage } from './pages/SupervisorPage';
import { useSoftphoneSession } from './hooks/useSoftphoneSession';

type WorkspaceView = 'login' | 'operator' | 'supervisor';

function navButtonClass(active: boolean): string {
  if (active) {
    return 'inline-flex h-11 items-center rounded-xl border border-transparent bg-gradient-to-r from-primary-600 to-secondary-500 px-3 text-sm font-semibold text-white shadow-soft transition-all duration-150';
  }

  return 'inline-flex h-11 items-center rounded-xl border border-border bg-white px-3 text-sm font-semibold text-muted-strong shadow-sm transition-all duration-150 hover:-translate-y-0.5 hover:border-primary-300 hover:bg-surface-2';
}

export default function App() {
  const {
    state,
    login,
    logout,
    selectExtension,
    setActivePbxDevice,
    makeOutgoingCall,
    makePbxOutgoingCall,
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
    setSipRemoteAudioElement,
    speakerSelectionSupported,
    sipRegistrationState,
    sipRegistrationError,
    sipCallInfo,
    answerSipCall,
    rejectSipCall,
    hangupSipCall
  } = useSoftphoneSession();

  const [statusMessage, setStatusMessage] = useState<string | null>(null);
  const [activeView, setActiveView] = useState<WorkspaceView>('login');

  const showStatus = (message: string): void => {
    setStatusMessage(message);
    window.setTimeout(() => setStatusMessage(null), 2500);
  };

  const isSupervisor = state.auth?.role === 'Supervisor';
  const displayName = state.auth?.displayName || state.auth?.username || 'Guest';
  const hasSoftphoneAccess = Boolean(state.auth?.hasSoftphoneAccess);
  const canUseSupervisor = Boolean(state.auth && isSupervisor);
  const availableModuleCount = Number(hasSoftphoneAccess) + Number(canUseSupervisor);

  useEffect(() => {
    if (!state.auth) {
      setActiveView('login');
      return;
    }

    if (hasSoftphoneAccess) {
      setActiveView((previous) => (previous === 'supervisor' && canUseSupervisor ? previous : 'operator'));
      return;
    }

    if (canUseSupervisor) {
      setActiveView('supervisor');
      return;
    }

    setActiveView('login');
  }, [canUseSupervisor, hasSoftphoneAccess, state.auth]);

  const sessionState = useMemo(() => {
    if (!state.auth) {
      return null;
    }

    const expiresMs = Date.parse(state.auth.expiresAtUtc);
    if (Number.isNaN(expiresMs)) {
      return {
        tone: 'status-info',
        text: 'Session expiry unknown'
      };
    }

    const remainingMs = expiresMs - Date.now();
    if (remainingMs <= 0) {
      return {
        tone: 'status-critical',
        text: 'Session expired'
      };
    }

    const remainingMinutes = Math.ceil(remainingMs / 60000);
    if (remainingMinutes <= 15) {
      return {
        tone: 'status-waiting',
        text: `Session ends in ${remainingMinutes} min`
      };
    }

    return {
      tone: 'status-active',
      text: `Session active (${remainingMinutes} min left)`
    };
  }, [state.auth]);

  const workspaceHeading = useMemo(() => {
    if (activeView === 'supervisor') {
      return {
        kicker: 'Supervisor Suite',
        title: 'Supervisor CRM Dashboard',
        subtitle: 'Real-time monitoring, user management, and operational control.'
      };
    }

    if (activeView === 'operator') {
      return {
        kicker: 'Operator Console',
        title: 'Operator Workbench',
        subtitle: 'Call handling, CRM context, and request registration in one view.'
      };
    }

    return {
      kicker: 'Access Portal',
      title: 'Role-Based Login',
      subtitle: 'Login with your CRM account to continue.'
    };
  }, [activeView]);

  return (
    <div className="min-h-screen px-3 pb-8 pt-4 sm:px-5 lg:px-7">
      <div className="mx-auto flex w-full max-w-[1600px] flex-col gap-4">
        <header className="rounded-3xl border border-border/80 bg-surface/85 p-4 shadow-card backdrop-blur md:p-5">
          <div className="flex flex-col gap-4 lg:flex-row lg:items-center lg:justify-between">
            <div className="grid gap-1">
              <span className="inline-flex w-fit rounded-full border border-primary-300 bg-primary-100 px-3 py-1 text-xs font-semibold uppercase tracking-wider text-primary-700">
                Unified Workspace
              </span>
              <h1 className="font-display text-xl font-semibold text-ink sm:text-2xl">3CX CRM Command Center</h1>
              <p className="text-sm text-muted">Live calling, CRM context, and operations control in one modern workspace.</p>
            </div>

            <div className="flex flex-wrap items-center gap-2">
              {state.auth && (
                <>
                  {sessionState && <span className={`status-chip ${sessionState.tone}`}>{sessionState.text}</span>}
                  <span className="status-chip status-info">User: {displayName}</span>
                  <span className="status-chip status-info">Role: {state.auth.role}</span>
                </>
              )}
              {state.auth && (
                <button className="secondary-button" type="button" onClick={logout}>
                  Logout
                </button>
              )}
            </div>
          </div>
        </header>

        <div className="grid gap-4 xl:grid-cols-[260px_minmax(0,1fr)]">
          <aside className="rounded-3xl border border-border/80 bg-surface/90 p-4 shadow-card backdrop-blur xl:sticky xl:top-6 xl:self-start">
            <div className="grid gap-1">
              <p className="text-xs font-semibold uppercase tracking-wider text-muted">Workspace Modules</p>
              <h2 className="font-display text-base font-semibold text-ink">
                {state.auth ? 'Choose your control panel' : 'Sign in to continue'}
              </h2>
            </div>

            <div className="mt-4 grid gap-2">
              {!state.auth && (
                <button type="button" className={navButtonClass(true)} disabled>
                  Login
                </button>
              )}

              {state.auth && hasSoftphoneAccess && (
                <button
                  type="button"
                  className={navButtonClass(activeView === 'operator')}
                  onClick={() => setActiveView('operator')}
                >
                  Operator Panel
                </button>
              )}

              {state.auth && canUseSupervisor && (
                <button
                  type="button"
                  className={navButtonClass(activeView === 'supervisor')}
                  onClick={() => setActiveView('supervisor')}
                >
                  Supervisor Panel
                </button>
              )}
            </div>

            {state.auth && !hasSoftphoneAccess && !canUseSupervisor && (
              <div className="mt-4 rounded-2xl border border-border bg-white/80 p-3 text-sm">
                <strong className="block text-ink">No active module</strong>
                <p className="mt-1 text-muted">This account needs a role or extension mapping.</p>
              </div>
            )}

            {state.auth && (
              <div className="mt-4 grid gap-1 border-t border-border pt-3 text-sm text-muted-strong">
                <strong className="text-ink">{displayName}</strong>
                <span>Extension: {state.auth.ownedExtensionDn || 'N/A'}</span>
                <span>{availableModuleCount} module(s) available</span>
              </div>
            )}
          </aside>

          <main className="grid gap-4">
            <section className="card overflow-hidden border-transparent bg-gradient-to-br from-primary-700 via-primary-600 to-secondary-500 text-white">
              <p className="mb-2 inline-flex w-fit rounded-full border border-white/35 bg-white/10 px-3 py-1 text-xs font-semibold uppercase tracking-wider text-white/95">
                {workspaceHeading.kicker}
              </p>
              <h2 className="font-display text-2xl font-semibold text-white sm:text-[1.7rem]">{workspaceHeading.title}</h2>
              <p className="mt-2 text-sm text-white/90">{workspaceHeading.subtitle}</p>
            </section>

            {(state.errorMessage || statusMessage) && (
              <section className="grid gap-2">
                {state.errorMessage && <div className="banner-error">{state.errorMessage}</div>}
                {statusMessage && <div className="banner-success">{statusMessage}</div>}
              </section>
            )}

            {!state.auth && <LoginPage loading={state.busy || state.bootstrapLoading} onLogin={login} />}

            {state.auth && state.bootstrapLoading && hasSoftphoneAccess && (
              <section className="card">
                <h3 className="text-lg font-semibold">Loading session</h3>
                <p className="mt-2 text-sm text-muted">Connecting to real-time channels and call controls...</p>
              </section>
            )}

            {state.auth && canUseSupervisor && activeView === 'supervisor' && (
              <SupervisorPage accessToken={state.auth.accessToken} />
            )}

            {state.auth && !hasSoftphoneAccess && activeView === 'operator' && (
              <section className="card">
                <h3 className="text-lg font-semibold">Softphone access disabled</h3>
                <p className="mt-2 text-sm text-muted">
                  This account can use CRM modules but does not have an extension bound for call controls.
                </p>
              </section>
            )}

            {state.auth && hasSoftphoneAccess && state.snapshot && !state.snapshot.selectedExtensionDn && (
              <section className="card grid gap-3">
                <div>
                  <h3 className="text-lg font-semibold">Bind extension</h3>
                  <p className="mt-2 text-sm text-muted">Configured extension: {state.snapshot.ownedExtensionDn || 'N/A'}</p>
                </div>
                <button
                  className="primary-button w-fit"
                  type="button"
                  disabled={state.busy || !state.snapshot.ownedExtensionDn}
                  onClick={async () => {
                    await selectExtension(state.snapshot?.ownedExtensionDn ?? '');
                    showStatus(`Extension ${state.snapshot?.ownedExtensionDn} selected`);
                  }}
                >
                  {state.busy ? 'Binding...' : 'Bind my extension'}
                </button>
              </section>
            )}

            {state.auth && hasSoftphoneAccess && state.snapshot && state.snapshot.selectedExtensionDn && activeView === 'operator' && (
              <SoftphonePage
                snapshot={state.snapshot}
                browserCalls={state.browserCalls}
                events={state.events}
                busy={state.busy}
                pbxBase={state.auth.pbxBase}
                onMakeOutgoingCall={makeOutgoingCall}
                onMakePbxOutgoingCall={makePbxOutgoingCall}
                onAnswer={answerCall}
                onReject={rejectCall}
                onEnd={endCall}
                onSetActivePbxDevice={setActivePbxDevice}
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
                sipRegistrationState={sipRegistrationState}
                sipRegistrationError={sipRegistrationError}
                sipCallInfo={sipCallInfo}
                onAnswerSipCall={answerSipCall}
                onRejectSipCall={rejectSipCall}
                onHangupSipCall={hangupSipCall}
                onSipRemoteAudioRef={setSipRemoteAudioElement}
              />
            )}

            {state.auth && !canUseSupervisor && !hasSoftphoneAccess && (
              <section className="card">
                <h3 className="text-lg font-semibold">Access is limited</h3>
                <p className="mt-2 text-sm text-muted">Contact an administrator to grant operator or supervisor permissions.</p>
              </section>
            )}
          </main>
        </div>
      </div>
    </div>
  );
}
