export type AppUserRole = 'User' | 'Supervisor';

export interface CrmUserDepartmentRoleRequest {
  appDepartmentId: number;
  roleName: string;
}

export interface CrmCreateUserRequest {
  username: string;
  password: string;
  firstName: string;
  lastName: string;
  emailAddress: string;
  ownedExtension: string;
  controlDn?: string | null;
  role: AppUserRole;
  language: string;
  promptSet?: string | null;
  vmEmailOptions: string;
  sendEmailMissedCalls: boolean;
  require2Fa: boolean;
  callUsEnableChat: boolean;
  clickToCallId?: string | null;
  webMeetingFriendlyName?: string | null;
  sipUsername?: string | null;
  sipAuthId?: string | null;
  sipPassword?: string | null;
  sipDisplayName?: string | null;
  threeCxAccessPassword?: string | null;
  departmentRoles: CrmUserDepartmentRoleRequest[];
}

export interface CrmUpdateUserRequest {
  firstName: string;
  lastName: string;
  emailAddress: string;
  ownedExtension: string;
  controlDn?: string | null;
  role: AppUserRole;
  language: string;
  promptSet?: string | null;
  vmEmailOptions: string;
  sendEmailMissedCalls: boolean;
  require2Fa: boolean;
  callUsEnableChat: boolean;
  clickToCallId?: string | null;
  webMeetingFriendlyName?: string | null;
  sipUsername?: string | null;
  sipAuthId?: string | null;
  sipPassword?: string | null;
  sipDisplayName?: string | null;
  isActive: boolean;
  newPassword?: string | null;
  departmentRoles: CrmUserDepartmentRoleRequest[];
}

export interface CrmDepartmentRoleResponse {
  appDepartmentId: number;
  threeCxGroupId: number;
  departmentName: string;
  roleName: string;
}

export interface CrmUserResponse {
  id: number;
  username: string;
  firstName: string;
  lastName: string;
  emailAddress: string;
  ownedExtension: string;
  controlDn?: string | null;
  role: AppUserRole;
  language: string;
  promptSet?: string | null;
  vmEmailOptions: string;
  sendEmailMissedCalls: boolean;
  require2Fa: boolean;
  callUsEnableChat: boolean;
  clickToCallId?: string | null;
  webMeetingFriendlyName?: string | null;
  threeCxUserId?: number | null;
  isActive: boolean;
  createdAtUtc: string;
  updatedAtUtc: string;
  departmentRoles: CrmDepartmentRoleResponse[];
}

export interface CrmDepartmentPropsDto {
  liveChatMaxCount: number;
  personalContactsMaxCount: number;
  promptsMaxCount: number;
  sbcMaxCount: number;
  systemNumberFrom?: string | null;
  systemNumberTo?: string | null;
  trunkNumberFrom?: string | null;
  trunkNumberTo?: string | null;
  userNumberFrom?: string | null;
  userNumberTo?: string | null;
}

export interface CrmDepartmentRouteTargetDto {
  to: string;
  number: string;
  external: string;
}

export interface CrmDepartmentRouteDto {
  isPromptEnabled: boolean;
  route: CrmDepartmentRouteTargetDto;
}

export interface CrmDepartmentRoutingDto {
  officeRoute: CrmDepartmentRouteDto;
  outOfOfficeRoute: CrmDepartmentRouteDto;
  breakRoute: CrmDepartmentRouteDto;
  holidaysRoute: CrmDepartmentRouteDto;
}

export interface CrmCreateDepartmentRequest {
  name: string;
  language: string;
  timeZoneId: string;
  promptSet?: string | null;
  disableCustomPrompt: boolean;
  allowCallService: boolean;
  props: CrmDepartmentPropsDto;
  liveChatLink?: string | null;
  liveChatWebsite?: string | null;
  routing?: CrmDepartmentRoutingDto | null;
}

export interface CrmUpdateDepartmentRequest extends CrmCreateDepartmentRequest {}

export interface CrmDepartmentResponse {
  id: number;
  name: string;
  threeCxGroupId: number;
  threeCxGroupNumber?: string | null;
  language: string;
  timeZoneId: string;
  promptSet?: string | null;
  disableCustomPrompt: boolean;
  props: CrmDepartmentPropsDto;
  routing?: CrmDepartmentRoutingDto | null;
  liveChatLink?: string | null;
  liveChatWebsite?: string | null;
  threeCxWebsiteLinkId?: number | null;
  createdAtUtc: string;
  updatedAtUtc: string;
}

export interface CrmValidateFriendlyNameRequest {
  friendlyName: string;
  pair: string;
}

export interface CrmUpdateFriendlyNameRequest {
  callUsEnableChat: boolean;
  clickToCallId: string;
  webMeetingFriendlyName: string;
}

export interface CrmCreateSharedParkingRequest {
  groupIds: number[];
}

export interface CrmSharedParkingResponse {
  id: number;
  number: string;
}

export interface CrmVersionResponse {
  version: string;
}

export interface CrmCallStatusHistoryItemResponse {
  status: string;
  eventType: string;
  eventReason?: string | null;
  occurredAtUtc: string;
}

export interface CrmCallHistoryItemResponse {
  id: number;
  source: string;
  operatorUserId: number;
  operatorUsername: string;
  operatorDisplayName: string;
  operatorExtension: string;
  trackingKey: string;
  callScopeId?: string | null;
  participantId?: number | null;
  pbxCallId?: number | null;
  pbxLegId?: number | null;
  direction: string;
  status: string;
  remoteParty?: string | null;
  remoteName?: string | null;
  endReason?: string | null;
  startedAtUtc: string;
  answeredAtUtc?: string | null;
  endedAtUtc?: string | null;
  isActive: boolean;
  talkDurationSeconds?: number | null;
  totalDurationSeconds?: number | null;
  statusHistory: CrmCallStatusHistoryItemResponse[];
}

export interface CrmCallHistoryResponse {
  totalCount: number;
  take: number;
  skip: number;
  items: CrmCallHistoryItemResponse[];
}

export interface CrmOperatorCallKpiResponse {
  operatorUserId: number;
  operatorUsername: string;
  operatorDisplayName: string;
  operatorExtension: string;
  totalCalls: number;
  activeCalls: number;
  answeredCalls: number;
  missedCalls: number;
  failedCalls: number;
  totalTalkSeconds: number;
  averageTalkSeconds: number;
  lastCallAtUtc?: string | null;
}

export interface CrmCallAnalyticsResponse {
  periodStartUtc: string;
  periodEndUtc: string;
  generatedAtUtc: string;
  totalCalls: number;
  activeCalls: number;
  answeredCalls: number;
  missedCalls: number;
  failedCalls: number;
  totalTalkSeconds: number;
  averageTalkSeconds: number;
  totalOperators: number;
  activeOperators: number;
  operatorKpis: CrmOperatorCallKpiResponse[];
}
