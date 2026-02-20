import type {
  LoginRequest,
  LoginResponse,
  SessionSnapshotResponse,
  SipRegistrationConfigResponse,
} from "../domain/softphone";
import {
  ApiRequestError,
  buildApiUrl,
  requestJson,
  requestNoContent,
} from "./httpClient";

export function login(request: LoginRequest): Promise<LoginResponse> {
  return requestJson<LoginResponse>("/api/auth/login", {
    method: "POST",
    body: JSON.stringify(request),
  });
}

export function logout(accessToken: string): Promise<void> {
  return requestNoContent(
    "/api/auth/logout",
    {
      method: "POST",
    },
    accessToken,
  );
}

export function getSessionSnapshot(
  accessToken: string,
): Promise<SessionSnapshotResponse> {
  return requestJson<SessionSnapshotResponse>(
    "/api/softphone/session",
    {
      method: "GET",
    },
    accessToken,
  );
}

export function getSipRegistrationConfig(
  accessToken: string,
): Promise<SipRegistrationConfigResponse> {
  return requestJson<SipRegistrationConfigResponse>(
    "/api/softphone/sip/config",
    {
      method: "GET",
    },
    accessToken,
  );
}

export function selectExtension(
  accessToken: string,
  extensionDn: string,
): Promise<void> {
  return requestNoContent(
    "/api/softphone/extensions/select",
    {
      method: "POST",
      body: JSON.stringify({ extensionDn }),
    },
    accessToken,
  );
}

export function setActiveDevice(
  accessToken: string,
  deviceId: string,
): Promise<void> {
  return requestNoContent(
    "/api/softphone/devices/active",
    {
      method: "POST",
      body: JSON.stringify({ deviceId }),
    },
    accessToken,
  );
}

export function makeOutgoingCall(
  accessToken: string,
  destination: string,
): Promise<void> {
  return requestNoContent(
    "/api/softphone/calls/outgoing",
    {
      method: "POST",
      body: JSON.stringify({ destination }),
    },
    accessToken,
  );
}

export function answerCall(
  accessToken: string,
  participantId: number,
): Promise<void> {
  return requestNoContent(
    `/api/softphone/calls/${participantId}/answer`,
    {
      method: "POST",
    },
    accessToken,
  );
}

export function rejectCall(
  accessToken: string,
  participantId: number,
): Promise<void> {
  return requestNoContent(
    `/api/softphone/calls/${participantId}/reject`,
    {
      method: "POST",
    },
    accessToken,
  );
}

export function endCall(
  accessToken: string,
  participantId: number,
): Promise<void> {
  return requestNoContent(
    `/api/softphone/calls/${participantId}/end`,
    {
      method: "POST",
    },
    accessToken,
  );
}

export function transferCall(
  accessToken: string,
  participantId: number,
  destination: string,
): Promise<void> {
  return requestNoContent(
    `/api/softphone/calls/${participantId}/transfer`,
    {
      method: "POST",
      body: JSON.stringify({ destination }),
    },
    accessToken,
  );
}

export async function openCallAudioDownlink(
  accessToken: string,
  participantId: number,
  signal?: AbortSignal,
): Promise<Response> {
  const response = await fetch(
    buildApiUrl(`/api/softphone/calls/${participantId}/audio`),
    {
      method: "GET",
      headers: {
        Authorization: `Bearer ${accessToken}`,
        Accept: "application/octet-stream",
      },
      signal,
    },
  );

  if (!response.ok) {
    const payload = await readErrorPayload(response);
    throw new ApiRequestError(
      response.status,
      errorMessageFromPayload(payload),
      payload,
    );
  }

  if (!response.body) {
    throw new Error("Audio downlink stream is not available.");
  }

  return response;
}

export async function uploadCallAudioUplink(
  accessToken: string,
  participantId: number,
  body: ReadableStream<Uint8Array>,
  signal?: AbortSignal,
): Promise<void> {
  const requestOptions = {
    method: "POST",
    headers: {
      Authorization: `Bearer ${accessToken}`,
      Accept: "application/json",
      "Content-Type": "application/octet-stream",
    },
    body,
    signal,
    duplex: "half",
  } as RequestInit & { duplex: "half" };

  const response = await fetch(
    buildApiUrl(`/api/softphone/calls/${participantId}/audio`),
    requestOptions,
  );
  if (response.status === 202 || response.status === 204) {
    return;
  }

  if (!response.ok) {
    const payload = await readErrorPayload(response);
    throw new ApiRequestError(
      response.status,
      errorMessageFromPayload(payload),
      payload,
    );
  }
}

async function readErrorPayload(response: Response): Promise<unknown> {
  const text = await response.text();
  if (text.length === 0) {
    return null;
  }

  try {
    return JSON.parse(text) as unknown;
  } catch {
    return text;
  }
}

function errorMessageFromPayload(payload: unknown): string {
  if (payload && typeof payload === "object") {
    const shape = payload as Record<string, unknown>;
    const messageValue = shape.message;
    if (typeof messageValue === "string" && messageValue.trim().length > 0) {
      return messageValue;
    }
  }

  if (typeof payload === "string" && payload.trim().length > 0) {
    return payload;
  }

  return "Unexpected API error";
}
