import type {
  CrmCallAnalyticsResponse,
  CrmCallHistoryResponse,
  CrmCreateDepartmentRequest,
  CrmCreateSharedParkingRequest,
  CrmCreateUserRequest,
  CrmDepartmentResponse,
  CrmSharedParkingResponse,
  CrmUpdateDepartmentRequest,
  CrmUpdateFriendlyNameRequest,
  CrmUpdateUserRequest,
  CrmUserResponse,
  CrmValidateFriendlyNameRequest,
  CrmVersionResponse
} from '../domain/crm';
import { requestJson, requestNoContent } from './httpClient';

export function getCrmUsers(accessToken: string): Promise<CrmUserResponse[]> {
  return requestJson<CrmUserResponse[]>('/api/crm/users', { method: 'GET' }, accessToken);
}

export function createCrmUser(accessToken: string, request: CrmCreateUserRequest): Promise<CrmUserResponse> {
  return requestJson<CrmUserResponse>(
    '/api/crm/users',
    {
      method: 'POST',
      body: JSON.stringify(request)
    },
    accessToken
  );
}

export function updateCrmUser(accessToken: string, id: number, request: CrmUpdateUserRequest): Promise<CrmUserResponse> {
  return requestJson<CrmUserResponse>(
    `/api/crm/users/${id}`,
    {
      method: 'PUT',
      body: JSON.stringify(request)
    },
    accessToken
  );
}

export function deleteCrmUser(accessToken: string, id: number): Promise<void> {
  return requestNoContent(
    `/api/crm/users/${id}`,
    {
      method: 'DELETE'
    },
    accessToken
  );
}

export function validateFriendlyName(accessToken: string, request: CrmValidateFriendlyNameRequest): Promise<void> {
  return requestNoContent(
    '/api/crm/users/validate-friendly-name',
    {
      method: 'POST',
      body: JSON.stringify(request)
    },
    accessToken
  );
}

export function updateFriendlyName(accessToken: string, id: number, request: CrmUpdateFriendlyNameRequest): Promise<void> {
  return requestNoContent(
    `/api/crm/users/${id}/friendly-name`,
    {
      method: 'PUT',
      body: JSON.stringify(request)
    },
    accessToken
  );
}

export function getCrmDepartments(accessToken: string): Promise<CrmDepartmentResponse[]> {
  return requestJson<CrmDepartmentResponse[]>('/api/crm/departments', { method: 'GET' }, accessToken);
}

export function createCrmDepartment(
  accessToken: string,
  request: CrmCreateDepartmentRequest
): Promise<CrmDepartmentResponse> {
  return requestJson<CrmDepartmentResponse>(
    '/api/crm/departments',
    {
      method: 'POST',
      body: JSON.stringify(request)
    },
    accessToken
  );
}

export function updateCrmDepartment(
  accessToken: string,
  id: number,
  request: CrmUpdateDepartmentRequest
): Promise<CrmDepartmentResponse> {
  return requestJson<CrmDepartmentResponse>(
    `/api/crm/departments/${id}`,
    {
      method: 'PUT',
      body: JSON.stringify(request)
    },
    accessToken
  );
}

export function deleteCrmDepartment(accessToken: string, id: number): Promise<void> {
  return requestNoContent(
    `/api/crm/departments/${id}`,
    {
      method: 'DELETE'
    },
    accessToken
  );
}

export function getThreeCxVersion(accessToken: string): Promise<CrmVersionResponse> {
  return requestJson<CrmVersionResponse>('/api/crm/system/version', { method: 'GET' }, accessToken);
}

export function getDefaultGroup(accessToken: string): Promise<unknown> {
  return requestJson<unknown>('/api/crm/system/groups/default', { method: 'GET' }, accessToken);
}

export function getGroupMembers(accessToken: string, groupId: number): Promise<unknown> {
  return requestJson<unknown>(`/api/crm/system/groups/${groupId}/members`, { method: 'GET' }, accessToken);
}

export function getThreeCxUsers(accessToken: string): Promise<unknown> {
  return requestJson<unknown>('/api/crm/system/3cx-users', { method: 'GET' }, accessToken);
}

export function createSharedParking(
  accessToken: string,
  request: CrmCreateSharedParkingRequest
): Promise<CrmSharedParkingResponse> {
  return requestJson<CrmSharedParkingResponse>(
    '/api/crm/system/parking',
    {
      method: 'POST',
      body: JSON.stringify(request)
    },
    accessToken
  );
}

export function getParkingByNumber(accessToken: string, number: string): Promise<CrmSharedParkingResponse> {
  return requestJson<CrmSharedParkingResponse>(
    `/api/crm/system/parking/${encodeURIComponent(number)}`,
    { method: 'GET' },
    accessToken
  );
}

export function deleteSharedParking(accessToken: string, parkingId: number): Promise<void> {
  return requestNoContent(
    `/api/crm/system/parking/${parkingId}`,
    { method: 'DELETE' },
    accessToken
  );
}

export function getCallHistory(
  accessToken: string,
  options: { take?: number; skip?: number; operatorUserId?: number } = {}
): Promise<CrmCallHistoryResponse> {
  const params = new URLSearchParams();
  if (typeof options.take === 'number') {
    params.set('take', String(options.take));
  }

  if (typeof options.skip === 'number') {
    params.set('skip', String(options.skip));
  }

  if (typeof options.operatorUserId === 'number') {
    params.set('operatorUserId', String(options.operatorUserId));
  }

  const queryString = params.toString();
  const url = queryString.length > 0
    ? `/api/crm/calls/history?${queryString}`
    : '/api/crm/calls/history';

  return requestJson<CrmCallHistoryResponse>(url, { method: 'GET' }, accessToken);
}

export function getCallAnalytics(accessToken: string, days = 7): Promise<CrmCallAnalyticsResponse> {
  const normalizedDays = Number.isFinite(days) ? Math.max(1, Math.min(90, Math.round(days))) : 7;
  return requestJson<CrmCallAnalyticsResponse>(
    `/api/crm/calls/analytics?days=${normalizedDays}`,
    { method: 'GET' },
    accessToken
  );
}
