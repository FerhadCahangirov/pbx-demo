import type { CrmDepartmentResponse } from '../../domain/crm';

interface DepartmentReadPageProps {
  departments: CrmDepartmentResponse[];
  busy: boolean;
  onCreatePage: () => void;
  onEdit: (department: CrmDepartmentResponse) => void;
  onDelete: (id: number) => Promise<void>;
}

export function DepartmentReadPage({
  departments,
  busy,
  onCreatePage,
  onEdit,
  onDelete
}: DepartmentReadPageProps) {
  return (
    <section className="supervisor-page-stack">
      <article className="card">
        <div className="analytics-header">
          <div>
            <h3>Departments / Read</h3>
            <p className="history-summary">Browse and manage departments and group mappings.</p>
          </div>
          <button className="primary-button" type="button" onClick={onCreatePage}>
            New Department
          </button>
        </div>
      </article>

      <article className="card">
        <div className="supervisor-table">
          {departments.map((department) => (
            <div key={department.id} className="supervisor-row">
              <div>
                <strong>{department.name}</strong> (3CX Group ID {department.threeCxGroupId})<br />
                Language: {department.language} | TZ: {department.timeZoneId}<br />
                Live Chat: {department.liveChatLink || '-'}
              </div>
              <div className="row-actions">
                <button className="secondary-button" type="button" disabled={busy} onClick={() => onEdit(department)}>
                  Edit
                </button>
                <button className="danger-button" type="button" disabled={busy} onClick={() => void onDelete(department.id)}>
                  Delete
                </button>
              </div>
            </div>
          ))}
          {departments.length === 0 && <p>No departments found.</p>}
        </div>
      </article>
    </section>
  );
}
