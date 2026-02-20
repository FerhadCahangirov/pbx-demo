import { useEffect, useMemo, useState } from 'react';
import type {
  CrmCallAnalyticsResponse,
  CrmCallHistoryItemResponse,
  CrmCreateDepartmentRequest,
  CrmCreateUserRequest,
  CrmDepartmentResponse,
  CrmUpdateDepartmentRequest,
  CrmUpdateUserRequest,
  CrmUserResponse
} from '../domain/crm';
import {
  createCrmDepartment,
  createCrmUser,
  createSharedParking,
  deleteCrmDepartment,
  deleteCrmUser,
  deleteSharedParking,
  getCallAnalytics,
  getCallHistory,
  getCrmDepartments,
  getCrmUsers,
  getParkingByNumber,
  getThreeCxVersion,
  updateCrmDepartment,
  updateCrmUser
} from '../services/crmApi';
import { CdrPage } from './supervisor/CdrPage';
import { DashboardPage } from './supervisor/DashboardPage';
import { DepartmentCreatePage } from './supervisor/DepartmentCreatePage';
import { DepartmentReadPage } from './supervisor/DepartmentReadPage';
import { DepartmentUpdatePage } from './supervisor/DepartmentUpdatePage';
import { ParkingToolsPage } from './supervisor/ParkingToolsPage';
import { SupervisorNav } from './supervisor/SupervisorNav';
import { UserCreatePage } from './supervisor/UserCreatePage';
import { UserReadPage } from './supervisor/UserReadPage';
import { UserUpdatePage } from './supervisor/UserUpdatePage';
import {
  CdrMetaState,
  DEFAULT_DEPARTMENT_PROPS,
  DepartmentFormState,
  SupervisorSection,
  UserFormState,
  buildStatusChartData,
  computeQueueLoad,
  createInitialDepartmentForm,
  createInitialUserForm,
  mapDepartmentToForm,
  mapUserToForm
} from './supervisor/shared';

interface SupervisorPageProps {
  accessToken: string;
}

const INITIAL_CDR_META: CdrMetaState = {
  totalCount: 0,
  take: 25,
  skip: 0
};

function toMessage(error: unknown, fallback: string): string {
  if (error instanceof Error && error.message.trim().length > 0) {
    return error.message;
  }

  return fallback;
}

export function SupervisorPage({ accessToken }: SupervisorPageProps) {
  const [section, setSection] = useState<SupervisorSection>('dashboard');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [version, setVersion] = useState('Unknown');
  const [users, setUsers] = useState<CrmUserResponse[]>([]);
  const [departments, setDepartments] = useState<CrmDepartmentResponse[]>([]);
  const [callHistory, setCallHistory] = useState<CrmCallHistoryItemResponse[]>([]);
  const [callHistoryMeta, setCallHistoryMeta] = useState<CdrMetaState>(INITIAL_CDR_META);
  const [callAnalytics, setCallAnalytics] = useState<CrmCallAnalyticsResponse | null>(null);
  const [analyticsDays, setAnalyticsDays] = useState(7);

  const [createUserForm, setCreateUserForm] = useState<UserFormState>(createInitialUserForm);
  const [selectedUserId, setSelectedUserId] = useState<number | null>(null);
  const [updateUserForm, setUpdateUserForm] = useState<UserFormState>(createInitialUserForm);

  const [createDepartmentForm, setCreateDepartmentForm] = useState<DepartmentFormState>(createInitialDepartmentForm);
  const [selectedDepartmentId, setSelectedDepartmentId] = useState<number | null>(null);
  const [updateDepartmentForm, setUpdateDepartmentForm] = useState<DepartmentFormState>(createInitialDepartmentForm);

  const [parkingGroupIds, setParkingGroupIds] = useState('');
  const [parkingNumber, setParkingNumber] = useState('');
  const [parkingInfo, setParkingInfo] = useState('');

  const queueLoad = useMemo(() => computeQueueLoad(callAnalytics), [callAnalytics]);
  const statusChartData = useMemo(() => buildStatusChartData(callHistory), [callHistory]);

  useEffect(() => {
    if (!notice) {
      return;
    }

    const timer = window.setTimeout(() => setNotice(null), 3000);
    return () => window.clearTimeout(timer);
  }, [notice]);

  const refreshDirectory = async (): Promise<void> => {
    const [loadedUsers, loadedDepartments, loadedVersion] = await Promise.all([
      getCrmUsers(accessToken),
      getCrmDepartments(accessToken),
      getThreeCxVersion(accessToken)
    ]);

    setUsers(loadedUsers);
    setDepartments(loadedDepartments);
    setVersion(loadedVersion.version || 'Unknown');
  };

  const refreshCdr = async (take = callHistoryMeta.take, skip = callHistoryMeta.skip): Promise<void> => {
    const loadedHistory = await getCallHistory(accessToken, { take, skip });
    setCallHistory(loadedHistory.items);
    setCallHistoryMeta({
      totalCount: loadedHistory.totalCount,
      take: loadedHistory.take,
      skip: loadedHistory.skip
    });
  };

  const refreshAnalytics = async (days = analyticsDays): Promise<void> => {
    const loadedAnalytics = await getCallAnalytics(accessToken, days);
    setCallAnalytics(loadedAnalytics);
  };

  const refreshAll = async (days = analyticsDays, take = callHistoryMeta.take, skip = 0): Promise<void> => {
    const [loadedUsers, loadedDepartments, loadedVersion, loadedHistory, loadedAnalytics] = await Promise.all([
      getCrmUsers(accessToken),
      getCrmDepartments(accessToken),
      getThreeCxVersion(accessToken),
      getCallHistory(accessToken, { take, skip }),
      getCallAnalytics(accessToken, days)
    ]);

    setUsers(loadedUsers);
    setDepartments(loadedDepartments);
    setVersion(loadedVersion.version || 'Unknown');
    setCallHistory(loadedHistory.items);
    setCallHistoryMeta({
      totalCount: loadedHistory.totalCount,
      take: loadedHistory.take,
      skip: loadedHistory.skip
    });
    setCallAnalytics(loadedAnalytics);
  };

  useEffect(() => {
    let mounted = true;

    const load = async () => {
      setBusy(true);
      setError(null);
      try {
        await refreshAll(analyticsDays, INITIAL_CDR_META.take, INITIAL_CDR_META.skip);
      } catch (loadError) {
        if (mounted) {
          setError(toMessage(loadError, 'Failed to load supervisor pages.'));
        }
      } finally {
        if (mounted) {
          setBusy(false);
        }
      }
    };

    void load();
    return () => {
      mounted = false;
    };
  }, [accessToken]);

  const runAction = async (operation: () => Promise<void>, fallback: string) => {
    setBusy(true);
    setError(null);
    try {
      await operation();
    } catch (actionError) {
      setError(toMessage(actionError, fallback));
    } finally {
      setBusy(false);
    }
  };

  const toDepartmentRoles = (form: UserFormState) => {
    const normalized = form.departmentId.trim();
    if (!normalized) {
      return [];
    }

    const id = Number(normalized);
    if (!Number.isFinite(id) || id <= 0) {
      return [];
    }

    return [{ appDepartmentId: id, roleName: form.departmentRoleName }];
  };

  const buildDepartmentRouting = (form: DepartmentFormState) => {
    const routeNumber = form.routeNumber.trim();
    if (!routeNumber) {
      return null;
    }

    return {
      officeRoute: { isPromptEnabled: false, route: { to: form.routeTo, number: routeNumber, external: '' } },
      outOfOfficeRoute: { isPromptEnabled: false, route: { to: 'VoiceMail', number: routeNumber, external: '' } },
      breakRoute: { isPromptEnabled: false, route: { to: 'VoiceMail', number: routeNumber, external: '' } },
      holidaysRoute: { isPromptEnabled: false, route: { to: 'VoiceMail', number: routeNumber, external: '' } }
    };
  };

  const createUser = async () => {
    await runAction(async () => {
      const payload: CrmCreateUserRequest = {
        username: createUserForm.username.trim(),
        password: createUserForm.password,
        firstName: createUserForm.firstName.trim(),
        lastName: createUserForm.lastName.trim(),
        emailAddress: createUserForm.emailAddress.trim(),
        ownedExtension: createUserForm.ownedExtension.trim(),
        controlDn: createUserForm.controlDn.trim() || null,
        role: createUserForm.role,
        language: 'EN',
        vmEmailOptions: 'Notification',
        sendEmailMissedCalls: true,
        require2Fa: false,
        callUsEnableChat: createUserForm.callUsEnableChat,
        clickToCallId: createUserForm.clickToCallId.trim() || null,
        webMeetingFriendlyName: createUserForm.webMeetingFriendlyName.trim() || null,
        sipUsername: createUserForm.ownedExtension.trim(),
        sipAuthId: createUserForm.ownedExtension.trim(),
        sipPassword: createUserForm.password,
        sipDisplayName: `${createUserForm.firstName.trim()} ${createUserForm.lastName.trim()}`.trim(),
        departmentRoles: toDepartmentRoles(createUserForm)
      };

      await createCrmUser(accessToken, payload);
      await refreshDirectory();
      setCreateUserForm(createInitialUserForm());
      setSection('users-read');
      setNotice('User created successfully.');
    }, 'User create failed.');
  };

  const updateUser = async () => {
    if (selectedUserId === null) {
      setError('Choose a user in Users / Read first.');
      return;
    }

    await runAction(async () => {
      const payload: CrmUpdateUserRequest = {
        firstName: updateUserForm.firstName.trim(),
        lastName: updateUserForm.lastName.trim(),
        emailAddress: updateUserForm.emailAddress.trim(),
        ownedExtension: updateUserForm.ownedExtension.trim(),
        controlDn: updateUserForm.controlDn.trim() || null,
        role: updateUserForm.role,
        language: 'EN',
        vmEmailOptions: 'Notification',
        sendEmailMissedCalls: true,
        require2Fa: false,
        callUsEnableChat: updateUserForm.callUsEnableChat,
        clickToCallId: updateUserForm.clickToCallId.trim() || null,
        webMeetingFriendlyName: updateUserForm.webMeetingFriendlyName.trim() || null,
        sipUsername: updateUserForm.ownedExtension.trim(),
        sipAuthId: updateUserForm.ownedExtension.trim(),
        sipPassword: '',
        sipDisplayName: `${updateUserForm.firstName.trim()} ${updateUserForm.lastName.trim()}`.trim(),
        isActive: updateUserForm.isActive,
        newPassword: updateUserForm.newPassword.trim() || null,
        departmentRoles: toDepartmentRoles(updateUserForm)
      };

      await updateCrmUser(accessToken, selectedUserId, payload);
      await refreshDirectory();
      setNotice('User updated successfully.');
    }, 'User update failed.');
  };

  const deleteUser = async (id: number) => {
    if (!window.confirm('Delete this user in both CRM and 3CX?')) {
      return;
    }

    await runAction(async () => {
      await deleteCrmUser(accessToken, id);
      await refreshDirectory();
      if (selectedUserId === id) {
        setSelectedUserId(null);
        setUpdateUserForm(createInitialUserForm());
      }
      setNotice('User deleted.');
    }, 'User delete failed.');
  };

  const createDepartment = async () => {
    await runAction(async () => {
      const payload: CrmCreateDepartmentRequest = {
        name: createDepartmentForm.name.trim(),
        language: createDepartmentForm.language.trim(),
        timeZoneId: createDepartmentForm.timeZoneId.trim(),
        disableCustomPrompt: true,
        allowCallService: true,
        props: DEFAULT_DEPARTMENT_PROPS,
        liveChatLink: createDepartmentForm.liveChatLink.trim() || null,
        liveChatWebsite: createDepartmentForm.liveChatWebsite.trim() || null,
        routing: buildDepartmentRouting(createDepartmentForm)
      };

      await createCrmDepartment(accessToken, payload);
      await refreshDirectory();
      setCreateDepartmentForm(createInitialDepartmentForm());
      setSection('departments-read');
      setNotice('Department created successfully.');
    }, 'Department create failed.');
  };

  const updateDepartment = async () => {
    if (selectedDepartmentId === null) {
      setError('Choose a department in Departments / Read first.');
      return;
    }

    await runAction(async () => {
      const payload: CrmUpdateDepartmentRequest = {
        name: updateDepartmentForm.name.trim(),
        language: updateDepartmentForm.language.trim(),
        timeZoneId: updateDepartmentForm.timeZoneId.trim(),
        disableCustomPrompt: true,
        allowCallService: true,
        props: DEFAULT_DEPARTMENT_PROPS,
        liveChatLink: updateDepartmentForm.liveChatLink.trim() || null,
        liveChatWebsite: updateDepartmentForm.liveChatWebsite.trim() || null,
        routing: buildDepartmentRouting(updateDepartmentForm)
      };

      await updateCrmDepartment(accessToken, selectedDepartmentId, payload);
      await refreshDirectory();
      setNotice('Department updated successfully.');
    }, 'Department update failed.');
  };

  const deleteDepartment = async (id: number) => {
    if (!window.confirm('Delete this department in both CRM and 3CX?')) {
      return;
    }

    await runAction(async () => {
      await deleteCrmDepartment(accessToken, id);
      await refreshDirectory();
      if (selectedDepartmentId === id) {
        setSelectedDepartmentId(null);
        setUpdateDepartmentForm(createInitialDepartmentForm());
      }
      setNotice('Department deleted.');
    }, 'Department delete failed.');
  };

  const createParking = async () => {
    await runAction(async () => {
      const groupIds = parkingGroupIds
        .split(',')
        .map((value) => Number(value.trim()))
        .filter((value) => Number.isFinite(value) && value > 0);

      if (groupIds.length === 0) {
        setError('Enter at least one valid 3CX Group ID.');
        return;
      }

      const created = await createSharedParking(accessToken, { groupIds });
      setParkingInfo(`Created parking ${created.number} (ID ${created.id})`);
      setNotice('Shared parking created.');
    }, 'Create parking failed.');
  };

  const findParking = async () => {
    await runAction(async () => {
      const number = parkingNumber.trim();
      if (!number) {
        setError('Enter parking number.');
        return;
      }

      const found = await getParkingByNumber(accessToken, number);
      setParkingInfo(`Found parking ${found.number} (ID ${found.id})`);
    }, 'Find parking failed.');
  };

  const removeParking = async () => {
    await runAction(async () => {
      const match = parkingInfo.match(/ID\s(\d+)/);
      const id = match ? Number(match[1]) : NaN;
      if (!Number.isFinite(id) || id <= 0) {
        setError('Find or create parking first.');
        return;
      }

      await deleteSharedParking(accessToken, id);
      setParkingInfo('');
      setNotice('Shared parking deleted.');
    }, 'Delete parking failed.');
  };

  const refreshDashboard = async () => {
    await runAction(async () => {
      await Promise.all([refreshAnalytics(analyticsDays), refreshCdr(callHistoryMeta.take, callHistoryMeta.skip)]);
      setNotice('Dashboard data refreshed.');
    }, 'Dashboard refresh failed.');
  };

  const refreshCdrPage = async () => {
    await runAction(async () => {
      await refreshCdr(callHistoryMeta.take, callHistoryMeta.skip);
      setNotice('CDR refreshed.');
    }, 'CDR refresh failed.');
  };

  const changeCdrTake = async (take: number) => {
    await runAction(async () => {
      await refreshCdr(take, 0);
    }, 'Could not change CDR page size.');
  };

  const previousCdrPage = async () => {
    const nextSkip = Math.max(0, callHistoryMeta.skip - callHistoryMeta.take);
    await runAction(async () => {
      await refreshCdr(callHistoryMeta.take, nextSkip);
    }, 'Could not load previous CDR page.');
  };

  const nextCdrPage = async () => {
    const nextSkip = callHistoryMeta.skip + callHistoryMeta.take;
    await runAction(async () => {
      await refreshCdr(callHistoryMeta.take, nextSkip);
    }, 'Could not load next CDR page.');
  };

  const renderSection = () => {
    switch (section) {
      case 'dashboard':
        return (
          <DashboardPage
            version={version}
            busy={busy}
            analyticsDays={analyticsDays}
            onAnalyticsDaysChange={setAnalyticsDays}
            onRefresh={refreshDashboard}
            callAnalytics={callAnalytics}
            queueLoad={queueLoad}
            statusChartData={statusChartData}
          />
        );
      case 'cdr':
        return (
          <CdrPage
            busy={busy}
            callHistory={callHistory}
            callHistoryMeta={callHistoryMeta}
            onRefresh={refreshCdrPage}
            onChangeTake={changeCdrTake}
            onPreviousPage={previousCdrPage}
            onNextPage={nextCdrPage}
          />
        );
      case 'users-read':
        return (
          <UserReadPage
            users={users}
            busy={busy}
            onCreatePage={() => setSection('users-create')}
            onEdit={(user) => {
              setSelectedUserId(user.id);
              setUpdateUserForm(mapUserToForm(user));
              setSection('users-update');
            }}
            onDelete={deleteUser}
          />
        );
      case 'users-create':
        return (
          <UserCreatePage
            form={createUserForm}
            departments={departments}
            busy={busy}
            onFormChange={setCreateUserForm}
            onSubmit={createUser}
            onReset={() => setCreateUserForm(createInitialUserForm())}
          />
        );
      case 'users-update':
        return (
          <UserUpdatePage
            selectedUserId={selectedUserId}
            form={updateUserForm}
            departments={departments}
            busy={busy}
            onFormChange={setUpdateUserForm}
            onSubmit={updateUser}
            onReset={() => {
              const selected = users.find((item) => item.id === selectedUserId);
              if (selected) {
                setUpdateUserForm(mapUserToForm(selected));
              }
            }}
            onGoRead={() => setSection('users-read')}
          />
        );
      case 'departments-read':
        return (
          <DepartmentReadPage
            departments={departments}
            busy={busy}
            onCreatePage={() => setSection('departments-create')}
            onEdit={(department) => {
              setSelectedDepartmentId(department.id);
              setUpdateDepartmentForm(mapDepartmentToForm(department));
              setSection('departments-update');
            }}
            onDelete={deleteDepartment}
          />
        );
      case 'departments-create':
        return (
          <DepartmentCreatePage
            form={createDepartmentForm}
            busy={busy}
            onFormChange={setCreateDepartmentForm}
            onSubmit={createDepartment}
            onReset={() => setCreateDepartmentForm(createInitialDepartmentForm())}
          />
        );
      case 'departments-update':
        return (
          <DepartmentUpdatePage
            selectedDepartmentId={selectedDepartmentId}
            form={updateDepartmentForm}
            busy={busy}
            onFormChange={setUpdateDepartmentForm}
            onSubmit={updateDepartment}
            onReset={() => {
              const selected = departments.find((item) => item.id === selectedDepartmentId);
              if (selected) {
                setUpdateDepartmentForm(mapDepartmentToForm(selected));
              }
            }}
            onGoRead={() => setSection('departments-read')}
          />
        );
      case 'parking':
        return (
          <ParkingToolsPage
            busy={busy}
            parkingGroupIds={parkingGroupIds}
            parkingNumber={parkingNumber}
            parkingInfo={parkingInfo}
            onParkingGroupIdsChange={setParkingGroupIds}
            onParkingNumberChange={setParkingNumber}
            onCreateParking={createParking}
            onFindParking={findParking}
            onDeleteParking={removeParking}
          />
        );
      default:
        return null;
    }
  };

  return (
    <section className="supervisor-workspace">
      <SupervisorNav active={section} onChange={setSection} />

      <div className="supervisor-content">
        <article className="card supervisor-hero">
          <div className="analytics-header">
            <div>
              <h2>Supervisor Business Pages</h2>
              <p className="history-summary">Separated pages for CRUD, CDR, and KPI analytics.</p>
            </div>
            <div className="status-strip">
              <span className="status-chip status-info">3CX Version: {version}</span>
              <span className={`status-chip ${busy ? 'status-waiting' : 'status-active'}`}>{busy ? 'Working' : 'Ready'}</span>
            </div>
          </div>
          {error && <div className="banner-error">{error}</div>}
          {notice && <div className="banner-success">{notice}</div>}
        </article>

        {renderSection()}
      </div>
    </section>
  );
}
