import type { DepartmentFormState } from './shared';

interface DepartmentCreatePageProps {
  form: DepartmentFormState;
  busy: boolean;
  onFormChange: (next: DepartmentFormState) => void;
  onSubmit: () => Promise<void>;
  onReset: () => void;
}

export function DepartmentCreatePage({
  form,
  busy,
  onFormChange,
  onSubmit,
  onReset
}: DepartmentCreatePageProps) {
  return (
    <section className="supervisor-page-stack">
      <article className="card">
        <h3>Departments / Create</h3>
        <p className="history-summary">Create a new CRM department and sync it to 3CX.</p>
      </article>

      <article className="card">
        <div className="form-grid">
          <div className="grid-two">
            <input
              className="input"
              placeholder="Department Name"
              value={form.name}
              onChange={(event) => onFormChange({ ...form, name: event.target.value })}
              required
            />
            <input
              className="input"
              placeholder="Language (EN)"
              value={form.language}
              onChange={(event) => onFormChange({ ...form, language: event.target.value })}
              required
            />
          </div>
          <div className="grid-two">
            <input
              className="input"
              placeholder="Time Zone ID (51)"
              value={form.timeZoneId}
              onChange={(event) => onFormChange({ ...form, timeZoneId: event.target.value })}
              required
            />
            <select
              className="select"
              value={form.routeTo}
              onChange={(event) => onFormChange({ ...form, routeTo: event.target.value })}
            >
              <option value="VoiceMail">VoiceMail</option>
              <option value="Extension">Extension</option>
              <option value="Queue">Queue</option>
            </select>
          </div>
          <div className="grid-two">
            <input
              className="input"
              placeholder="Live Chat Link (optional)"
              value={form.liveChatLink}
              onChange={(event) => onFormChange({ ...form, liveChatLink: event.target.value })}
            />
            <input
              className="input"
              placeholder="Live Chat Website (optional)"
              value={form.liveChatWebsite}
              onChange={(event) => onFormChange({ ...form, liveChatWebsite: event.target.value })}
            />
          </div>
          <input
            className="input"
            placeholder="Routing Number (optional)"
            value={form.routeNumber}
            onChange={(event) => onFormChange({ ...form, routeNumber: event.target.value })}
          />
          <div className="grid-two">
            <button className="primary-button" type="button" disabled={busy} onClick={() => void onSubmit()}>
              Create Department
            </button>
            <button className="secondary-button" type="button" disabled={busy} onClick={onReset}>
              Reset
            </button>
          </div>
        </div>
      </article>
    </section>
  );
}
