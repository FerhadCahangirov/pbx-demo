export interface LoginRequest {
  username: string;
  password: string;
  pbxBase: string;
  appId: string;
  appSecret: string;
}

export interface LoginResponse {
  accessToken: string;
  expiresAtUtc: string;
  sessionId: string;
  username: string;
}

export interface StoredAuthSession {
  accessToken: string;
  expiresAtUtc: string;
  sessionId: string;
  username: string;
  pbxBase: string;
}

export interface SoftphoneDeviceView {
  dn?: string | null;
  deviceId?: string | null;
  userAgent?: string | null;
}

export type SoftphoneCallDirection = 'Incoming' | 'Outgoing' | 0 | 1;

export interface SoftphoneCallView {
  participantId: number;
  callId?: number | null;
  legId?: number | null;
  status?: string | null;
  remoteParty?: string | null;
  remoteName?: string | null;
  direction: SoftphoneCallDirection;
  directControl: boolean;
  connectedAtUtc?: string | null;
}

export type BrowserCallStatus = 'Ringing' | 'Connecting' | 'Connected' | 'Ended';

export interface BrowserCallView {
  callId: string;
  status: BrowserCallStatus;
  localExtension: string;
  remoteExtension: string;
  remoteUsername: string;
  isIncoming: boolean;
  createdAtUtc: string;
  answeredAtUtc?: string | null;
  endedAtUtc?: string | null;
  endReason?: string | null;
}

export interface WebRtcSignalMessage {
  callId: string;
  type: 'offer' | 'answer' | 'ice';
  sdp?: string | null;
  candidate?: string | null;
  sdpMid?: string | null;
  sdpMLineIndex?: number | null;
  fromExtension: string;
  toExtension: string;
  sentAtUtc: string;
}

export interface SessionSnapshotResponse {
  connected: boolean;
  username: string;
  sessionId: string;
  selectedExtensionDn?: string | null;
  ownedExtensionDn: string;
  devices: SoftphoneDeviceView[];
  activeDeviceId?: string | null;
  calls: SoftphoneCallView[];
  wsConnected: boolean;
  lastUpdatedUtc: string;
}

export interface SoftphoneEventEnvelope {
  eventType: string;
  occurredAtUtc: string;
  payload: unknown;
}

export function isIncomingCall(call: SoftphoneCallView): boolean {
  if (call.direction === 'Incoming' || call.direction === 0) {
    return true;
  }

  if (call.status?.toLowerCase() === 'ringing' && call.directControl) {
    return true;
  }

  return false;
}
