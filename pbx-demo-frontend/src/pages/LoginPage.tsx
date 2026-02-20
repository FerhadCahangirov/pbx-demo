import { FormEvent, useState } from 'react';
import type { LoginRequest } from '../domain/softphone';

interface LoginPageProps {
  loading: boolean;
  onLogin: (request: LoginRequest) => Promise<void>;
}

export function LoginPage({ loading, onLogin }: LoginPageProps) {
  const [form, setForm] = useState<LoginRequest>({
    username: '',
    password: ''
  });

  const onSubmit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    await onLogin(form);
  };

  return (
    <section className="card mx-auto w-full max-w-2xl">
      <p className="inline-flex rounded-full border border-primary-300 bg-primary-100 px-3 py-1 text-xs font-semibold uppercase tracking-wider text-primary-700">
        Secure Access
      </p>
      <h2 className="mt-3 font-display text-2xl font-semibold text-ink">Welcome back</h2>
      <p className="mt-2 text-sm text-muted">
        Sign in with your CRM account. The workspace auto-configures by role and extension access.
      </p>

      <form className="form-grid mt-6" onSubmit={onSubmit}>
        <div>
          <label className="label" htmlFor="username">
            Username
          </label>
          <input
            id="username"
            className="input"
            value={form.username}
            onChange={(event) => setForm((prev) => ({ ...prev, username: event.target.value }))}
            autoComplete="username"
            required
          />
        </div>

        <div>
          <label className="label" htmlFor="password">
            Password
          </label>
          <input
            id="password"
            className="input"
            type="password"
            value={form.password}
            onChange={(event) => setForm((prev) => ({ ...prev, password: event.target.value }))}
            autoComplete="current-password"
            required
          />
        </div>

        <button className="primary-button w-fit min-w-36" type="submit" disabled={loading}>
          {loading ? 'Connecting...' : 'Login'}
        </button>
      </form>

      <div className="mt-5 flex flex-wrap gap-2">
        <span className="status-chip status-info">Role-based access</span>
        <span className="status-chip status-waiting">Session warnings enabled</span>
      </div>

      <div className="mt-5 grid gap-3 md:grid-cols-2">
        <article className="rounded-2xl border border-border bg-white/80 p-4">
          <strong className="text-sm font-semibold text-ink">Smart module routing</strong>
          <p className="mt-1 text-sm text-muted">
            Operator and supervisor panels open automatically after authentication.
          </p>
        </article>
        <article className="rounded-2xl border border-border bg-white/80 p-4">
          <strong className="text-sm font-semibold text-ink">Live call context</strong>
          <p className="mt-1 text-sm text-muted">Voice controls, CRM edits, and request capture stay in one flow.</p>
        </article>
      </div>
    </section>
  );
}
