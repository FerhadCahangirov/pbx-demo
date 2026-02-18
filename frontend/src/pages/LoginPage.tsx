import { FormEvent, useState } from 'react';
import type { LoginRequest } from '../domain/softphone';

interface LoginPageProps {
  loading: boolean;
  onLogin: (request: LoginRequest) => Promise<void>;
}

export function LoginPage({ loading, onLogin }: LoginPageProps) {
  const [form, setForm] = useState<LoginRequest>({
    username: '',
    password: '',
    pbxBase: '',
    appId: '',
    appSecret: ''
  });

  const onSubmit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    await onLogin(form);
  };

  return (
    <section className="card">
      <h2>Login</h2>
      <p>Authenticate with backend/app credentials to join native browser WebRTC signaling.</p>

      <form className="form-grid" onSubmit={onSubmit}>
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

        <div className="grid-two">
          <div>
            <label className="label" htmlFor="pbxBase">
              PBX Base URL
            </label>
            <input
              id="pbxBase"
              className="input"
              value={form.pbxBase}
              onChange={(event) => setForm((prev) => ({ ...prev, pbxBase: event.target.value }))}
              placeholder="https://pbx.example.com"
              required
            />
          </div>
          <div>
            <label className="label" htmlFor="appId">
              App ID
            </label>
            <input
              id="appId"
              className="input"
              value={form.appId}
              onChange={(event) => setForm((prev) => ({ ...prev, appId: event.target.value }))}
              required
            />
          </div>
        </div>

        <div>
          <label className="label" htmlFor="appSecret">
            App Secret
          </label>
          <input
            id="appSecret"
            className="input"
            value={form.appSecret}
            onChange={(event) => setForm((prev) => ({ ...prev, appSecret: event.target.value }))}
            required
          />
        </div>

        <button className="primary-button" type="submit" disabled={loading}>
          {loading ? 'Connecting...' : 'Connect to PBX'}
        </button>
      </form>
    </section>
  );
}
