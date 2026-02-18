import type {
  LoginRequest,
  LoginResponse,
  SessionSnapshotResponse
} from '../domain/softphone';
import { requestJson, requestNoContent } from './httpClient';

export function login(request: LoginRequest): Promise<LoginResponse> {
  return requestJson<LoginResponse>('/api/auth/login', {
    method: 'POST',
    body: JSON.stringify(request)
  });
}

export function logout(accessToken: string): Promise<void> {
  return requestNoContent(
    '/api/auth/logout',
    {
      method: 'POST'
    },
    accessToken
  );
}

export function getSessionSnapshot(accessToken: string): Promise<SessionSnapshotResponse> {
  return requestJson<SessionSnapshotResponse>(
    '/api/softphone/session',
    {
      method: 'GET'
    },
    accessToken
  );
}

export function selectExtension(accessToken: string, extensionDn: string): Promise<void> {
  return requestNoContent(
    '/api/softphone/extensions/select',
    {
      method: 'POST',
      body: JSON.stringify({ extensionDn })
    },
    accessToken
  );
}

export function setActiveDevice(accessToken: string, deviceId: string): Promise<void> {
  return requestNoContent(
    '/api/softphone/devices/active',
    {
      method: 'POST',
      body: JSON.stringify({ deviceId })
    },
    accessToken
  );
}

export function makeOutgoingCall(accessToken: string, destination: string): Promise<void> {
  return requestNoContent(
    '/api/softphone/calls/outgoing',
    {
      method: 'POST',
      body: JSON.stringify({ destination })
    },
    accessToken
  );
}

export function answerCall(accessToken: string, participantId: number): Promise<void> {
  return requestNoContent(
    `/api/softphone/calls/${participantId}/answer`,
    {
      method: 'POST'
    },
    accessToken
  );
}

export function rejectCall(accessToken: string, participantId: number): Promise<void> {
  return requestNoContent(
    `/api/softphone/calls/${participantId}/reject`,
    {
      method: 'POST'
    },
    accessToken
  );
}

export function endCall(accessToken: string, participantId: number): Promise<void> {
  return requestNoContent(
    `/api/softphone/calls/${participantId}/end`,
    {
      method: 'POST'
    },
    accessToken
  );
}

export function transferCall(accessToken: string, participantId: number, destination: string): Promise<void> {
  return requestNoContent(
    `/api/softphone/calls/${participantId}/transfer`,
    {
      method: 'POST',
      body: JSON.stringify({ destination })
    },
    accessToken
  );
}
