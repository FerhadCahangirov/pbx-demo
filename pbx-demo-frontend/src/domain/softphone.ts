export interface LoginRequest {
  username: string;
  password: string;
}

export interface LoginResponse {
  userId: number;
  accessToken: string;
  expiresAtUtc: string;
  sessionId: string;
  username: string;
  displayName: string;
  role: 'User' | 'Supervisor' | string;
  hasSoftphoneAccess: boolean;
  ownedExtensionDn: string;
  pbxBase: string;
}

export interface StoredAuthSession {
  userId: number;
  accessToken: string;
  expiresAtUtc: string;
  sessionId: string;
  username: string;
  displayName: string;
  role: 'User' | 'Supervisor' | string;
  hasSoftphoneAccess: boolean;
  ownedExtensionDn: string;
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
  dn?: string | null;
  partyDnType?: string | null;
  callId?: number | null;
  legId?: number | null;
  status?: string | null;
  remoteParty?: string | null;
  remoteName?: string | null;
  direction: SoftphoneCallDirection;
  directControl: boolean;
  answerable: boolean;
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
  controlDn?: string | null;
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

export interface SipRegistrationConfigResponse {
  enabled: boolean;
  webSocketUrl: string;
  domain: string;
  aor: string;
  authorizationUsername: string;
  authorizationPassword: string;
  displayName: string;
  iceServers: string[];
}

export type SipRegistrationState = 'Disabled' | 'Connecting' | 'Registered' | 'Failed';

export type SipCallState = 'Idle' | 'Ringing' | 'Connected';

export interface SipCallInfo {
  state: SipCallState;
  remoteIdentity: string;
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
