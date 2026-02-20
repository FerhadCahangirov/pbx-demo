import type { SupervisorSection } from './shared';

interface SupervisorNavProps {
  active: SupervisorSection;
  onChange: (section: SupervisorSection) => void;
}

interface NavItem {
  section: SupervisorSection;
  label: string;
}

const DASHBOARD_ITEMS: NavItem[] = [
  { section: 'dashboard', label: 'Dashboard' },
  { section: 'cdr', label: 'CDR' }
];

const USER_ITEMS: NavItem[] = [
  { section: 'users-read', label: 'Users / Read' },
  { section: 'users-create', label: 'Users / Create' },
  { section: 'users-update', label: 'Users / Update' }
];

const DEPARTMENT_ITEMS: NavItem[] = [
  { section: 'departments-read', label: 'Departments / Read' },
  { section: 'departments-create', label: 'Departments / Create' },
  { section: 'departments-update', label: 'Departments / Update' }
];

const SYSTEM_ITEMS: NavItem[] = [{ section: 'parking', label: 'Shared Parking' }];

function NavGroup({
  title,
  items,
  active,
  onChange
}: {
  title: string;
  items: NavItem[];
  active: SupervisorSection;
  onChange: (section: SupervisorSection) => void;
}) {
  return (
    <div className="supervisor-nav-group">
      <p className="supervisor-nav-title">{title}</p>
      <div className="supervisor-nav-list">
        {items.map((item) => (
          <button
            key={item.section}
            type="button"
            className={`supervisor-link ${active === item.section ? 'supervisor-link-active' : ''}`}
            onClick={() => onChange(item.section)}
          >
            {item.label}
          </button>
        ))}
      </div>
    </div>
  );
}

export function SupervisorNav({ active, onChange }: SupervisorNavProps) {
  return (
    <aside className="supervisor-nav card">
      <h3>Business Pages</h3>
      <NavGroup title="Insights" items={DASHBOARD_ITEMS} active={active} onChange={onChange} />
      <NavGroup title="User CRUD" items={USER_ITEMS} active={active} onChange={onChange} />
      <NavGroup title="Department CRUD" items={DEPARTMENT_ITEMS} active={active} onChange={onChange} />
      <NavGroup title="System" items={SYSTEM_ITEMS} active={active} onChange={onChange} />
    </aside>
  );
}
