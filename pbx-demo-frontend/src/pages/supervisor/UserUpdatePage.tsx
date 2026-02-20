import type { CrmDepartmentResponse } from '../../domain/crm';
import type { UserFormState } from './shared';
import { ROLE_OPTIONS } from './shared';

interface UserUpdatePageProps {
  selectedUserId: number | null;
  form: UserFormState;
  departments: CrmDepartmentResponse[];
  busy: boolean;
  onFormChange: (next: UserFormState) => void;
  onSubmit: () => Promise<void>;
  onReset: () => void;
  onGoRead: () => void;
}

export function UserUpdatePage({
  selectedUserId,
  form,
  departments,
  busy,
  onFormChange,
  onSubmit,
  onReset,
  onGoRead
}: UserUpdatePageProps) {
  if (selectedUserId === null) {
    return (
      <section className="supervisor-page-stack">
        <article className="card">
          <h3>Users / Update</h3>
          <p className="history-summary">Select a user from the read page first.</p>
          <button className="secondary-button" type="button" onClick={onGoRead}>
            Go to Users / Read
          </button>
        </article>
      </section>
    );
  }

  return (
    <section className="supervisor-page-stack">
      <article className="card">
        <h3>Users / Update</h3>
        <p className="history-summary">Editing user #{selectedUserId}</p>
      </article>

      <article className="card">
        <div className="form-grid">
          <div className="grid-two">
            <input className="input" placeholder="Username" value={form.username} disabled />
            <input
              className="input"
              placeholder="New Password (optional)"
              type="password"
              value={form.newPassword}
              onChange={(event) => onFormChange({ ...form, newPassword: event.target.value })}
            />
          </div>
          <div className="grid-two">
            <input
              className="input"
              placeholder="First Name"
              value={form.firstName}
              onChange={(event) => onFormChange({ ...form, firstName: event.target.value })}
              required
            />
            <input
              className="input"
              placeholder="Last Name"
              value={form.lastName}
              onChange={(event) => onFormChange({ ...form, lastName: event.target.value })}
              required
            />
          </div>
          <div className="grid-two">
            <input
              className="input"
              placeholder="Email"
              type="email"
              value={form.emailAddress}
              onChange={(event) => onFormChange({ ...form, emailAddress: event.target.value })}
              required
            />
            <input
              className="input"
              placeholder="Owned Extension"
              value={form.ownedExtension}
              onChange={(event) => onFormChange({ ...form, ownedExtension: event.target.value })}
            />
          </div>
          <div className="grid-two">
            <select
              className="select"
              value={form.role}
              onChange={(event) => onFormChange({ ...form, role: event.target.value as UserFormState['role'] })}
            >
              <option value="User">User</option>
              <option value="Supervisor">Supervisor</option>
            </select>
            <input
              className="input"
              placeholder="Control DN (optional)"
              value={form.controlDn}
              onChange={(event) => onFormChange({ ...form, controlDn: event.target.value })}
            />
          </div>
          <div className="grid-two">
            <select
              className="select"
              value={form.departmentId}
              onChange={(event) => onFormChange({ ...form, departmentId: event.target.value })}
            >
              <option value="">No Department Role</option>
              {departments.map((department) => (
                <option key={department.id} value={department.id}>
                  {department.name} (3CX {department.threeCxGroupId})
                </option>
              ))}
            </select>
            <select
              className="select"
              value={form.departmentRoleName}
              onChange={(event) => onFormChange({ ...form, departmentRoleName: event.target.value })}
            >
              {ROLE_OPTIONS.map((role) => (
                <option key={role} value={role}>
                  {role}
                </option>
              ))}
            </select>
          </div>
          <div className="grid-two">
            <input
              className="input"
              placeholder="ClickToCallId (optional)"
              value={form.clickToCallId}
              onChange={(event) => onFormChange({ ...form, clickToCallId: event.target.value })}
            />
            <input
              className="input"
              placeholder="WebMeetingFriendlyName (optional)"
              value={form.webMeetingFriendlyName}
              onChange={(event) => onFormChange({ ...form, webMeetingFriendlyName: event.target.value })}
            />
          </div>
          <div className="grid-two">
            <label className="label switch-line">
              <input
                type="checkbox"
                checked={form.callUsEnableChat}
                onChange={(event) => onFormChange({ ...form, callUsEnableChat: event.target.checked })}
              />
              CallUs Chat Enabled
            </label>
            <label className="label switch-line">
              <input
                type="checkbox"
                checked={form.isActive}
                onChange={(event) => onFormChange({ ...form, isActive: event.target.checked })}
              />
              User Active
            </label>
          </div>
          <div className="grid-two">
            <button className="primary-button" type="button" disabled={busy} onClick={() => void onSubmit()}>
              Update User
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
