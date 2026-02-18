import { HubConnection } from '@microsoft/signalr';
import { useCallback, useEffect, useReducer, useRef, useState } from 'react';
import type {
  BrowserCallView,
  LoginRequest,
  SessionSnapshotResponse,
  SoftphoneEventEnvelope,
  StoredAuthSession,
  WebRtcSignalMessage
} from '../domain/softphone';
import { clearAuthSession, isAuthSessionExpired, loadAuthSession, saveAuthSession } from '../services/authStorage';
import { ApiRequestError, getApiBase } from '../services/httpClient';
import { getMediaDevices, requireMediaDevicesWithGetUserMedia } from '../services/mediaDevices';
import { createSoftphoneSignalR } from '../services/signalrClient';
import {
  getSessionSnapshot,
  login as loginApi,
  logout as logoutApi,
  selectExtension as selectExtensionApi
} from '../services/softphoneApi';
import { createInitialSessionState, sessionReducer } from '../state/sessionStore';

type MicrophonePermission = 'unknown' | 'granted' | 'denied';

export interface AudioDeviceOption {
  deviceId: string;
  label: string;
}

interface SignalPayload {
  callId: string;
  type: 'offer' | 'answer' | 'ice';
  sdp?: string;
  candidate?: string;
  sdpMid?: string;
  sdpMLineIndex?: number;
}

const STUN_SERVERS = splitEnvList(import.meta.env.VITE_STUN_SERVERS);
const TURN_SERVERS = splitEnvList(import.meta.env.VITE_TURN_SERVERS);
const TURN_USERNAME = (import.meta.env.VITE_TURN_USERNAME ?? '').trim();
const TURN_PASSWORD = (import.meta.env.VITE_TURN_PASSWORD ?? '').trim();
const ICE_SERVERS = buildIceServers();

function isNegotiationFetchError(error: unknown): boolean {
  if (!(error instanceof Error)) {
    return false;
  }

  const message = error.message.toLowerCase();
  return message.includes('failed to complete negotiation') || message.includes('failed to fetch');
}

function toMessage(error: unknown): string {
  if (error instanceof ApiRequestError) {
    return error.message;
  }

  if (isNegotiationFetchError(error)) {
    const apiBase = getApiBase();
    const connectionTarget = apiBase.length > 0 ? apiBase : window.location.origin;
    return `Cannot reach real-time hub at ${connectionTarget}. Check API base URL/protocol and that backend is reachable.`;
  }

  if (error instanceof Error) {
    return error.message;
  }

  return 'Unexpected error';
}

function isUnauthorized(error: unknown): boolean {
  return error instanceof ApiRequestError && (error.statusCode === 401 || error.statusCode === 403);
}

function isHubUnauthorized(error: unknown): boolean {
  if (!(error instanceof Error)) {
    return false;
  }

  const message = error.message.toLowerCase();
  return message.includes('unauthorized') || message.includes('401') || message.includes('403');
}

function createClientEvent(eventType: string, payload: unknown): SoftphoneEventEnvelope {
  return {
    eventType,
    occurredAtUtc: new Date().toISOString(),
    payload
  };
}

function splitEnvList(raw: string | undefined): string[] {
  return (raw ?? '')
    .split(',')
    .map((value) => value.trim())
    .filter((value) => value.length > 0);
}

function buildIceServers(): RTCIceServer[] {
  const servers: RTCIceServer[] = [];

  if (STUN_SERVERS.length > 0) {
    servers.push({ urls: STUN_SERVERS });
  }

  if (TURN_SERVERS.length > 0) {
    const turnServer: RTCIceServer = {
      urls: TURN_SERVERS
    };

    if (TURN_USERNAME.length > 0) {
      turnServer.username = TURN_USERNAME;
    }

    if (TURN_PASSWORD.length > 0) {
      turnServer.credential = TURN_PASSWORD;
    }

    servers.push(turnServer);
  }

  return servers;
}

export function useSoftphoneSession() {
  const [state, dispatch] = useReducer(sessionReducer, loadAuthSession(), createInitialSessionState);
  const hubConnectionRef = useRef<HubConnection | null>(null);
  const peerConnectionsRef = useRef<Map<string, RTCPeerConnection>>(new Map());
  const pendingIceCandidatesRef = useRef<Map<string, RTCIceCandidateInit[]>>(new Map());
  const pendingOffersRef = useRef<Map<string, string>>(new Map());
  const localStreamRef = useRef<MediaStream | null>(null);
  const remoteStreamRef = useRef<MediaStream | null>(null);
  const remoteAudioElementRef = useRef<HTMLAudioElement | null>(null);
  const browserCallsRef = useRef<BrowserCallView[]>([]);
  const [microphonePermission, setMicrophonePermission] = useState<MicrophonePermission>('unknown');
  const [muted, setMuted] = useState(false);
  const [microphoneDevices, setMicrophoneDevices] = useState<AudioDeviceOption[]>([]);
  const [speakerDevices, setSpeakerDevices] = useState<AudioDeviceOption[]>([]);
  const [selectedMicrophoneDeviceId, setSelectedMicrophoneDeviceId] = useState('');
  const [selectedSpeakerDeviceId, setSelectedSpeakerDeviceId] = useState('');
  const [speakerSelectionSupported, setSpeakerSelectionSupported] = useState(false);

  useEffect(() => {
    browserCallsRef.current = state.browserCalls;
  }, [state.browserCalls]);

  const stopHub = useCallback(async () => {
    const connection = hubConnectionRef.current;
    hubConnectionRef.current = null;
    if (!connection) {
      return;
    }

    connection.off('SessionSnapshot');
    connection.off('SoftphoneEvent');
    connection.off('BrowserCallsSnapshot');
    connection.off('BrowserCallUpdated');
    connection.off('WebRtcSignal');
    await connection.stop();
  }, []);

  const pushClientEvent = useCallback((eventType: string, payload: unknown) => {
    dispatch({ type: 'PUSH_EVENT', payload: createClientEvent(eventType, payload) });
  }, []);

  const stopLocalStream = useCallback(() => {
    const stream = localStreamRef.current;
    if (!stream) {
      return;
    }

    stream.getTracks().forEach((track) => track.stop());
    localStreamRef.current = null;
  }, []);

  const closePeerConnection = useCallback((callId: string) => {
    const connection = peerConnectionsRef.current.get(callId);
    if (!connection) {
      return;
    }

    connection.onicecandidate = null;
    connection.ontrack = null;
    connection.onconnectionstatechange = null;
    connection.close();
    peerConnectionsRef.current.delete(callId);
    pendingIceCandidatesRef.current.delete(callId);
    pendingOffersRef.current.delete(callId);

    if (peerConnectionsRef.current.size === 0) {
      remoteStreamRef.current = null;
      const audio = remoteAudioElementRef.current;
      if (audio) {
        audio.srcObject = null;
      }
    }
  }, []);

  const closeAllPeerConnections = useCallback(() => {
    const callIds = [...peerConnectionsRef.current.keys()];
    callIds.forEach((callId) => closePeerConnection(callId));
    pendingIceCandidatesRef.current.clear();
    pendingOffersRef.current.clear();
    remoteStreamRef.current = null;
  }, [closePeerConnection]);

  const applySpeakerDevice = useCallback(async (deviceId: string) => {
    const audio = remoteAudioElementRef.current as (HTMLAudioElement & { setSinkId?: (sinkId: string) => Promise<void> }) | null;
    if (!audio) {
      return;
    }

    if (typeof audio.setSinkId !== 'function') {
      setSpeakerSelectionSupported(false);
      return;
    }

    setSpeakerSelectionSupported(true);
    if (deviceId.length > 0) {
      await audio.setSinkId(deviceId);
    }
  }, []);

  const refreshMediaDevices = useCallback(async () => {
    const mediaDevices = getMediaDevices();
    if (!mediaDevices || typeof mediaDevices.enumerateDevices !== 'function') {
      return;
    }

    const devices = await mediaDevices.enumerateDevices();

    const audioInputs = devices
      .filter((device) => device.kind === 'audioinput')
      .map((device, index) => ({
        deviceId: device.deviceId,
        label: device.label || `Microphone ${index + 1}`
      }));

    const audioOutputs = devices
      .filter((device) => device.kind === 'audiooutput')
      .map((device, index) => ({
        deviceId: device.deviceId,
        label: device.label || `Speaker ${index + 1}`
      }));

    setMicrophoneDevices(audioInputs);
    setSpeakerDevices(audioOutputs);
    setSelectedMicrophoneDeviceId((previous) => previous || audioInputs[0]?.deviceId || '');
    setSelectedSpeakerDeviceId((previous) => previous || audioOutputs[0]?.deviceId || '');
  }, []);

  const replaceAudioTrack = useCallback(async (stream: MediaStream) => {
    const [track] = stream.getAudioTracks();
    if (!track) {
      return;
    }

    const operations: Promise<void>[] = [];
    for (const connection of peerConnectionsRef.current.values()) {
      const sender = connection.getSenders().find((candidate) => candidate.track?.kind === 'audio');
      if (sender) {
        operations.push(sender.replaceTrack(track));
      }
    }

    await Promise.allSettled(operations);
  }, []);

  const ensureLocalStream = useCallback(async () => {
    if (localStreamRef.current) {
      return localStreamRef.current;
    }

    const audioConstraint: MediaTrackConstraints | boolean =
      selectedMicrophoneDeviceId.length > 0
        ? {
            deviceId: {
              exact: selectedMicrophoneDeviceId
            }
          }
        : true;

    try {
      const mediaDevices = requireMediaDevicesWithGetUserMedia();
      const stream = await mediaDevices.getUserMedia({
        audio: audioConstraint,
        video: false
      });

      stream.getAudioTracks().forEach((track) => {
        track.enabled = !muted;
      });

      localStreamRef.current = stream;
      setMicrophonePermission('granted');
      void refreshMediaDevices();
      return stream;
    } catch (error) {
      setMicrophonePermission('denied');
      throw error;
    }
  }, [muted, refreshMediaDevices, selectedMicrophoneDeviceId]);

  const sendSignal = useCallback(async (payload: SignalPayload) => {
    const hub = hubConnectionRef.current;
    if (!hub) {
      throw new Error('Real-time signaling channel is not connected.');
    }

    await hub.invoke('SendWebRtcSignal', payload);
  }, []);

  const applyPendingIce = useCallback(async (callId: string, connection: RTCPeerConnection) => {
    const queuedCandidates = pendingIceCandidatesRef.current.get(callId);
    if (!queuedCandidates || queuedCandidates.length === 0) {
      return;
    }

    for (const candidate of queuedCandidates) {
      await connection.addIceCandidate(candidate);
    }

    pendingIceCandidatesRef.current.delete(callId);
  }, []);

  const ensurePeerConnection = useCallback(
    async (callId: string) => {
      const existingConnection = peerConnectionsRef.current.get(callId);
      if (existingConnection) {
        return existingConnection;
      }

      const stream = await ensureLocalStream();
      const connection = new RTCPeerConnection({ iceServers: ICE_SERVERS });

      stream.getAudioTracks().forEach((track) => {
        connection.addTrack(track, stream);
      });

      connection.onicecandidate = (event) => {
        const candidate = event.candidate;
        if (!candidate) {
          return;
        }

        const payload: SignalPayload = {
          callId,
          type: 'ice',
          candidate: candidate.candidate,
          sdpMid: candidate.sdpMid ?? undefined,
          sdpMLineIndex: candidate.sdpMLineIndex ?? undefined
        };

        void sendSignal(payload).catch(() => undefined);
      };

      connection.ontrack = (event) => {
        const streamFromEvent = event.streams[0];
        if (streamFromEvent) {
          remoteStreamRef.current = streamFromEvent;
        } else {
          const aggregateStream = remoteStreamRef.current ?? new MediaStream();
          aggregateStream.addTrack(event.track);
          remoteStreamRef.current = aggregateStream;
        }

        const audio = remoteAudioElementRef.current;
        if (audio && remoteStreamRef.current) {
          audio.srcObject = remoteStreamRef.current;
          void audio.play().catch(() => undefined);
        }
      };

      connection.onconnectionstatechange = () => {
        const stateLabel = connection.connectionState;
        if (stateLabel === 'connected') {
          const hub = hubConnectionRef.current;
          if (hub) {
            void hub.invoke('MarkCallConnected', callId).catch(() => undefined);
          }
          pushClientEvent('webrtc.connected', { callId });
        }

        if (stateLabel === 'failed') {
          const hub = hubConnectionRef.current;
          if (hub) {
            void hub.invoke('EndBrowserCall', callId).catch(() => undefined);
          }
          pushClientEvent('webrtc.failed', { callId });
        }

        if (stateLabel === 'closed') {
          closePeerConnection(callId);
        }
      };

      peerConnectionsRef.current.set(callId, connection);
      return connection;
    },
    [closePeerConnection, ensureLocalStream, pushClientEvent, sendSignal]
  );

  const answerWithPendingOffer = useCallback(
    async (callId: string) => {
      const offerSdp = pendingOffersRef.current.get(callId);
      if (!offerSdp) {
        return;
      }

      const connection = await ensurePeerConnection(callId);
      if (!connection.currentRemoteDescription) {
        await connection.setRemoteDescription({ type: 'offer', sdp: offerSdp });
      }

      await applyPendingIce(callId, connection);
      const answer = await connection.createAnswer();
      await connection.setLocalDescription(answer);

      if (!answer.sdp) {
        throw new Error('Failed to generate SDP answer.');
      }

      await sendSignal({
        callId,
        type: 'answer',
        sdp: answer.sdp
      });

      pendingOffersRef.current.delete(callId);
    },
    [applyPendingIce, ensurePeerConnection, sendSignal]
  );

  const handleOfferSignal = useCallback(
    async (signal: WebRtcSignalMessage) => {
      if (!signal.sdp) {
        return;
      }

      pendingOffersRef.current.set(signal.callId, signal.sdp);
      const call = browserCallsRef.current.find((candidate) => candidate.callId === signal.callId);
      if (!call) {
        return;
      }

      const canAutoAnswer = !call.isIncoming || call.status !== 'Ringing';
      if (!canAutoAnswer) {
        return;
      }

      await answerWithPendingOffer(signal.callId);
    },
    [answerWithPendingOffer]
  );

  const handleAnswerSignal = useCallback(
    async (signal: WebRtcSignalMessage) => {
      if (!signal.sdp) {
        return;
      }

      const connection = await ensurePeerConnection(signal.callId);
      if (!connection.currentRemoteDescription) {
        await connection.setRemoteDescription({
          type: 'answer',
          sdp: signal.sdp
        });
      }

      await applyPendingIce(signal.callId, connection);
    },
    [applyPendingIce, ensurePeerConnection]
  );

  const handleIceSignal = useCallback(
    async (signal: WebRtcSignalMessage) => {
      if (!signal.candidate) {
        return;
      }

      const candidate: RTCIceCandidateInit = {
        candidate: signal.candidate,
        sdpMid: signal.sdpMid ?? undefined,
        sdpMLineIndex: signal.sdpMLineIndex ?? undefined
      };

      const connection = peerConnectionsRef.current.get(signal.callId);
      if (!connection || !connection.remoteDescription) {
        const queue = pendingIceCandidatesRef.current.get(signal.callId) ?? [];
        queue.push(candidate);
        pendingIceCandidatesRef.current.set(signal.callId, queue);
        return;
      }

      await connection.addIceCandidate(candidate);
    },
    []
  );

  const routeSignal = useCallback(
    async (signal: WebRtcSignalMessage) => {
      switch (signal.type) {
        case 'offer':
          await handleOfferSignal(signal);
          break;
        case 'answer':
          await handleAnswerSignal(signal);
          break;
        case 'ice':
          await handleIceSignal(signal);
          break;
        default:
          break;
      }
    },
    [handleAnswerSignal, handleIceSignal, handleOfferSignal]
  );

  const resetToLoggedOut = useCallback(async () => {
    await stopHub();
    closeAllPeerConnections();
    stopLocalStream();
    clearAuthSession();
    dispatch({ type: 'SET_AUTH', payload: null });
    dispatch({ type: 'SET_SNAPSHOT', payload: null });
    dispatch({ type: 'CLEAR_BROWSER_CALLS' });
    dispatch({ type: 'CLEAR_EVENTS' });
  }, [closeAllPeerConnections, stopHub, stopLocalStream]);

  const refreshSnapshot = useCallback(async (accessToken: string) => {
    const snapshot = await getSessionSnapshot(accessToken);
    dispatch({ type: 'SET_SNAPSHOT', payload: snapshot });
  }, []);

  const connectHub = useCallback(
    async (accessToken: string) => {
      await stopHub();

      const bindHandlers = (hub: HubConnection) => {
        hub.on('SessionSnapshot', (snapshot: SessionSnapshotResponse) => {
          dispatch({ type: 'SET_SNAPSHOT', payload: snapshot });
        });

        hub.on('SoftphoneEvent', (envelope: SoftphoneEventEnvelope) => {
          dispatch({ type: 'PUSH_EVENT', payload: envelope });
        });

        hub.on('BrowserCallsSnapshot', (calls: BrowserCallView[]) => {
          dispatch({ type: 'SET_BROWSER_CALLS', payload: calls });
        });

        hub.on('BrowserCallUpdated', (call: BrowserCallView) => {
          dispatch({ type: 'UPSERT_BROWSER_CALL', payload: call });
          dispatch({
            type: 'PUSH_EVENT',
            payload: createClientEvent(`browser.call.${call.status.toLowerCase()}`, {
              callId: call.callId,
              remoteExtension: call.remoteExtension,
              endReason: call.endReason ?? null
            })
          });

          if (call.status === 'Ended') {
            closePeerConnection(call.callId);
            return;
          }

          if (call.isIncoming && call.status !== 'Ringing' && pendingOffersRef.current.has(call.callId)) {
            void answerWithPendingOffer(call.callId).catch(() => undefined);
          }
        });

        hub.on('WebRtcSignal', (signal: WebRtcSignalMessage) => {
          dispatch({
            type: 'PUSH_EVENT',
            payload: createClientEvent(`webrtc.signal.${signal.type}`, {
              callId: signal.callId,
              fromExtension: signal.fromExtension
            })
          });
          void routeSignal(signal).catch(() => undefined);
        });

        hub.onclose((error?: Error) => {
          if (error) {
            dispatch({ type: 'SET_ERROR', payload: 'Real-time connection was interrupted. Reconnecting...' });
          }
        });

        hub.onreconnected(() => {
          dispatch({ type: 'SET_ERROR', payload: null });
        });
      };

      const startHub = async (forceWebSockets: boolean) => {
        const hub = createSoftphoneSignalR(accessToken, forceWebSockets);
        bindHandlers(hub);
        hubConnectionRef.current = hub;
        await hub.start();
      };

      try {
        await startHub(false);
      } catch (error) {
        await stopHub();
        if (!isNegotiationFetchError(error)) {
          throw error;
        }

        await startHub(true);
      }
    },
    [answerWithPendingOffer, closePeerConnection, routeSignal, stopHub]
  );

  useEffect(() => {
    let disposed = false;

    const bootstrap = async () => {
      const auth = state.auth;
      if (!auth) {
        dispatch({ type: 'SET_BOOTSTRAP_LOADING', payload: false });
        return;
      }

      if (isAuthSessionExpired(auth)) {
        await resetToLoggedOut();
        if (!disposed) {
          dispatch({ type: 'SET_ERROR', payload: 'Session has expired. Login again.' });
          dispatch({ type: 'SET_BOOTSTRAP_LOADING', payload: false });
        }
        return;
      }

      dispatch({ type: 'SET_BOOTSTRAP_LOADING', payload: true });
      dispatch({ type: 'SET_ERROR', payload: null });

      try {
        await refreshSnapshot(auth.accessToken);
        await connectHub(auth.accessToken);
      } catch (error) {
        if (disposed) {
          return;
        }

        if (isUnauthorized(error)) {
          await resetToLoggedOut();
          dispatch({ type: 'SET_ERROR', payload: 'Authentication failed. Login again.' });
        } else {
          dispatch({ type: 'SET_ERROR', payload: toMessage(error) });
        }
      } finally {
        if (!disposed) {
          dispatch({ type: 'SET_BOOTSTRAP_LOADING', payload: false });
        }
      }
    };

    void bootstrap();

    return () => {
      disposed = true;
    };
  }, [state.auth, connectHub, refreshSnapshot, resetToLoggedOut]);

  useEffect(() => {
    return () => {
      closeAllPeerConnections();
      stopLocalStream();
      void stopHub();
    };
  }, [closeAllPeerConnections, stopHub, stopLocalStream]);

  const runMutation = useCallback(
    async (operation: (session: StoredAuthSession) => Promise<void>) => {
      const auth = state.auth;
      if (!auth) {
        throw new Error('Not authenticated');
      }

      dispatch({ type: 'SET_BUSY', payload: true });
      dispatch({ type: 'SET_ERROR', payload: null });

      try {
        await operation(auth);
        await refreshSnapshot(auth.accessToken);
      } catch (error) {
        if (isUnauthorized(error)) {
          await resetToLoggedOut();
          dispatch({ type: 'SET_ERROR', payload: 'Authentication failed. Login again.' });
          return;
        }

        dispatch({ type: 'SET_ERROR', payload: toMessage(error) });
      } finally {
        dispatch({ type: 'SET_BUSY', payload: false });
      }
    },
    [state.auth, refreshSnapshot, resetToLoggedOut]
  );

  const login = useCallback(async (request: LoginRequest) => {
    dispatch({ type: 'SET_BUSY', payload: true });
    dispatch({ type: 'SET_ERROR', payload: null });

    try {
      const response = await loginApi(request);
      const authSession: StoredAuthSession = {
        accessToken: response.accessToken,
        expiresAtUtc: response.expiresAtUtc,
        sessionId: response.sessionId,
        username: response.username,
        pbxBase: request.pbxBase
      };

      saveAuthSession(authSession);
      dispatch({ type: 'SET_AUTH', payload: authSession });
      dispatch({ type: 'SET_BOOTSTRAP_LOADING', payload: true });
      dispatch({ type: 'CLEAR_EVENTS' });
    } catch (error) {
      dispatch({ type: 'SET_ERROR', payload: toMessage(error) });
    } finally {
      dispatch({ type: 'SET_BUSY', payload: false });
    }
  }, []);

  const logout = useCallback(async () => {
    const auth = state.auth;
    dispatch({ type: 'SET_BUSY', payload: true });
    dispatch({ type: 'SET_ERROR', payload: null });

    try {
      if (auth) {
        try {
          await logoutApi(auth.accessToken);
        } catch {
          // If backend session is already gone, local cleanup still proceeds.
        }
      }

      await resetToLoggedOut();
    } finally {
      dispatch({ type: 'SET_BUSY', payload: false });
      dispatch({ type: 'SET_BOOTSTRAP_LOADING', payload: false });
    }
  }, [state.auth, resetToLoggedOut]);

  const selectExtension = useCallback(
    async (extensionDn: string) => {
      await runMutation(async (session) => {
        await selectExtensionApi(session.accessToken, extensionDn);
      });
    },
    [runMutation]
  );

  const runHubMutation = useCallback(
    async (operation: (hub: HubConnection) => Promise<void>) => {
      const auth = state.auth;
      if (!auth) {
        throw new Error('Not authenticated');
      }

      const hub = hubConnectionRef.current;
      if (!hub) {
        throw new Error('Real-time hub is not connected.');
      }

      dispatch({ type: 'SET_BUSY', payload: true });
      dispatch({ type: 'SET_ERROR', payload: null });

      try {
        await operation(hub);
      } catch (error) {
        if (isHubUnauthorized(error)) {
          await resetToLoggedOut();
          dispatch({ type: 'SET_ERROR', payload: 'Authentication failed. Login again.' });
          return;
        }

        dispatch({ type: 'SET_ERROR', payload: toMessage(error) });
      } finally {
        dispatch({ type: 'SET_BUSY', payload: false });
      }
    },
    [resetToLoggedOut, state.auth]
  );

  const makeOutgoingCall = useCallback(
    async (destinationExtension: string) => {
      await runHubMutation(async (hub) => {
        const destination = destinationExtension.trim();
        if (destination.length === 0) {
          throw new Error('Destination extension is required.');
        }

        await ensureLocalStream();
        const call = await hub.invoke<BrowserCallView>('PlaceBrowserCall', destination);
        dispatch({ type: 'UPSERT_BROWSER_CALL', payload: call });

        const connection = await ensurePeerConnection(call.callId);
        const offer = await connection.createOffer();
        await connection.setLocalDescription(offer);

        if (!offer.sdp) {
          throw new Error('Failed to create SDP offer.');
        }

        await sendSignal({
          callId: call.callId,
          type: 'offer',
          sdp: offer.sdp
        });
      });
    },
    [ensureLocalStream, ensurePeerConnection, runHubMutation, sendSignal]
  );

  const answerCall = useCallback(
    async (callId: string) => {
      await runHubMutation(async (hub) => {
        await ensureLocalStream();
        await hub.invoke('AnswerBrowserCall', callId);
        await ensurePeerConnection(callId);
        await answerWithPendingOffer(callId);
      });
    },
    [answerWithPendingOffer, ensureLocalStream, ensurePeerConnection, runHubMutation]
  );

  const rejectCall = useCallback(
    async (callId: string) => {
      await runHubMutation(async (hub) => {
        await hub.invoke('RejectBrowserCall', callId);
        closePeerConnection(callId);
      });
    },
    [closePeerConnection, runHubMutation]
  );

  const endCall = useCallback(
    async (callId: string) => {
      await runHubMutation(async (hub) => {
        await hub.invoke('EndBrowserCall', callId);
        closePeerConnection(callId);
      });
    },
    [closePeerConnection, runHubMutation]
  );

  const requestMicrophone = useCallback(async () => {
    dispatch({ type: 'SET_ERROR', payload: null });
    try {
      await ensureLocalStream();
      pushClientEvent('microphone.granted', {});
    } catch (error) {
      dispatch({ type: 'SET_ERROR', payload: toMessage(error) });
    }
  }, [ensureLocalStream, pushClientEvent]);

  const setMicrophoneDevice = useCallback(
    async (deviceId: string) => {
      setSelectedMicrophoneDeviceId(deviceId);
      if (deviceId.trim().length === 0) {
        return;
      }

      if (!localStreamRef.current) {
        return;
      }

      try {
        const mediaDevices = requireMediaDevicesWithGetUserMedia();
        const stream = await mediaDevices.getUserMedia({
          audio: {
            deviceId: {
              exact: deviceId
            }
          },
          video: false
        });

        stream.getAudioTracks().forEach((track) => {
          track.enabled = !muted;
        });

        const previous = localStreamRef.current;
        localStreamRef.current = stream;
        await replaceAudioTrack(stream);
        previous.getTracks().forEach((track) => track.stop());
        setMicrophonePermission('granted');
      } catch (error) {
        dispatch({ type: 'SET_ERROR', payload: toMessage(error) });
      }
    },
    [muted, replaceAudioTrack]
  );

  const setSpeakerDevice = useCallback(
    async (deviceId: string) => {
      setSelectedSpeakerDeviceId(deviceId);
      try {
        await applySpeakerDevice(deviceId);
      } catch (error) {
        dispatch({ type: 'SET_ERROR', payload: toMessage(error) });
      }
    },
    [applySpeakerDevice]
  );

  const setRemoteAudioElement = useCallback(
    (element: HTMLAudioElement | null) => {
      remoteAudioElementRef.current = element;
      if (!element) {
        return;
      }

      element.autoplay = true;
      if (remoteStreamRef.current) {
        element.srcObject = remoteStreamRef.current;
      }

      if (selectedSpeakerDeviceId.length > 0) {
        void applySpeakerDevice(selectedSpeakerDeviceId).catch(() => undefined);
      } else {
        const sinkAwareElement = element as HTMLAudioElement & { setSinkId?: (sinkId: string) => Promise<void> };
        setSpeakerSelectionSupported(typeof sinkAwareElement.setSinkId === 'function');
      }
    },
    [applySpeakerDevice, selectedSpeakerDeviceId]
  );

  useEffect(() => {
    const stream = localStreamRef.current;
    if (!stream) {
      return;
    }

    stream.getAudioTracks().forEach((track) => {
      track.enabled = !muted;
    });
  }, [muted]);

  useEffect(() => {
    let active = true;
    const init = async () => {
      try {
        await refreshMediaDevices();
      } catch {
        if (!active) {
          return;
        }
      }
    };

    void init();

    const mediaDevices = getMediaDevices();
    if (!mediaDevices || !mediaDevices.addEventListener) {
      return () => {
        active = false;
      };
    }

    const handler = () => {
      void refreshMediaDevices();
    };

    mediaDevices.addEventListener('devicechange', handler);
    return () => {
      active = false;
      mediaDevices.removeEventListener('devicechange', handler);
    };
  }, [refreshMediaDevices]);

  return {
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
  };
}
