const API_BASE = import.meta.env.VITE_API_BASE ?? '';
const LOOPBACK_HOSTS = new Set(['localhost', '127.0.0.1', '::1', '[::1]']);

export class ApiRequestError extends Error {
  readonly statusCode: number;
  readonly payload: unknown;

  constructor(statusCode: number, message: string, payload: unknown) {
    super(message);
    this.statusCode = statusCode;
    this.payload = payload;
  }
}

export function getApiBase(): string {
  return resolveApiBase(API_BASE);
}

export function buildApiUrl(path: string): string {
  const normalizedPath = path.startsWith('/') ? path : `/${path}`;
  const base = getApiBase();
  return base.length > 0 ? `${base}${normalizedPath}` : normalizedPath;
}

export async function requestJson<TResponse>(
  path: string,
  options: RequestInit = {},
  accessToken?: string
): Promise<TResponse> {
  const headers = new Headers(options.headers ?? {});
  headers.set('Accept', 'application/json');

  if (options.body !== undefined && !headers.has('Content-Type')) {
    headers.set('Content-Type', 'application/json');
  }

  if (accessToken) {
    headers.set('Authorization', `Bearer ${accessToken}`);
  }

  const response = await fetch(buildApiUrl(path), {
    ...options,
    headers
  });

  const text = await response.text();
  const payload = text.length > 0 ? safeJsonParse(text) : null;

  if (!response.ok) {
    throw new ApiRequestError(response.status, errorMessageFromPayload(payload), payload);
  }

  return payload as TResponse;
}

export async function requestNoContent(path: string, options: RequestInit = {}, accessToken?: string): Promise<void> {
  const headers = new Headers(options.headers ?? {});
  headers.set('Accept', 'application/json');

  if (options.body !== undefined && !headers.has('Content-Type')) {
    headers.set('Content-Type', 'application/json');
  }

  if (accessToken) {
    headers.set('Authorization', `Bearer ${accessToken}`);
  }

  const response = await fetch(buildApiUrl(path), {
    ...options,
    headers
  });

  if (response.status === 204) {
    return;
  }

  const text = await response.text();
  const payload = text.length > 0 ? safeJsonParse(text) : null;

  if (!response.ok) {
    throw new ApiRequestError(response.status, errorMessageFromPayload(payload), payload);
  }
}

function safeJsonParse(raw: string): unknown {
  try {
    return JSON.parse(raw);
  } catch {
    return raw;
  }
}

function errorMessageFromPayload(payload: unknown): string {
  if (payload && typeof payload === 'object') {
    const shape = payload as Record<string, unknown>;
    const messageValue = shape.message;
    if (typeof messageValue === 'string' && messageValue.trim().length > 0) {
      return messageValue;
    }
  }

  return 'Unexpected API error';
}

function resolveApiBase(rawBase: string): string {
  const trimmed = rawBase.trim();
  if (trimmed.length === 0 || typeof window === 'undefined') {
    return trimmed.replace(/\/+$/, '');
  }

  let baseUrl: URL;
  try {
    baseUrl = new URL(trimmed, window.location.origin);
  } catch {
    return trimmed.replace(/\/+$/, '');
  }

  const browserHost = window.location.hostname;
  const apiHost = baseUrl.hostname;

  if (LOOPBACK_HOSTS.has(apiHost) && !LOOPBACK_HOSTS.has(browserHost)) {
    baseUrl.hostname = browserHost;
  }

  const normalizedPath = baseUrl.pathname === '/' ? '' : baseUrl.pathname.replace(/\/+$/, '');
  return `${baseUrl.origin}${normalizedPath}`;
}
