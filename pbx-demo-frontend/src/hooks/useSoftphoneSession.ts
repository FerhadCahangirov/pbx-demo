import { HubConnection } from "@microsoft/signalr";
import { useCallback, useEffect, useReducer, useRef, useState } from "react";
import { Web } from "sip.js";
import type {
  BrowserCallView,
  LoginRequest,
  SoftphoneCallView,
  SessionSnapshotResponse,
  SipCallInfo,
  SipRegistrationState,
  SoftphoneEventEnvelope,
  StoredAuthSession,
  WebRtcSignalMessage,
} from "../domain/softphone";
import { isIncomingCall } from "../domain/softphone";
import {
  clearAuthSession,
  isAuthSessionExpired,
  loadAuthSession,
  saveAuthSession,
} from "../services/authStorage";
import { ApiRequestError, getApiBase } from "../services/httpClient";
import {
  getMediaDevices,
  requireMediaDevicesWithGetUserMedia,
} from "../services/mediaDevices";
import { createSoftphoneSignalR } from "../services/signalrClient";
import {
  answerCall as answerCallApi,
  endCall as endCallApi,
  getSipRegistrationConfig,
  getSessionSnapshot,
  login as loginApi,
  logout as logoutApi,
  makeOutgoingCall as makePbxOutgoingCallApi,
  openCallAudioDownlink,
  rejectCall as rejectCallApi,
  setActiveDevice as setActiveDeviceApi,
  selectExtension as selectExtensionApi,
  uploadCallAudioUplink,
} from "../services/softphoneApi";
import {
  createInitialSessionState,
  sessionReducer,
} from "../state/sessionStore";

type MicrophonePermission = "unknown" | "granted" | "denied";

export interface AudioDeviceOption {
  deviceId: string;
  label: string;
}

interface SignalPayload {
  callId: string;
  type: "offer" | "answer" | "ice";
  sdp?: string;
  candidate?: string;
  sdpMid?: string;
  sdpMLineIndex?: number;
}

const STUN_SERVERS = splitEnvList(import.meta.env.VITE_STUN_SERVERS);
const TURN_SERVERS = splitEnvList(import.meta.env.VITE_TURN_SERVERS);
const TURN_USERNAME = (import.meta.env.VITE_TURN_USERNAME ?? "").trim();
const TURN_PASSWORD = (import.meta.env.VITE_TURN_PASSWORD ?? "").trim();
const ICE_SERVERS = buildIceServers();
const DIRECT_SIP_JS_ENABLED = false;
const SIP_DISABLED_MESSAGE =
  "3CX voice bridge uses Call Control audio streaming (SIP.js direct registration is disabled).";
const CALL_CONTROL_SAMPLE_RATE = 8000;

function isNegotiationFetchError(error: unknown): boolean {
  if (!(error instanceof Error)) {
    return false;
  }

  const message = error.message.toLowerCase();
  return (
    message.includes("failed to complete negotiation") ||
    message.includes("failed to fetch")
  );
}

function toMessage(error: unknown): string {
  if (error instanceof ApiRequestError) {
    return error.message;
  }

  if (isNegotiationFetchError(error)) {
    const apiBase = getApiBase();
    const connectionTarget =
      apiBase.length > 0 ? apiBase : window.location.origin;
    return `Cannot reach real-time hub at ${connectionTarget}. Check API base URL/protocol and that backend is reachable.`;
  }

  if (error instanceof Error) {
    return error.message;
  }

  return "Unexpected error";
}

function isUnauthorized(error: unknown): boolean {
  return error instanceof ApiRequestError && error.statusCode === 401;
}

function isHubUnauthorized(error: unknown): boolean {
  if (!(error instanceof Error)) {
    return false;
  }

  const message = error.message.toLowerCase();
  return message.includes("unauthorized") || message.includes("401");
}

function createClientEvent(
  eventType: string,
  payload: unknown,
): SoftphoneEventEnvelope {
  return {
    eventType,
    occurredAtUtc: new Date().toISOString(),
    payload,
  };
}

function splitEnvList(raw: string | undefined): string[] {
  return (raw ?? "")
    .split(",")
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
      urls: TURN_SERVERS,
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

function resolveRemoteIdentity(simpleUser: Web.SimpleUser | null): string {
  if (!simpleUser) {
    return "Unknown";
  }

  const unknownShape = simpleUser as unknown as {
    session?: {
      remoteIdentity?: {
        displayName?: string;
        uri?: {
          user?: string;
          normal?: { user?: string };
        };
      };
    };
  };

  const displayName =
    unknownShape.session?.remoteIdentity?.displayName?.trim() ?? "";
  if (displayName.length > 0) {
    return displayName;
  }

  const user =
    unknownShape.session?.remoteIdentity?.uri?.user?.trim() ??
    unknownShape.session?.remoteIdentity?.uri?.normal?.user?.trim() ??
    "";

  return user.length > 0 ? user : "Unknown";
}

function normalizeCallStatus(status: string | null | undefined): string {
  return (status ?? "").trim().toLowerCase();
}

function isRingingPbxCall(call: SoftphoneCallView): boolean {
  return normalizeCallStatus(call.status) === "ringing";
}

function isConnectedPbxCall(call: SoftphoneCallView): boolean {
  return normalizeCallStatus(call.status) === "connected";
}

function isAnswerablePbxCall(call: SoftphoneCallView): boolean {
  if (call.answerable === true || call.directControl === true) {
    return true;
  }

  const dnType = (call.partyDnType ?? "").trim().toLowerCase();
  return dnType === "wroutepoint";
}

function resolvePbxRemoteIdentity(call: SoftphoneCallView): string {
  const remoteName = (call.remoteName ?? "").trim();
  const remoteParty = (call.remoteParty ?? "").trim();

  if (remoteName.length > 0 && remoteParty.length > 0) {
    return `${remoteName} (${remoteParty})`;
  }

  if (remoteName.length > 0) {
    return remoteName;
  }

  if (remoteParty.length > 0) {
    return remoteParty;
  }

  return "Unknown";
}

function findConnectedPbxCall(
  calls: SoftphoneCallView[],
): SoftphoneCallView | null {
  return (
    calls.find(
      (call) => isConnectedPbxCall(call) && isAnswerablePbxCall(call),
    ) ??
    calls.find((call) => isConnectedPbxCall(call)) ??
    null
  );
}

function findRingingPbxCall(
  calls: SoftphoneCallView[],
): SoftphoneCallView | null {
  return (
    calls.find((call) => isRingingPbxCall(call) && isAnswerablePbxCall(call)) ??
    calls.find((call) => isRingingPbxCall(call) && isIncomingCall(call)) ??
    calls.find((call) => isRingingPbxCall(call)) ??
    null
  );
}

function findActionablePbxCall(
  calls: SoftphoneCallView[],
): SoftphoneCallView | null {
  return (
    findConnectedPbxCall(calls) ??
    findRingingPbxCall(calls) ??
    calls.find((call) => isAnswerablePbxCall(call)) ??
    calls[0] ??
    null
  );
}

function pcm16ToFloat32(data: Uint8Array): Float32Array {
  const sampleCount = Math.floor(data.byteLength / 2);
  const view = new DataView(data.buffer, data.byteOffset, sampleCount * 2);
  const output = new Float32Array(sampleCount);

  for (let index = 0; index < sampleCount; index += 1) {
    const sample = view.getInt16(index * 2, true);
    output[index] = sample / 32768;
  }

  return output;
}

function float32ToPcm16(data: Float32Array): Uint8Array {
  const buffer = new ArrayBuffer(data.length * 2);
  const view = new DataView(buffer);

  for (let index = 0; index < data.length; index += 1) {
    const sample = Math.max(-1, Math.min(1, data[index]));
    const value = sample < 0 ? sample * 0x8000 : sample * 0x7fff;
    view.setInt16(index * 2, Math.round(value), true);
  }

  return new Uint8Array(buffer);
}

function downsampleTo8k(
  data: Float32Array,
  sourceSampleRate: number,
): Float32Array {
  if (data.length === 0 || sourceSampleRate <= CALL_CONTROL_SAMPLE_RATE) {
    return data;
  }

  const ratio = sourceSampleRate / CALL_CONTROL_SAMPLE_RATE;
  const resultLength = Math.max(1, Math.floor(data.length / ratio));
  const result = new Float32Array(resultLength);

  let sourceOffset = 0;
  for (let index = 0; index < resultLength; index += 1) {
    const nextSourceOffset = Math.min(
      data.length,
      Math.round((index + 1) * ratio),
    );
    let accumulator = 0;
    let count = 0;

    for (let cursor = sourceOffset; cursor < nextSourceOffset; cursor += 1) {
      accumulator += data[cursor];
      count += 1;
    }

    result[index] = count > 0 ? accumulator / count : 0;
    sourceOffset = nextSourceOffset;
  }

  return result;
}

function isAbortError(error: unknown): boolean {
  return error instanceof DOMException && error.name === "AbortError";
}

export function useSoftphoneSession() {
  const [state, dispatch] = useReducer(
    sessionReducer,
    loadAuthSession(),
    createInitialSessionState,
  );
  const hubConnectionRef = useRef<HubConnection | null>(null);
  const peerConnectionsRef = useRef<Map<string, RTCPeerConnection>>(new Map());
  const pendingIceCandidatesRef = useRef<Map<string, RTCIceCandidateInit[]>>(
    new Map(),
  );
  const pendingOffersRef = useRef<Map<string, string>>(new Map());
  const localStreamRef = useRef<MediaStream | null>(null);
  const remoteStreamRef = useRef<MediaStream | null>(null);
  const remoteAudioElementRef = useRef<HTMLAudioElement | null>(null);
  const sipRemoteAudioElementRef = useRef<HTMLAudioElement | null>(null);
  const browserCallsRef = useRef<BrowserCallView[]>([]);
  const sipUserRef = useRef<Web.SimpleUser | null>(null);
  const callControlActiveParticipantRef = useRef<number | null>(null);
  const callControlDownlinkAbortRef = useRef<AbortController | null>(null);
  const callControlUplinkAbortRef = useRef<AbortController | null>(null);
  const callControlUplinkControllerRef =
    useRef<ReadableStreamDefaultController<Uint8Array> | null>(null);
  const callControlPlaybackContextRef = useRef<AudioContext | null>(null);
  const callControlPlaybackDestinationRef =
    useRef<MediaStreamAudioDestinationNode | null>(null);
  const callControlPlaybackCursorRef = useRef(0);
  const callControlUplinkContextRef = useRef<AudioContext | null>(null);
  const callControlUplinkSourceRef = useRef<MediaStreamAudioSourceNode | null>(
    null,
  );
  const callControlUplinkProcessorRef = useRef<ScriptProcessorNode | null>(
    null,
  );
  const callControlUplinkMuteGainRef = useRef<GainNode | null>(null);
  const [microphonePermission, setMicrophonePermission] =
    useState<MicrophonePermission>("unknown");
  const [muted, setMuted] = useState(false);
  const [microphoneDevices, setMicrophoneDevices] = useState<
    AudioDeviceOption[]
  >([]);
  const [speakerDevices, setSpeakerDevices] = useState<AudioDeviceOption[]>([]);
  const [selectedMicrophoneDeviceId, setSelectedMicrophoneDeviceId] =
    useState("");
  const [selectedSpeakerDeviceId, setSelectedSpeakerDeviceId] = useState("");
  const [speakerSelectionSupported, setSpeakerSelectionSupported] =
    useState(false);
  const [sipRegistrationState, setSipRegistrationState] =
    useState<SipRegistrationState>("Disabled");
  const [sipRegistrationError, setSipRegistrationError] = useState<
    string | null
  >(null);
  const [sipCallInfo, setSipCallInfo] = useState<SipCallInfo>({
    state: "Idle",
    remoteIdentity: "",
  });

  useEffect(() => {
    browserCallsRef.current = state.browserCalls;
  }, [state.browserCalls]);

  const stopHub = useCallback(async () => {
    const connection = hubConnectionRef.current;
    hubConnectionRef.current = null;
    if (!connection) {
      return;
    }

    connection.off("SessionSnapshot");
    connection.off("SoftphoneEvent");
    connection.off("BrowserCallsSnapshot");
    connection.off("BrowserCallUpdated");
    connection.off("WebRtcSignal");
    await connection.stop();
  }, []);

  const pushClientEvent = useCallback((eventType: string, payload: unknown) => {
    dispatch({
      type: "PUSH_EVENT",
      payload: createClientEvent(eventType, payload),
    });
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
    const targets = [
      remoteAudioElementRef.current,
      sipRemoteAudioElementRef.current,
    ].filter((audio): audio is HTMLAudioElement => audio !== null);

    if (targets.length === 0) {
      return;
    }

    let supported = false;
    const operations: Promise<void>[] = [];

    for (const target of targets) {
      const sinkAwareElement = target as HTMLAudioElement & {
        setSinkId?: (sinkId: string) => Promise<void>;
      };
      if (typeof sinkAwareElement.setSinkId !== "function") {
        continue;
      }

      supported = true;
      if (deviceId.length > 0) {
        operations.push(sinkAwareElement.setSinkId(deviceId));
      }
    }

    setSpeakerSelectionSupported(supported);
    if (operations.length > 0) {
      await Promise.all(operations);
    }
  }, []);

  const refreshMediaDevices = useCallback(async () => {
    const mediaDevices = getMediaDevices();
    if (!mediaDevices || typeof mediaDevices.enumerateDevices !== "function") {
      return;
    }

    const devices = await mediaDevices.enumerateDevices();

    const audioInputs = devices
      .filter((device) => device.kind === "audioinput")
      .map((device, index) => ({
        deviceId: device.deviceId,
        label: device.label || `Microphone ${index + 1}`,
      }));

    const audioOutputs = devices
      .filter((device) => device.kind === "audiooutput")
      .map((device, index) => ({
        deviceId: device.deviceId,
        label: device.label || `Speaker ${index + 1}`,
      }));

    setMicrophoneDevices(audioInputs);
    setSpeakerDevices(audioOutputs);
    setSelectedMicrophoneDeviceId(
      (previous) => previous || audioInputs[0]?.deviceId || "",
    );
    setSelectedSpeakerDeviceId(
      (previous) => previous || audioOutputs[0]?.deviceId || "",
    );
  }, []);

  const replaceAudioTrack = useCallback(async (stream: MediaStream) => {
    const [track] = stream.getAudioTracks();
    if (!track) {
      return;
    }

    const operations: Promise<void>[] = [];
    for (const connection of peerConnectionsRef.current.values()) {
      const sender = connection
        .getSenders()
        .find((candidate) => candidate.track?.kind === "audio");
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
              exact: selectedMicrophoneDeviceId,
            },
          }
        : true;

    try {
      const mediaDevices = requireMediaDevicesWithGetUserMedia();
      const stream = await mediaDevices.getUserMedia({
        audio: audioConstraint,
        video: false,
      });

      stream.getAudioTracks().forEach((track) => {
        track.enabled = !muted;
      });

      localStreamRef.current = stream;
      setMicrophonePermission("granted");
      void refreshMediaDevices();
      return stream;
    } catch (error) {
      setMicrophonePermission("denied");
      throw error;
    }
  }, [muted, refreshMediaDevices, selectedMicrophoneDeviceId]);

  const stopCallControlAudioBridge = useCallback(async () => {
    callControlActiveParticipantRef.current = null;

    const downlinkAbort = callControlDownlinkAbortRef.current;
    callControlDownlinkAbortRef.current = null;
    downlinkAbort?.abort();

    const uplinkAbort = callControlUplinkAbortRef.current;
    callControlUplinkAbortRef.current = null;
    uplinkAbort?.abort();

    const uploadController = callControlUplinkControllerRef.current;
    callControlUplinkControllerRef.current = null;
    if (uploadController) {
      try {
        uploadController.close();
      } catch {
        // Controller can already be closed when call ends upstream.
      }
    }

    const uplinkProcessor = callControlUplinkProcessorRef.current;
    callControlUplinkProcessorRef.current = null;
    if (uplinkProcessor) {
      uplinkProcessor.onaudioprocess = null;
      uplinkProcessor.disconnect();
    }

    const uplinkSource = callControlUplinkSourceRef.current;
    callControlUplinkSourceRef.current = null;
    uplinkSource?.disconnect();

    const uplinkMuteGain = callControlUplinkMuteGainRef.current;
    callControlUplinkMuteGainRef.current = null;
    uplinkMuteGain?.disconnect();

    const playbackContext = callControlPlaybackContextRef.current;
    callControlPlaybackContextRef.current = null;
    callControlPlaybackDestinationRef.current = null;
    callControlPlaybackCursorRef.current = 0;

    const uplinkContext = callControlUplinkContextRef.current;
    callControlUplinkContextRef.current = null;

    if (playbackContext) {
      try {
        await playbackContext.close();
      } catch {
        // Closing an already-closed context is safe to ignore.
      }
    }

    if (uplinkContext) {
      try {
        await uplinkContext.close();
      } catch {
        // Closing an already-closed context is safe to ignore.
      }
    }

    const remoteAudio = sipRemoteAudioElementRef.current;
    if (remoteAudio) {
      remoteAudio.srcObject = null;
    }
  }, []);

  const ensureCallControlPlaybackPipeline = useCallback(async () => {
    if (
      callControlPlaybackContextRef.current &&
      callControlPlaybackDestinationRef.current
    ) {
      return;
    }

    const context = new AudioContext();
    const destination = context.createMediaStreamDestination();
    callControlPlaybackContextRef.current = context;
    callControlPlaybackDestinationRef.current = destination;
    callControlPlaybackCursorRef.current = context.currentTime;

    const remoteAudio = sipRemoteAudioElementRef.current;
    if (!remoteAudio) {
      return;
    }

    remoteAudio.autoplay = true;
    remoteAudio.srcObject = destination.stream;
    await remoteAudio.play().catch(() => undefined);

    if (selectedSpeakerDeviceId.length > 0) {
      await applySpeakerDevice(selectedSpeakerDeviceId).catch(() => undefined);
    }
  }, [applySpeakerDevice, selectedSpeakerDeviceId]);

  const queueCallControlDownlinkChunk = useCallback((chunk: Uint8Array) => {
    const context = callControlPlaybackContextRef.current;
    const destination = callControlPlaybackDestinationRef.current;
    if (!context || !destination || chunk.byteLength < 2) {
      return;
    }

    const samples = pcm16ToFloat32(chunk);
    if (samples.length === 0) {
      return;
    }

    const channelSamples = Float32Array.from(samples);
    const buffer = context.createBuffer(
      1,
      channelSamples.length,
      CALL_CONTROL_SAMPLE_RATE,
    );
    buffer.copyToChannel(channelSamples, 0);

    const source = context.createBufferSource();
    source.buffer = buffer;
    source.connect(destination);
    source.onended = () => {
      source.disconnect();
    };

    const startAt = Math.max(
      callControlPlaybackCursorRef.current,
      context.currentTime + 0.02,
    );
    source.start(startAt);
    callControlPlaybackCursorRef.current = startAt + buffer.duration;
  }, []);

  const startCallControlDownlink = useCallback(
    async (accessToken: string, participantId: number) => {
      const abortController = new AbortController();
      callControlDownlinkAbortRef.current = abortController;

      let response: Response | null = null;
      let reader: ReadableStreamDefaultReader<Uint8Array> | null = null;

      try {
        response = await openCallAudioDownlink(
          accessToken,
          participantId,
          abortController.signal,
        );
        reader = response.body?.getReader() ?? null;
        if (!reader) {
          throw new Error("Audio downlink stream is not available.");
        }

        while (true) {
          const { done, value } = await reader.read();
          if (done) {
            break;
          }

          if (value && value.byteLength > 0) {
            queueCallControlDownlinkChunk(value);
          }
        }
      } finally {
        if (callControlDownlinkAbortRef.current === abortController) {
          callControlDownlinkAbortRef.current = null;
        }

        if (reader) {
          await reader.cancel().catch(() => undefined);
        }

        if (response?.body) {
          await response.body.cancel().catch(() => undefined);
        }
      }
    },
    [queueCallControlDownlinkChunk],
  );

  const startCallControlUplink = useCallback(
    async (accessToken: string, participantId: number) => {
      const microphoneStream = await ensureLocalStream();
      const abortController = new AbortController();
      callControlUplinkAbortRef.current = abortController;

      const uploadStream = new ReadableStream<Uint8Array>({
        start(controller) {
          callControlUplinkControllerRef.current = controller;
        },
        cancel() {
          if (callControlUplinkControllerRef.current) {
            callControlUplinkControllerRef.current = null;
          }
        },
      });

      const context = new AudioContext();
      callControlUplinkContextRef.current = context;

      const source = context.createMediaStreamSource(microphoneStream);
      callControlUplinkSourceRef.current = source;

      const processor = context.createScriptProcessor(4096, 1, 1);
      callControlUplinkProcessorRef.current = processor;

      const muteGain = context.createGain();
      muteGain.gain.value = 0;
      callControlUplinkMuteGainRef.current = muteGain;

      source.connect(processor);
      processor.connect(muteGain);
      muteGain.connect(context.destination);

      processor.onaudioprocess = (event) => {
        if (callControlActiveParticipantRef.current !== participantId) {
          return;
        }

        const inputData = Float32Array.from(
          event.inputBuffer.getChannelData(0),
        );
        const downsampled = downsampleTo8k(
          inputData,
          event.inputBuffer.sampleRate,
        );
        if (downsampled.length === 0) {
          return;
        }

        const encodedChunk = float32ToPcm16(downsampled);
        const controller = callControlUplinkControllerRef.current;
        if (!controller) {
          return;
        }

        try {
          controller.enqueue(encodedChunk);
        } catch {
          // Queue can be closed while call is terminating.
        }
      };

      try {
        await uploadCallAudioUplink(
          accessToken,
          participantId,
          uploadStream,
          abortController.signal,
        );
      } finally {
        if (callControlUplinkAbortRef.current === abortController) {
          callControlUplinkAbortRef.current = null;
        }
      }
    },
    [ensureLocalStream],
  );

  const startCallControlAudioBridge = useCallback(
    async (accessToken: string, participantId: number) => {
      if (participantId <= 0) {
        return;
      }

      const alreadyRunning =
        callControlActiveParticipantRef.current === participantId &&
        callControlDownlinkAbortRef.current &&
        callControlUplinkAbortRef.current;
      if (alreadyRunning) {
        return;
      }

      await stopCallControlAudioBridge();
      callControlActiveParticipantRef.current = participantId;
      setSipRegistrationError(null);

      await ensureCallControlPlaybackPipeline();
      pushClientEvent("callcontrol.audio.bridge.started", { participantId });

      void startCallControlDownlink(accessToken, participantId).catch(
        (error) => {
          if (isAbortError(error)) {
            return;
          }

          const message = `Audio downlink failed: ${toMessage(error)}`;
          setSipRegistrationError(message);
          dispatch({ type: "SET_ERROR", payload: message });
          pushClientEvent("callcontrol.audio.downlink.failed", {
            participantId,
            message,
          });
        },
      );

      void startCallControlUplink(accessToken, participantId).catch((error) => {
        if (isAbortError(error)) {
          return;
        }

        const message = `Audio uplink failed: ${toMessage(error)}`;
        setSipRegistrationError(message);
        dispatch({ type: "SET_ERROR", payload: message });
        pushClientEvent("callcontrol.audio.uplink.failed", {
          participantId,
          message,
        });
      });
    },
    [
      ensureCallControlPlaybackPipeline,
      pushClientEvent,
      startCallControlDownlink,
      startCallControlUplink,
      stopCallControlAudioBridge,
    ],
  );

  const sendSignal = useCallback(async (payload: SignalPayload) => {
    const hub = hubConnectionRef.current;
    if (!hub) {
      throw new Error("Real-time signaling channel is not connected.");
    }

    await hub.invoke("SendWebRtcSignal", payload);
  }, []);

  const applyPendingIce = useCallback(
    async (callId: string, connection: RTCPeerConnection) => {
      const queuedCandidates = pendingIceCandidatesRef.current.get(callId);
      if (!queuedCandidates || queuedCandidates.length === 0) {
        return;
      }

      for (const candidate of queuedCandidates) {
        await connection.addIceCandidate(candidate);
      }

      pendingIceCandidatesRef.current.delete(callId);
    },
    [],
  );

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
          type: "ice",
          candidate: candidate.candidate,
          sdpMid: candidate.sdpMid ?? undefined,
          sdpMLineIndex: candidate.sdpMLineIndex ?? undefined,
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
        if (stateLabel === "connected") {
          const hub = hubConnectionRef.current;
          if (hub) {
            void hub.invoke("MarkCallConnected", callId).catch(() => undefined);
          }
          pushClientEvent("webrtc.connected", { callId });
        }

        if (stateLabel === "failed") {
          const hub = hubConnectionRef.current;
          if (hub) {
            void hub.invoke("EndBrowserCall", callId).catch(() => undefined);
          }
          pushClientEvent("webrtc.failed", { callId });
        }

        if (stateLabel === "closed") {
          closePeerConnection(callId);
        }
      };

      peerConnectionsRef.current.set(callId, connection);
      return connection;
    },
    [closePeerConnection, ensureLocalStream, pushClientEvent, sendSignal],
  );

  const answerWithPendingOffer = useCallback(
    async (callId: string) => {
      const offerSdp = pendingOffersRef.current.get(callId);
      if (!offerSdp) {
        return;
      }

      const connection = await ensurePeerConnection(callId);
      if (!connection.currentRemoteDescription) {
        await connection.setRemoteDescription({ type: "offer", sdp: offerSdp });
      }

      await applyPendingIce(callId, connection);
      const answer = await connection.createAnswer();
      await connection.setLocalDescription(answer);

      if (!answer.sdp) {
        throw new Error("Failed to generate SDP answer.");
      }

      await sendSignal({
        callId,
        type: "answer",
        sdp: answer.sdp,
      });

      pendingOffersRef.current.delete(callId);
    },
    [applyPendingIce, ensurePeerConnection, sendSignal],
  );

  const handleOfferSignal = useCallback(
    async (signal: WebRtcSignalMessage) => {
      if (!signal.sdp) {
        return;
      }

      pendingOffersRef.current.set(signal.callId, signal.sdp);
      const call = browserCallsRef.current.find(
        (candidate) => candidate.callId === signal.callId,
      );
      if (!call) {
        return;
      }

      const canAutoAnswer = !call.isIncoming || call.status !== "Ringing";
      if (!canAutoAnswer) {
        return;
      }

      await answerWithPendingOffer(signal.callId);
    },
    [answerWithPendingOffer],
  );

  const handleAnswerSignal = useCallback(
    async (signal: WebRtcSignalMessage) => {
      if (!signal.sdp) {
        return;
      }

      const connection = await ensurePeerConnection(signal.callId);
      if (!connection.currentRemoteDescription) {
        await connection.setRemoteDescription({
          type: "answer",
          sdp: signal.sdp,
        });
      }

      await applyPendingIce(signal.callId, connection);
    },
    [applyPendingIce, ensurePeerConnection],
  );

  const handleIceSignal = useCallback(async (signal: WebRtcSignalMessage) => {
    if (!signal.candidate) {
      return;
    }

    const candidate: RTCIceCandidateInit = {
      candidate: signal.candidate,
      sdpMid: signal.sdpMid ?? undefined,
      sdpMLineIndex: signal.sdpMLineIndex ?? undefined,
    };

    const connection = peerConnectionsRef.current.get(signal.callId);
    if (!connection || !connection.remoteDescription) {
      const queue = pendingIceCandidatesRef.current.get(signal.callId) ?? [];
      queue.push(candidate);
      pendingIceCandidatesRef.current.set(signal.callId, queue);
      return;
    }

    await connection.addIceCandidate(candidate);
  }, []);

  const routeSignal = useCallback(
    async (signal: WebRtcSignalMessage) => {
      switch (signal.type) {
        case "offer":
          await handleOfferSignal(signal);
          break;
        case "answer":
          await handleAnswerSignal(signal);
          break;
        case "ice":
          await handleIceSignal(signal);
          break;
        default:
          break;
      }
    },
    [handleAnswerSignal, handleIceSignal, handleOfferSignal],
  );

  const endSimpleUserCall = useCallback(async (simpleUser: Web.SimpleUser) => {
    const candidate = simpleUser as unknown as {
      hangup?: () => Promise<void>;
      decline?: () => Promise<void>;
      reject?: () => Promise<void>;
    };

    if (typeof candidate.hangup === "function") {
      await candidate.hangup();
      return;
    }

    if (typeof candidate.decline === "function") {
      await candidate.decline();
      return;
    }

    if (typeof candidate.reject === "function") {
      await candidate.reject();
    }
  }, []);

  const stopSip = useCallback(
    async (nextState: SipRegistrationState = "Disabled") => {
      const simpleUser = sipUserRef.current;
      sipUserRef.current = null;

      if (simpleUser) {
        try {
          await endSimpleUserCall(simpleUser);
        } catch {
          // A call might not exist, which is acceptable during teardown.
        }

        try {
          await simpleUser.unregister();
        } catch {
          // Unregister can fail if the transport is already closed.
        }

        try {
          await simpleUser.disconnect();
        } catch {
          // Disconnect is best-effort during cleanup.
        }
      }

      setSipCallInfo({
        state: "Idle",
        remoteIdentity: "",
      });
      setSipRegistrationState(nextState);
    },
    [endSimpleUserCall],
  );

  const connectSip = useCallback(
    async (accessToken: string) => {
      if (!DIRECT_SIP_JS_ENABLED) {
        setSipRegistrationState("Disabled");
        setSipRegistrationError(SIP_DISABLED_MESSAGE);
        setSipCallInfo({
          state: "Idle",
          remoteIdentity: "",
        });
        pushClientEvent("sip.disabled", { reason: "unsupported_endpoint" });
        return;
      }

      await stopSip("Connecting");
      setSipRegistrationError(null);

      const config = await getSipRegistrationConfig(accessToken);
      if (!config.enabled) {
        setSipRegistrationState("Disabled");
        pushClientEvent("sip.disabled", {});
        return;
      }

      const remoteAudio = sipRemoteAudioElementRef.current ?? new Audio();
      remoteAudio.autoplay = true;
      if (!sipRemoteAudioElementRef.current) {
        sipRemoteAudioElementRef.current = remoteAudio;
      }

      const mediaConstraint: MediaTrackConstraints | boolean =
        selectedMicrophoneDeviceId.length > 0
          ? {
              deviceId: {
                exact: selectedMicrophoneDeviceId,
              },
            }
          : true;

      const iceServers: RTCIceServer[] = config.iceServers.map((server) => ({
        urls: server,
      }));

      let simpleUser: Web.SimpleUser | null = null;

      const delegate = {
        onRegistered: () => {
          setSipRegistrationState("Registered");
          pushClientEvent("sip.registered", {});
        },
        onUnregistered: () => {
          setSipRegistrationState("Connecting");
          pushClientEvent("sip.unregistered", {});
        },
        onServerDisconnect: () => {
          setSipRegistrationState("Failed");
          setSipRegistrationError("SIP transport disconnected.");
          pushClientEvent("sip.transport.disconnected", {});
        },
        onCallReceived: () => {
          const remoteIdentity = resolveRemoteIdentity(simpleUser);
          setSipCallInfo({
            state: "Ringing",
            remoteIdentity,
          });
          pushClientEvent("sip.call.ringing", { remoteIdentity });
        },
        onCallAnswered: () => {
          const remoteIdentity = resolveRemoteIdentity(simpleUser);
          setSipCallInfo({
            state: "Connected",
            remoteIdentity,
          });
          pushClientEvent("sip.call.connected", { remoteIdentity });
        },
        onCallHangup: () => {
          setSipCallInfo({
            state: "Idle",
            remoteIdentity: "",
          });
          pushClientEvent("sip.call.ended", {});
        },
      } as Web.SimpleUserDelegate;

      const options = {
        aor: config.aor,
        delegate,
        media: {
          constraints: {
            audio: mediaConstraint,
            video: false,
          },
          remote: {
            audio: remoteAudio,
          },
        },
        sessionDescriptionHandlerFactoryOptions: {
          peerConnectionConfiguration: {
            iceServers,
          },
        },
        userAgentOptions: {
          authorizationUsername: config.authorizationUsername,
          authorizationPassword: config.authorizationPassword,
          displayName: config.displayName,
        },
      } as unknown as Web.SimpleUserOptions;

      simpleUser = new Web.SimpleUser(config.webSocketUrl, options);
      sipUserRef.current = simpleUser;

      await simpleUser.connect();
      await simpleUser.register();
      setSipRegistrationState("Registered");
      pushClientEvent("sip.connecting", {
        aor: config.aor,
        webSocketUrl: config.webSocketUrl,
      });
    },
    [pushClientEvent, selectedMicrophoneDeviceId, stopSip],
  );

  const resetToLoggedOut = useCallback(async () => {
    await stopHub();
    await stopSip();
    await stopCallControlAudioBridge();
    closeAllPeerConnections();
    stopLocalStream();
    clearAuthSession();
    dispatch({ type: "SET_AUTH", payload: null });
    dispatch({ type: "SET_SNAPSHOT", payload: null });
    dispatch({ type: "CLEAR_BROWSER_CALLS" });
    dispatch({ type: "CLEAR_EVENTS" });
    setSipRegistrationError(null);
    setSipRegistrationState("Disabled");
  }, [
    closeAllPeerConnections,
    stopCallControlAudioBridge,
    stopHub,
    stopLocalStream,
    stopSip,
  ]);

  const refreshSnapshot = useCallback(async (accessToken: string) => {
    const snapshot = await getSessionSnapshot(accessToken);
    dispatch({ type: "SET_SNAPSHOT", payload: snapshot });
  }, []);

  const runCallControlCallMutation = useCallback(
    async (
      resolveTarget: (calls: SoftphoneCallView[]) => SoftphoneCallView | null,
      operation: (accessToken: string, participantId: number) => Promise<void>,
      noCallMessage: string,
    ) => {
      const auth = state.auth;
      if (!auth) {
        throw new Error("Not authenticated");
      }

      const calls = state.snapshot?.calls ?? [];
      const target = resolveTarget(calls);
      if (!target) {
        dispatch({ type: "SET_ERROR", payload: noCallMessage });
        return;
      }

      dispatch({ type: "SET_BUSY", payload: true });
      dispatch({ type: "SET_ERROR", payload: null });

      try {
        await operation(auth.accessToken, target.participantId);
        await refreshSnapshot(auth.accessToken);
      } catch (error) {
        if (isUnauthorized(error)) {
          await resetToLoggedOut();
          dispatch({
            type: "SET_ERROR",
            payload: "Authentication failed. Login again.",
          });
          return;
        }

        dispatch({ type: "SET_ERROR", payload: toMessage(error) });
      } finally {
        dispatch({ type: "SET_BUSY", payload: false });
      }
    },
    [refreshSnapshot, resetToLoggedOut, state.auth, state.snapshot?.calls],
  );

  const answerSipCall = useCallback(async () => {
    await runCallControlCallMutation(
      (calls) => findRingingPbxCall(calls),
      (accessToken, participantId) => answerCallApi(accessToken, participantId),
      "No ringing 3CX call is available.",
    );
  }, [runCallControlCallMutation]);

  const rejectSipCall = useCallback(async () => {
    await runCallControlCallMutation(
      (calls) => findRingingPbxCall(calls),
      (accessToken, participantId) => rejectCallApi(accessToken, participantId),
      "No ringing 3CX call is available.",
    );
  }, [runCallControlCallMutation]);

  const hangupSipCall = useCallback(async () => {
    await runCallControlCallMutation(
      (calls) => findActionablePbxCall(calls),
      (accessToken, participantId) => endCallApi(accessToken, participantId),
      "No active 3CX call is available.",
    );
  }, [runCallControlCallMutation]);

  const connectHub = useCallback(
    async (accessToken: string) => {
      await stopHub();

      const bindHandlers = (hub: HubConnection) => {
        hub.on("SessionSnapshot", (snapshot: SessionSnapshotResponse) => {
          dispatch({ type: "SET_SNAPSHOT", payload: snapshot });
        });

        hub.on("SoftphoneEvent", (envelope: SoftphoneEventEnvelope) => {
          dispatch({ type: "PUSH_EVENT", payload: envelope });
        });

        hub.on("BrowserCallsSnapshot", (calls: BrowserCallView[]) => {
          dispatch({ type: "SET_BROWSER_CALLS", payload: calls });
        });

        hub.on("BrowserCallUpdated", (call: BrowserCallView) => {
          dispatch({ type: "UPSERT_BROWSER_CALL", payload: call });
          dispatch({
            type: "PUSH_EVENT",
            payload: createClientEvent(
              `browser.call.${call.status.toLowerCase()}`,
              {
                callId: call.callId,
                remoteExtension: call.remoteExtension,
                endReason: call.endReason ?? null,
              },
            ),
          });

          if (call.status === "Ended") {
            closePeerConnection(call.callId);
            return;
          }

          if (
            call.isIncoming &&
            call.status !== "Ringing" &&
            pendingOffersRef.current.has(call.callId)
          ) {
            void answerWithPendingOffer(call.callId).catch(() => undefined);
          }
        });

        hub.on("WebRtcSignal", (signal: WebRtcSignalMessage) => {
          dispatch({
            type: "PUSH_EVENT",
            payload: createClientEvent(`webrtc.signal.${signal.type}`, {
              callId: signal.callId,
              fromExtension: signal.fromExtension,
            }),
          });
          void routeSignal(signal).catch(() => undefined);
        });

        hub.onclose((error?: Error) => {
          if (error) {
            dispatch({
              type: "SET_ERROR",
              payload: "Real-time connection was interrupted. Reconnecting...",
            });
          }
        });

        hub.onreconnected(() => {
          dispatch({ type: "SET_ERROR", payload: null });
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
    [answerWithPendingOffer, closePeerConnection, routeSignal, stopHub],
  );

  useEffect(() => {
    let disposed = false;

    const bootstrap = async () => {
      const auth = state.auth;
      if (!auth) {
        dispatch({ type: "SET_BOOTSTRAP_LOADING", payload: false });
        return;
      }

      if (isAuthSessionExpired(auth)) {
        await resetToLoggedOut();
        if (!disposed) {
          dispatch({
            type: "SET_ERROR",
            payload: "Session has expired. Login again.",
          });
          dispatch({ type: "SET_BOOTSTRAP_LOADING", payload: false });
        }
        return;
      }

      if (!auth.hasSoftphoneAccess) {
        setSipRegistrationState("Disabled");
        setSipRegistrationError(null);
        dispatch({ type: "SET_SNAPSHOT", payload: null });
        dispatch({ type: "SET_BOOTSTRAP_LOADING", payload: false });
        return;
      }

      dispatch({ type: "SET_BOOTSTRAP_LOADING", payload: true });
      dispatch({ type: "SET_ERROR", payload: null });

      try {
        await refreshSnapshot(auth.accessToken);
        await connectHub(auth.accessToken);

        if (DIRECT_SIP_JS_ENABLED) {
          try {
            await connectSip(auth.accessToken);
          } catch (error) {
            if (!disposed) {
              setSipRegistrationState("Failed");
              setSipRegistrationError(toMessage(error));
              pushClientEvent("sip.registration.failed", {
                message: toMessage(error),
              });
            }
          }
        } else {
          setSipRegistrationState("Registered");
          setSipRegistrationError(null);
          pushClientEvent("callcontrol.audio.bridge.ready", {});
        }
      } catch (error) {
        if (disposed) {
          return;
        }

        if (isUnauthorized(error)) {
          await resetToLoggedOut();
          dispatch({
            type: "SET_ERROR",
            payload: "Authentication failed. Login again.",
          });
        } else {
          dispatch({ type: "SET_ERROR", payload: toMessage(error) });
        }
      } finally {
        if (!disposed) {
          dispatch({ type: "SET_BOOTSTRAP_LOADING", payload: false });
        }
      }
    };

    void bootstrap();

    return () => {
      disposed = true;
    };
  }, [
    state.auth,
    connectHub,
    connectSip,
    pushClientEvent,
    refreshSnapshot,
    resetToLoggedOut,
  ]);

  useEffect(() => {
    const calls = state.snapshot?.calls ?? [];
    const connectedCall = findConnectedPbxCall(calls);
    const ringingCall = findRingingPbxCall(calls);
    const displayCall = connectedCall ?? ringingCall;

    if (!displayCall) {
      setSipCallInfo({
        state: "Idle",
        remoteIdentity: "",
      });
    } else {
      setSipCallInfo({
        state: connectedCall ? "Connected" : "Ringing",
        remoteIdentity: resolvePbxRemoteIdentity(displayCall),
      });
    }

    const auth = state.auth;
    if (!auth || !connectedCall) {
      if (callControlActiveParticipantRef.current !== null) {
        void stopCallControlAudioBridge();
      }
      return;
    }

    void startCallControlAudioBridge(
      auth.accessToken,
      connectedCall.participantId,
    ).catch((error) => {
      if (isAbortError(error)) {
        return;
      }

      const message = `Audio bridge failed: ${toMessage(error)}`;
      setSipRegistrationError(message);
      dispatch({ type: "SET_ERROR", payload: message });
      pushClientEvent("callcontrol.audio.bridge.failed", {
        message,
        participantId: connectedCall.participantId,
      });
    });
  }, [
    pushClientEvent,
    startCallControlAudioBridge,
    state.auth,
    state.snapshot?.calls,
    stopCallControlAudioBridge,
  ]);

  useEffect(() => {
    return () => {
      closeAllPeerConnections();
      stopLocalStream();
      void stopHub();
      void stopSip();
      void stopCallControlAudioBridge();
    };
  }, [
    closeAllPeerConnections,
    stopCallControlAudioBridge,
    stopHub,
    stopLocalStream,
    stopSip,
  ]);

  const runMutation = useCallback(
    async (operation: (session: StoredAuthSession) => Promise<void>) => {
      const auth = state.auth;
      if (!auth) {
        throw new Error("Not authenticated");
      }

      dispatch({ type: "SET_BUSY", payload: true });
      dispatch({ type: "SET_ERROR", payload: null });

      try {
        await operation(auth);
        await refreshSnapshot(auth.accessToken);
      } catch (error) {
        if (isUnauthorized(error)) {
          await resetToLoggedOut();
          dispatch({
            type: "SET_ERROR",
            payload: "Authentication failed. Login again.",
          });
          return;
        }

        dispatch({ type: "SET_ERROR", payload: toMessage(error) });
      } finally {
        dispatch({ type: "SET_BUSY", payload: false });
      }
    },
    [state.auth, refreshSnapshot, resetToLoggedOut],
  );

  const login = useCallback(async (request: LoginRequest) => {
    dispatch({ type: "SET_BUSY", payload: true });
    dispatch({ type: "SET_ERROR", payload: null });

    try {
      const response = await loginApi(request);
      const authSession: StoredAuthSession = {
        userId: response.userId,
        accessToken: response.accessToken,
        expiresAtUtc: response.expiresAtUtc,
        sessionId: response.sessionId,
        username: response.username,
        displayName: response.displayName,
        role: response.role,
        hasSoftphoneAccess: response.hasSoftphoneAccess,
        ownedExtensionDn: response.ownedExtensionDn,
        pbxBase: response.pbxBase,
      };

      saveAuthSession(authSession);
      dispatch({ type: "SET_AUTH", payload: authSession });
      dispatch({ type: "SET_BOOTSTRAP_LOADING", payload: true });
      dispatch({ type: "CLEAR_EVENTS" });
    } catch (error) {
      dispatch({ type: "SET_ERROR", payload: toMessage(error) });
    } finally {
      dispatch({ type: "SET_BUSY", payload: false });
    }
  }, []);

  const logout = useCallback(async () => {
    const auth = state.auth;
    dispatch({ type: "SET_BUSY", payload: true });
    dispatch({ type: "SET_ERROR", payload: null });

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
      dispatch({ type: "SET_BUSY", payload: false });
      dispatch({ type: "SET_BOOTSTRAP_LOADING", payload: false });
    }
  }, [state.auth, resetToLoggedOut]);

  const selectExtension = useCallback(
    async (extensionDn: string) => {
      await runMutation(async (session) => {
        await selectExtensionApi(session.accessToken, extensionDn);
      });
    },
    [runMutation],
  );

  const setActivePbxDevice = useCallback(
    async (deviceId: string) => {
      await runMutation(async (session) => {
        await setActiveDeviceApi(session.accessToken, deviceId);
      });
    },
    [runMutation],
  );

  const runHubMutation = useCallback(
    async (operation: (hub: HubConnection) => Promise<void>) => {
      const auth = state.auth;
      if (!auth) {
        throw new Error("Not authenticated");
      }

      const hub = hubConnectionRef.current;
      if (!hub) {
        throw new Error("Real-time hub is not connected.");
      }

      dispatch({ type: "SET_BUSY", payload: true });
      dispatch({ type: "SET_ERROR", payload: null });

      try {
        await operation(hub);
      } catch (error) {
        if (isHubUnauthorized(error)) {
          await resetToLoggedOut();
          dispatch({
            type: "SET_ERROR",
            payload: "Authentication failed. Login again.",
          });
          return;
        }

        dispatch({ type: "SET_ERROR", payload: toMessage(error) });
      } finally {
        dispatch({ type: "SET_BUSY", payload: false });
      }
    },
    [resetToLoggedOut, state.auth],
  );

  const makeOutgoingCall = useCallback(
    async (destinationExtension: string) => {
      await runHubMutation(async (hub) => {
        const destination = destinationExtension.trim();
        if (destination.length === 0) {
          throw new Error("Destination extension is required.");
        }

        await ensureLocalStream();
        const call = await hub.invoke<BrowserCallView>(
          "PlaceBrowserCall",
          destination,
        );
        dispatch({ type: "UPSERT_BROWSER_CALL", payload: call });

        const connection = await ensurePeerConnection(call.callId);
        const offer = await connection.createOffer();
        await connection.setLocalDescription(offer);

        if (!offer.sdp) {
          throw new Error("Failed to create SDP offer.");
        }

        await sendSignal({
          callId: call.callId,
          type: "offer",
          sdp: offer.sdp,
        });
      });
    },
    [ensureLocalStream, ensurePeerConnection, runHubMutation, sendSignal],
  );

  const makePbxOutgoingCall = useCallback(
    async (destination: string) => {
      const normalizedDestination = destination.trim();
      if (normalizedDestination.length === 0) {
        dispatch({ type: "SET_ERROR", payload: "Destination is required." });
        return;
      }

      await runMutation(async (session) => {
        await makePbxOutgoingCallApi(
          session.accessToken,
          normalizedDestination,
        );
      });
    },
    [runMutation],
  );

  const answerCall = useCallback(
    async (callId: string) => {
      await runHubMutation(async (hub) => {
        await ensureLocalStream();
        await hub.invoke("AnswerBrowserCall", callId);
        await ensurePeerConnection(callId);
        await answerWithPendingOffer(callId);
      });
    },
    [
      answerWithPendingOffer,
      ensureLocalStream,
      ensurePeerConnection,
      runHubMutation,
    ],
  );

  const rejectCall = useCallback(
    async (callId: string) => {
      await runHubMutation(async (hub) => {
        await hub.invoke("RejectBrowserCall", callId);
        closePeerConnection(callId);
      });
    },
    [closePeerConnection, runHubMutation],
  );

  const endCall = useCallback(
    async (callId: string) => {
      await runHubMutation(async (hub) => {
        await hub.invoke("EndBrowserCall", callId);
        closePeerConnection(callId);
      });
    },
    [closePeerConnection, runHubMutation],
  );

  const requestMicrophone = useCallback(async () => {
    dispatch({ type: "SET_ERROR", payload: null });
    try {
      await ensureLocalStream();
      pushClientEvent("microphone.granted", {});
    } catch (error) {
      dispatch({ type: "SET_ERROR", payload: toMessage(error) });
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
              exact: deviceId,
            },
          },
          video: false,
        });

        stream.getAudioTracks().forEach((track) => {
          track.enabled = !muted;
        });

        const previous = localStreamRef.current;
        localStreamRef.current = stream;
        await replaceAudioTrack(stream);
        previous.getTracks().forEach((track) => track.stop());
        setMicrophonePermission("granted");
      } catch (error) {
        dispatch({ type: "SET_ERROR", payload: toMessage(error) });
      }
    },
    [muted, replaceAudioTrack],
  );

  const setSpeakerDevice = useCallback(
    async (deviceId: string) => {
      setSelectedSpeakerDeviceId(deviceId);
      try {
        await applySpeakerDevice(deviceId);
      } catch (error) {
        dispatch({ type: "SET_ERROR", payload: toMessage(error) });
      }
    },
    [applySpeakerDevice],
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
        const sinkAwareElement = element as HTMLAudioElement & {
          setSinkId?: (sinkId: string) => Promise<void>;
        };
        const sipSinkAwareElement = sipRemoteAudioElementRef.current as
          | (HTMLAudioElement & {
              setSinkId?: (sinkId: string) => Promise<void>;
            })
          | null;
        setSpeakerSelectionSupported(
          typeof sinkAwareElement.setSinkId === "function" ||
            typeof sipSinkAwareElement?.setSinkId === "function",
        );
      }
    },
    [applySpeakerDevice, selectedSpeakerDeviceId],
  );

  const setSipRemoteAudioElement = useCallback(
    (element: HTMLAudioElement | null) => {
      sipRemoteAudioElementRef.current = element;
      if (!element) {
        return;
      }

      element.autoplay = true;
      if (callControlPlaybackDestinationRef.current) {
        element.srcObject = callControlPlaybackDestinationRef.current.stream;
        void element.play().catch(() => undefined);
      }

      if (selectedSpeakerDeviceId.length > 0) {
        void applySpeakerDevice(selectedSpeakerDeviceId).catch(() => undefined);
      } else {
        const sinkAwareElement = element as HTMLAudioElement & {
          setSinkId?: (sinkId: string) => Promise<void>;
        };
        const browserSinkAwareElement = remoteAudioElementRef.current as
          | (HTMLAudioElement & {
              setSinkId?: (sinkId: string) => Promise<void>;
            })
          | null;
        setSpeakerSelectionSupported(
          typeof sinkAwareElement.setSinkId === "function" ||
            typeof browserSinkAwareElement?.setSinkId === "function",
        );
      }
    },
    [applySpeakerDevice, selectedSpeakerDeviceId],
  );

  useEffect(() => {
    const stream = localStreamRef.current;
    if (stream) {
      stream.getAudioTracks().forEach((track) => {
        track.enabled = !muted;
      });
    }

    const simpleUser = sipUserRef.current as unknown as {
      mute?: () => Promise<void>;
      unmute?: () => Promise<void>;
    } | null;

    if (simpleUser) {
      if (muted && typeof simpleUser.mute === "function") {
        void simpleUser.mute().catch(() => undefined);
      }

      if (!muted && typeof simpleUser.unmute === "function") {
        void simpleUser.unmute().catch(() => undefined);
      }
    }
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

    mediaDevices.addEventListener("devicechange", handler);
    return () => {
      active = false;
      mediaDevices.removeEventListener("devicechange", handler);
    };
  }, [refreshMediaDevices]);

  return {
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
    hangupSipCall,
  };
}
