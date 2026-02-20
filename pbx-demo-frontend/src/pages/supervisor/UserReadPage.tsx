import type { CrmUserResponse } from '../../domain/crm';

interface UserReadPageProps {
  users: CrmUserResponse[];
  busy: boolean;
  onCreatePage: () => void;
  onEdit: (user: CrmUserResponse) => void;
  onDelete: (id: number) => Promise<void>;
}

export function UserReadPage({ users, busy, onCreatePage, onEdit, onDelete }: UserReadPageProps) {
  return (
    <section className="supervisor-page-stack">
      <article className="card">
        <div className="analytics-header">
          <div>
            <h3>Users / Read</h3>
            <p className="history-summary">Browse and manage CRM users.</p>
          </div>
          <button className="primary-button" type="button" onClick={onCreatePage}>
            New User
          </button>
        </div>
      </article>

      <article className="card">
        <div className="supervisor-table">
          {users.map((user) => (
            <div key={user.id} className="supervisor-row">
              <div>
                <strong>{user.username}</strong> ({user.role})<br />
                {user.firstName} {user.lastName} | {user.emailAddress}<br />
                Ext: {user.ownedExtension || 'N/A'} | Active: {user.isActive ? 'Yes' : 'No'}
              </div>
              <div className="row-actions">
                <button className="secondary-button" type="button" disabled={busy} onClick={() => onEdit(user)}>
                  Edit
                </button>
                <button className="danger-button" type="button" disabled={busy} onClick={() => void onDelete(user.id)}>
                  Delete
                </button>
              </div>
            </div>
          ))}
          {users.length === 0 && <p>No users found.</p>}
        </div>
      </article>
    </section>
  );
}
