import type { StoredAuthSession } from '../domain/softphone';

const STORAGE_KEY = 'pbx-softphone-auth-session';

export function loadAuthSession(): StoredAuthSession | null {
  const raw = localStorage.getItem(STORAGE_KEY);
  if (!raw) {
    return null;
  }

  try {
    const parsed = JSON.parse(raw) as StoredAuthSession;
    if (!parsed.accessToken || !parsed.expiresAtUtc || !parsed.sessionId || !parsed.username) {
      clearAuthSession();
      return null;
    }

    if (!parsed.pbxBase || parsed.pbxBase.trim().length === 0) {
      clearAuthSession();
      return null;
    }

    if (isAuthSessionExpired(parsed)) {
      clearAuthSession();
      return null;
    }

    return parsed;
  } catch {
    clearAuthSession();
    return null;
  }
}

export function saveAuthSession(session: StoredAuthSession): void {
  localStorage.setItem(STORAGE_KEY, JSON.stringify(session));
}

export function clearAuthSession(): void {
  localStorage.removeItem(STORAGE_KEY);
}

export function isAuthSessionExpired(session: StoredAuthSession): boolean {
  const expiresAtUtcMs = Date.parse(session.expiresAtUtc);
  if (Number.isNaN(expiresAtUtcMs)) {
    return true;
  }

  return expiresAtUtcMs <= Date.now() + 30_000;
}
