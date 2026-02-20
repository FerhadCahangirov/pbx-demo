import type {
  AppUserRole,
  CrmCallAnalyticsResponse,
  CrmCallHistoryItemResponse,
  CrmDepartmentResponse,
  CrmUserResponse
} from '../../domain/crm';

export interface UserFormState {
  username: string;
  password: string;
  newPassword: string;
  firstName: string;
  lastName: string;
  emailAddress: string;
  ownedExtension: string;
  controlDn: string;
  role: AppUserRole;
  departmentId: string;
  departmentRoleName: string;
  clickToCallId: string;
  webMeetingFriendlyName: string;
  callUsEnableChat: boolean;
  isActive: boolean;
}

export interface DepartmentFormState {
  name: string;
  language: string;
  timeZoneId: string;
  liveChatLink: string;
  liveChatWebsite: string;
  routeTo: string;
  routeNumber: string;
}

export interface CdrMetaState {
  totalCount: number;
  take: number;
  skip: number;
}

export type SupervisorSection =
  | 'dashboard'
  | 'cdr'
  | 'users-read'
  | 'users-create'
  | 'users-update'
  | 'departments-read'
  | 'departments-create'
  | 'departments-update'
  | 'parking';

export const ROLE_OPTIONS = [
  'system_owners',
  'system_admins',
  'group_owners',
  'managers',
  'group_admins',
  'receptionists',
  'users'
];

export const DEFAULT_DEPARTMENT_PROPS = {
  liveChatMaxCount: 20,
  personalContactsMaxCount: 500,
  promptsMaxCount: 10,
  sbcMaxCount: 20,
  systemNumberFrom: '',
  systemNumberTo: '',
  trunkNumberFrom: '',
  trunkNumberTo: '',
  userNumberFrom: '',
  userNumberTo: ''
};

export function createInitialUserForm(): UserFormState {
  return {
    username: '',
    password: '',
    newPassword: '',
    firstName: '',
    lastName: '',
    emailAddress: '',
    ownedExtension: '',
    controlDn: '',
    role: 'User',
    departmentId: '',
    departmentRoleName: 'users',
    clickToCallId: '',
    webMeetingFriendlyName: '',
    callUsEnableChat: true,
    isActive: true
  };
}

export function createInitialDepartmentForm(): DepartmentFormState {
  return {
    name: '',
    language: 'EN',
    timeZoneId: '51',
    liveChatLink: '',
    liveChatWebsite: '',
    routeTo: 'VoiceMail',
    routeNumber: ''
  };
}

export function mapUserToForm(user: CrmUserResponse): UserFormState {
  return {
    username: user.username,
    password: '',
    newPassword: '',
    firstName: user.firstName,
    lastName: user.lastName,
    emailAddress: user.emailAddress,
    ownedExtension: user.ownedExtension,
    controlDn: user.controlDn ?? '',
    role: user.role,
    departmentId: user.departmentRoles[0]?.appDepartmentId?.toString() ?? '',
    departmentRoleName: user.departmentRoles[0]?.roleName ?? 'users',
    clickToCallId: user.clickToCallId ?? '',
    webMeetingFriendlyName: user.webMeetingFriendlyName ?? '',
    callUsEnableChat: user.callUsEnableChat,
    isActive: user.isActive
  };
}

export function mapDepartmentToForm(department: CrmDepartmentResponse): DepartmentFormState {
  return {
    name: department.name,
    language: department.language,
    timeZoneId: department.timeZoneId,
    liveChatLink: department.liveChatLink ?? '',
    liveChatWebsite: department.liveChatWebsite ?? '',
    routeTo: department.routing?.officeRoute?.route?.to ?? 'VoiceMail',
    routeNumber: department.routing?.officeRoute?.route?.number ?? ''
  };
}

export function formatUtc(value: string | null | undefined): string {
  if (!value) {
    return '-';
  }

  const parsed = new Date(value);
  if (Number.isNaN(parsed.getTime())) {
    return value;
  }

  return parsed.toLocaleString();
}

export function formatDurationSeconds(value: number | null | undefined): string {
  if (typeof value !== 'number' || !Number.isFinite(value) || value < 0) {
    return '-';
  }

  const seconds = Math.floor(value);
  const hours = Math.floor(seconds / 3600);
  const minutes = Math.floor((seconds % 3600) / 60);
  const remainder = seconds % 60;

  if (hours > 0) {
    return `${hours}h ${minutes}m ${remainder}s`;
  }

  if (minutes > 0) {
    return `${minutes}m ${remainder}s`;
  }

  return `${remainder}s`;
}

export function computeQueueLoad(callAnalytics: CrmCallAnalyticsResponse | null): { label: string; tone: string } {
  if (!callAnalytics || callAnalytics.totalOperators <= 0) {
    return {
      label: 'N/A',
      tone: 'status-info'
    };
  }

  const loadRatio = callAnalytics.activeCalls / callAnalytics.totalOperators;
  if (loadRatio >= 1) {
    return {
      label: 'High',
      tone: 'status-critical'
    };
  }

  if (loadRatio >= 0.6) {
    return {
      label: 'Medium',
      tone: 'status-waiting'
    };
  }

  return {
    label: 'Low',
    tone: 'status-active'
  };
}

export function buildStatusChartData(calls: CrmCallHistoryItemResponse[]): Array<{ label: string; value: number }> {
  const counts = new Map<string, number>();
  for (const call of calls) {
    const key = (call.status || 'Unknown').trim() || 'Unknown';
    counts.set(key, (counts.get(key) ?? 0) + 1);
  }

  return [...counts.entries()]
    .map(([label, value]) => ({ label, value }))
    .sort((left, right) => right.value - left.value);
}
