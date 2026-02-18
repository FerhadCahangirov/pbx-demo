const LOOPBACK_HOSTS = new Set(['localhost', '127.0.0.1', '::1', '[::1]']);

function isLoopbackHost(hostname: string): boolean {
  return LOOPBACK_HOSTS.has(hostname.toLowerCase());
}

function toHttpsUrl(rawUrl: string): string | null {
  try {
    const url = new URL(rawUrl);
    if (url.protocol !== 'http:') {
      return null;
    }

    url.protocol = 'https:';
    return url.toString();
  } catch {
    return null;
  }
}

function microphoneUnsupportedMessage(): string {
  if (typeof window === 'undefined' || typeof navigator === 'undefined') {
    return 'Microphone access is unavailable outside a browser context.';
  }

  const currentUrl = window.location.href;
  const currentOrigin = window.location.origin;
  const isLoopback = isLoopbackHost(window.location.hostname);

  if (!window.isSecureContext && !isLoopback) {
    const httpsUrl = toHttpsUrl(currentUrl);
    if (httpsUrl) {
      return `Microphone access requires HTTPS. Current origin: ${currentOrigin}. Open: ${httpsUrl}`;
    }

    return `Microphone access requires HTTPS. Current origin: ${currentOrigin}.`;
  }

  return 'Microphone access is unavailable. Use a modern browser over HTTPS (or localhost).';
}

export function getMediaDevices(): MediaDevices | null {
  if (typeof navigator === 'undefined') {
    return null;
  }

  return navigator.mediaDevices ?? null;
}

export function requireMediaDevicesWithGetUserMedia(): MediaDevices {
  const mediaDevices = getMediaDevices();
  if (!mediaDevices || typeof mediaDevices.getUserMedia !== 'function') {
    throw new Error(microphoneUnsupportedMessage());
  }

  return mediaDevices;
}
