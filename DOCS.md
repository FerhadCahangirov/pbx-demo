# PBX Demo Technical Manual (Backend + Frontend)

Coverage scope: all authored source/configuration files in `pbx-demo-backend` and `pbx-demo-frontend`, plus top-level `README.md`. Generated/vendor artifacts (`bin`, `obj`, `node_modules`, `dist`, `*.tsbuildinfo`, EF designer snapshots) are cataloged and explained as generated assets.

## Part 1. System Overview

### 1.1 Project Purpose
This project is a unified operator/supervisor workspace for:
- Real-time softphone operations
- Browser-to-browser WebRTC calling (inside the app)
- 3CX PBX call control integration (extension state + call control)
- CRM/supervisor operations (users, departments, parking, CDR analytics)

### 1.2 Problem It Solves
It combines:
- PBX call control visibility
- Operator call handling UI
- Supervisor CRM admin pages
- Real-time event streams
- Call history/analytics

into one web application instead of separate PBX client and admin tools.

### 1.3 Target Users
- Operators with assigned 3CX extensions
- Supervisors managing users/departments and monitoring call activity
- Technical admins integrating with 3CX API and SIP/WebRTC config

### 1.4 High-Level Architecture (Text Diagram)

```text
[React Frontend (Vite)]
  - App shell / Operator UI / Supervisor UI
  - useSoftphoneSession hook
  - SignalR client
  - WebRTC peer connections
  - Optional SIP.js path (currently disabled)
  - PBX audio bridge via HTTP streaming

        | HTTPS REST + SignalR + audio stream
        v

[ASP.NET Core Backend]
  - AuthController
  - SoftphoneController
  - CrmController
  - SoftphoneHub (SignalR)
  - CallManager (PBX session orchestration)
  - WebRtcCallManager (browser call orchestration)
  - CrmManagementService (3CX config + local CRM sync)
  - CallCdrService (CDR persistence/analytics)
  - EF Core DbContext + SQL Server

        | HTTPS (OAuth token + 3CX config APIs)
        | HTTPS + WebSocket (3CX Call Control)
        v

[3CX PBX]
  - /connect/token
  - /xapi/v1/*
  - /callcontrol
  - /callcontrol/ws
  - participant audio stream endpoints
```

### 1.5 Tech Stack Overview

#### Backend
- .NET 8 ASP.NET Core Web API (`pbx-demo-backend`)
- SignalR
- EF Core 8 + SQL Server
- JWT bearer auth
- Swashbuckle package installed (not enabled in `Program.cs`)

#### Frontend
- React 18 + TypeScript + Vite
- Tailwind CSS
- `@microsoft/signalr`
- WebRTC browser APIs
- `sip.js` (code path present, currently disabled by flag)

### 1.6 Backend/Frontend Interaction Flow
- Frontend logs in using `POST /api/auth/login`.
- Backend returns JWT + session metadata.
- Frontend stores auth session in `localStorage`.
- Frontend fetches `GET /api/softphone/session`.
- Frontend connects SignalR hub `/hubs/softphone`.
- Backend publishes session snapshots, PBX events, WebRTC browser call updates/signals.
- Frontend invokes REST and hub methods for call actions and supervisor CRUD.

### 1.7 Authentication Flow
- Username/password validated against local DB (`Users` table), seeded from config if DB is empty.
- Backend creates JWT with claims:
  - `NameIdentifier` = username
  - `Role` = `User` or `Supervisor`
  - custom `sid` (session id)
  - custom `uid` (user id)
- SignalR hub accepts JWT from query `access_token` for `/hubs/softphone`.
- Supervisor endpoints require role policy `SupervisorOnly`.

### 1.8 Data Flow
- PBX topology and events are cached in memory per session (`SoftphoneSession`).
- Backend transforms 3CX entities into UI snapshot models.
- PBX/browser call events are persisted into local CDR tables.
- Supervisor UI reads local CRM entities + local CDR analytics + live 3CX admin data via backend.

### 1.9 Error Handling Strategy
- Backend: `AppExceptionMiddleware` converts domain exceptions to JSON error payloads with `traceId`.
- Hub: `SoftphoneHub` catches `AppException` and throws `HubException(message)`.
- Frontend: `ApiRequestError` wraps HTTP failures, hook normalizes error messages and handles auth expiration.

### 1.10 Logging Strategy
- ASP.NET Core default logging via `ILogger<>`.
- Key logs in:
  - 3CX websocket connect/reconnect/failure
  - call control fallback attempts
  - cleanup/rollback failures (3CX delete rollback)
  - CDR persistence failures
- No centralized logging sink configured.

### 1.11 Security Strategy (Current + Gaps)
Implemented:
- JWT auth
- Role-based authorization (`SupervisorOnly`)
- PBKDF2 password hashing for stored credentials
- Fixed-time hash compare
- 401/403 enforcement in controllers/hub

Gaps / risks in current repo:
- Real secrets appear committed in `pbx-demo-backend/appsettings.json` and `pbx-demo-backend/softphone.config.json`
- CORS is fully permissive (`AllowAnyOrigin/Header/Method`)
- `RequireHttpsMetadata = false`
- SignalR detailed errors enabled
- Swagger package installed but auth/docs hardening not configured
- `.env` files and config files should move to secret stores for production

---

## Part 2. Backend Documentation (Complete)

## 2.1 Backend Folder Structure (`pbx-demo-backend`)

```text
pbx-demo-backend/
  Program.cs
  pbx-demo-backend.csproj
  pbx-demo-backend.slnx
  appsettings.json
  softphone.config.json
  .env.example
  BACKEND_DOCS.md
  Controllers/
    AuthController.cs
    SoftphoneController.cs
    CrmController.cs
  Hubs/
    SoftphoneHub.cs
  Domain/
    Constants.cs
    Errors.cs
    Models.cs
    WebRtcModels.cs
    CrmEntities.cs
    CrmContracts.cs
  Infrastructure/
    AppExceptionMiddleware.cs
    CallControlMapFactory.cs
    ClaimsPrincipalExtensions.cs
    EntityPathHelper.cs
    RequestInputResolver.cs
    SoftphoneDbContext.cs
  Services/
    AuthService.cs
    CallCdrService.cs
    CallManager.cs
    CrmManagementService.cs
    CrmServiceSupport.cs
    DatabaseBootstrapper.cs
    EventDispatcher.cs
    JwtTokenService.cs
    PasswordHasher.cs
    SessionPresenceRegistry.cs
    SessionRegistry.cs
    SipConfigurationService.cs
    ThreeCxCallControlClient.cs
    ThreeCxClientFactory.cs
    ThreeCxConfigurationClient.cs
    UserDirectoryService.cs
    WebRtcCallManager.cs
  Migrations/
    20260220051340_InitDB.cs (+ Designer)
    20260220055734_CDR.cs (+ Designer)
    SoftphoneDbContextModelSnapshot.cs
  Properties/
    launchSettings.json
  Samples/
    fullinfo.sample.json
    websocket.sample.json
```

### Design Notes
- `Domain` contains DTOs, options, enums, errors, and persistence entities.
- `Infrastructure` contains cross-cutting plumbing and EF Core context.
- `Services` contains orchestration and external API integrations.
- `Controllers` expose REST APIs.
- `Hubs` expose real-time API.
- `Migrations` exist, but runtime bootstrap uses `EnsureCreated()` plus manual SQL CDR schema patching. This is an important architectural inconsistency.

---

## 2.2 Startup, DI, Middleware, and Runtime Wiring (`Program.cs`)

### 2.2.1 Startup Responsibilities
`Program.cs`:
- Loads config from `appsettings.json` and `softphone.config.json`
- Binds `SoftphoneOptions`
- Registers controllers + SignalR + HttpClient + EF Core DbContextFactory
- Configures JWT auth and supervisor role policy
- Registers all services (mostly singletons)
- Runs `DatabaseBootstrapper.InitializeAsync()`
- Adds middleware and endpoint mappings

### 2.2.2 DI Registrations and Why They Exist
Registered singletons:
- `PasswordHasher`
- `UserDirectoryService`
- `ThreeCxConfigurationClient`
- `CrmManagementService`
- `DatabaseBootstrapper`
- `JwtTokenService`
- `ThreeCxClientFactory`
- `SessionRegistry`
- `SessionPresenceRegistry`
- `EventDispatcher`
- `CallCdrService`
- `CallManager`
- `WebRtcCallManager`
- `AuthService`
- `SipConfigurationService`

Why mostly singletons:
- `CallManager`, `WebRtcCallManager`, registries maintain shared in-memory session/call state.
- `ThreeCxConfigurationClient` caches 3CX OAuth token.
- State coordination uses `ConcurrentDictionary` and `SemaphoreSlim`.

### 2.2.3 Middleware Order
1. `AppExceptionMiddleware`
2. CORS (`SoftphoneCors`)
3. Authentication
4. Authorization
5. Controllers + SignalR hub mappings

### 2.2.4 Authentication Configuration
- JWT issuer, audience, signing key from `SoftphoneOptions`
- Hub token support via query `access_token` only for `/hubs/softphone`
- `ClockSkew = 30s`

### 2.2.5 Authorization Policy
- `SupervisorOnly` requires role `Supervisor` (`AppUserRoles.Supervisor`)

### 2.2.6 CORS
Current policy `SoftphoneCors` is open to all origins/headers/methods. Good for demo, unsafe for production.

---

## 2.3 Backend Configuration and Environment Files

## 2.3.1 Files

### `pbx-demo-backend/appsettings.json`
Contains:
- Logging levels
- SQL Server connection string
- `Softphone` settings (JWT + reconnect config + seed users)

### `pbx-demo-backend/softphone.config.json`
Contains:
- 3CX app credentials (`PbxBase`, `AppId`, `AppSecret`)
- SIP/WebRTC config (`SipWebRtc`) sent to frontend on request

### `pbx-demo-backend/.env.example`
Contains example `ASPNETCORE_URLS=http://0.0.0.0:8080`
Note: .NET does not load `.env` automatically by default in this project.

### `pbx-demo-backend/Properties/launchSettings.json`
Local dev launch profiles:
- `http` on `5201`
- `https` on `7138` (+ `5201`)
- `IIS Express`

### Important Runtime Mismatch
`pbx-demo-frontend/vite.config.ts` proxies to `http://localhost:8080`, but backend launch settings default to `5201/7138`. You must either:
- set `ASPNETCORE_URLS=http://localhost:8080`, or
- change Vite proxy target, or
- set `VITE_API_BASE` in frontend to backend URL.

## 2.3.2 `SoftphoneOptions` Object Model (`Domain/Models.cs`)
Main options graph:
- `SoftphoneOptions`
  - JWT settings
  - websocket reconnect settings
  - `ThreeCxApiOptions`
  - `SoftphoneSipWebRtcOptions`
  - `List<SoftphoneUserCredential>`

---

## 2.4 Backend Domain Model Catalog (Classes, Enums, Contracts)

## 2.4.1 Constants and Enums (`Domain/Constants.cs`)
- `CallControlConstants`
  - 3CX entity names (`participants`, `devices`)
  - DN types (`Wextension`, `Wroutepoint`)
  - participant statuses/actions
  - pseudo device `not_registered_dev`
- `ClaimTypesEx`
  - custom JWT claims (`sid`, `uid`)
- `ThreeCxEventType`
  - maps 3CX websocket event types
- `SoftphoneCallDirection`
  - `Incoming`, `Outgoing`
  - JSON serialized as string

## 2.4.2 Errors (`Domain/Errors.cs`)
Base:
- `AppException(name, message, errorCode)`

Derived:
- `BadRequestException` (400)
- `UnauthorizedException` (401)
- `ForbiddenException` (403)
- `NotFoundException` (404)
- `InternalServerErrorException` (500)
- `UpstreamApiException` (variable upstream status code)

Purpose:
- Consistent domain error mapping through middleware and hub wrappers.

## 2.4.3 Persistence Entities (`Domain/CrmEntities.cs`)

### `AppUserEntity`
Purpose: local CRM user + app auth + extension/SIP metadata + 3CX linkage.

Key properties:
- Identity: `Id`, `Username`, `PasswordHash`
- Profile: `FirstName`, `LastName`, `EmailAddress`
- Telephony: `OwnedExtension`, `ControlDn`
- Role: `Role` (`User`/`Supervisor`)
- 3CX profile settings: `Language`, `PromptSet`, `VmEmailOptions`, `SendEmailMissedCalls`, `Require2Fa`
- Website/chat settings: `CallUsEnableChat`, `ClickToCallId`, `WebMeetingFriendlyName`
- SIP credentials for frontend bridge registration path: `SipUsername`, `SipAuthId`, `SipPassword`, `SipDisplayName`
- External link: `ThreeCxUserId`
- Lifecycle: `IsActive`, `CreatedAtUtc`, `UpdatedAtUtc`
- Navigation: `DepartmentMemberships`, `CallCdrs`

### `AppDepartmentEntity`
Purpose: local CRM department linked to 3CX group.

Key properties:
- Identity/link: `Id`, `Name`, `ThreeCxGroupId`, `ThreeCxGroupNumber`
- Localization/routing settings: `Language`, `TimeZoneId`, `PromptSet`, `DisableCustomPrompt`
- Serialized configs: `PropsJson`, `RoutingJson`
- Live chat integration: `LiveChatLink`, `LiveChatWebsite`, `ThreeCxWebsiteLinkId`
- Lifecycle timestamps
- Navigation: `UserMemberships`

### `AppDepartmentMembershipEntity`
Purpose: bridge table between users and departments with 3CX role rights.

Key properties:
- FK pair: `AppUserId`, `AppDepartmentId`
- Role mapping: `ThreeCxRoleName`
- `CreatedAtUtc`

### `AppCallCdrEntity`
Purpose: canonical local call record for both PBX and browser calls.

Key properties:
- Identity: `Id`
- Source: `Source` (`Pbx` or `Browser`)
- Operator linkage: `OperatorUserId`, `OperatorUser`, `OperatorUsername`, `OperatorExtension`
- Correlation: `TrackingKey`, `CallScopeId`, `ParticipantId`, `PbxCallId`, `PbxLegId`
- Call state: `Direction`, `Status`, `RemoteParty`, `RemoteName`, `EndReason`
- Timing: `StartedAtUtc`, `AnsweredAtUtc`, `EndedAtUtc`, `LastStatusAtUtc`
- Flags: `IsActive`
- Audit: `CreatedAtUtc`, `UpdatedAtUtc`
- Navigation: `StatusHistory`

### `AppCallCdrStatusHistoryEntity`
Purpose: event timeline rows per call CDR.

Key properties:
- FK: `CallCdrId`
- Snapshot status: `Status`
- Event metadata: `EventType`, `EventReason`
- Time: `OccurredAtUtc`, `CreatedAtUtc`

### Other Types
- `AppUserRole` enum
- `AppUserRoles` string constants
- `AppCallSource` enum
- `AppUserRecord` immutable read model for auth/runtime lookup
- `AppUserEntityMapper.ToRecord()` mapper extension

## 2.4.4 Runtime/Transport Models (`Domain/Models.cs`)

### Authentication/Session Contracts
- `LoginRequest`
- `LoginResponse`
- `SessionSnapshotResponse`
- `SoftphoneEventEnvelope`

### SIP Config Contract
- `SipRegistrationConfigResponse`

### Softphone Action Requests
- `SelectExtensionRequest`
- `SetActiveDeviceRequest`
- `OutgoingCallRequest`
- `TransferRequest`

### Snapshot Item Models
- `SoftphoneDeviceView`
- `SoftphoneCallView`

### 3CX Integration Models
- `ThreeCxConnectSettings`
- `ThreeCxDnInfo`
- `ThreeCxDnInfoModel`
- `ThreeCxDevice`
- `ThreeCxParticipant`
- `ThreeCxCallControlResult`
- `ThreeCxWsEvent`
- `ThreeCxWsEventBody`

### In-Memory Session Runtime State
- `SoftphoneSession : IAsyncDisposable`
  - holds per-session cached topology/devices/participants
  - holds selected extension, control DN, active device, websocket state
  - includes session gate `SemaphoreSlim` for thread-safe mutations
  - disposes embedded `ThreeCxCallControlClient`

## 2.4.5 Browser WebRTC Models (`Domain/WebRtcModels.cs`)
- `BrowserCallView`
- `WebRtcSignalRequest`
- `WebRtcSignalMessage`

Used by SignalR hub and `WebRtcCallManager`.

## 2.4.6 CRM API Contracts (`Domain/CrmContracts.cs`)
This file contains request/response DTOs for supervisor APIs. Frontend `src/domain/crm.ts` mirrors these shapes closely.

Classes by function:
- User requests: `CrmCreateUserRequest`, `CrmUpdateUserRequest`, `CrmUserDepartmentRoleRequest`
- User responses: `CrmUserResponse`, `CrmDepartmentRoleResponse`
- Department config: `CrmDepartmentPropsDto`, `CrmDepartmentRouteTargetDto`, `CrmDepartmentRouteDto`, `CrmDepartmentRoutingDto`
- Department requests/responses: `CrmCreateDepartmentRequest`, `CrmUpdateDepartmentRequest`, `CrmDepartmentResponse`
- Friendly name APIs: `CrmValidateFriendlyNameRequest`, `CrmUpdateFriendlyNameRequest`
- Shared parking APIs: `CrmCreateSharedParkingRequest`, `CrmSharedParkingResponse`
- System/version API: `CrmVersionResponse`
- CDR APIs:
  - `CrmCallStatusHistoryItemResponse`
  - `CrmCallHistoryItemResponse`
  - `CrmCallHistoryResponse`
  - `CrmOperatorCallKpiResponse`
  - `CrmCallAnalyticsResponse`

---

## 2.5 Infrastructure Layer (Class-by-Class)

### `Infrastructure/AppExceptionMiddleware.cs`
Purpose:
- Converts exceptions to consistent JSON error responses.

Behavior:
- `AppException` -> uses embedded `ErrorCode`, `ErrorName`, `Message`
- Unknown exception -> logs error and returns generic 500
- Adds `traceId` from `HttpContext.TraceIdentifier`

Why it exists:
- Prevents controller/hub/service duplication for error formatting.

### `Infrastructure/RequestInputResolver.cs`
Purpose:
- Robustly read input values from query, form, JSON body, or raw body.

Key methods:
- `ResolveFieldAsync(...)`
- Internal JSON/raw caching via `HttpContext.Items`

Why it exists:
- `AuthController` and `SoftphoneController` intentionally support flexible clients (JSON, raw body, form, query).

### `Infrastructure/CallControlMapFactory.cs`
Purpose:
- Converts `IEnumerable<ThreeCxDnInfo>` from 3CX `/callcontrol` into dictionary map keyed by DN.

Why:
- Efficient runtime lookup by DN and participant/device id.

### `Infrastructure/ClaimsPrincipalExtensions.cs`
Purpose:
- Strongly validate required JWT claims from `User`.

Methods:
- `RequireSessionId()`
- `RequireUsername()`
- `RequireUserId()`

### `Infrastructure/EntityPathHelper.cs`
Types:
- `EntityOperation` record struct (`Dn`, `Type`, `Id`)

Purpose:
- Parses 3CX websocket entity path `/callcontrol/{dn}/{entityType}/{entityId}`

### `Infrastructure/SoftphoneDbContext.cs`
Purpose:
- EF Core mapping for users, departments, memberships, CDRs, CDR status history.

Why:
- Local persistence for auth/user management and analytics.

---

## 2.6 Database Documentation (Schema, Relationships, Indexes, Constraints)

## 2.6.1 Tables
- `Users`
- `Departments`
- `DepartmentMemberships`
- `CallCdrs`
- `CallCdrStatusHistory`

## 2.6.2 Relationships
- `Users` 1:N `DepartmentMemberships`
- `Departments` 1:N `DepartmentMemberships`
- `Users` 1:N `CallCdrs`
- `CallCdrs` 1:N `CallCdrStatusHistory`

## 2.6.3 Important Indexes
From `SoftphoneDbContext` + migrations:
- `Users.Username` unique
- `Users.EmailAddress` unique
- `Users.OwnedExtension`
- `Users.ThreeCxUserId`
- `Departments.Name` unique
- `Departments.ThreeCxGroupId` unique
- `DepartmentMemberships(AppUserId, AppDepartmentId)` unique
- `CallCdrs(OperatorUserId, StartedAtUtc)`
- `CallCdrs(Source, TrackingKey)`
- `CallCdrs(OperatorUserId, PbxCallId)`
- `CallCdrs(IsActive)`
- `CallCdrStatusHistory(CallCdrId, OccurredAtUtc)`

## 2.6.4 Constraints
- PKs on all tables
- FK cascades:
  - memberships delete on user/department delete
  - CDRs delete on user delete
  - CDR history delete on call delete

## 2.6.5 Migration Files
- `20260220051340_InitDB.cs`: creates `Users`, `Departments`, `DepartmentMemberships`
- `20260220055734_CDR.cs`: creates `CallCdrs`, `CallCdrStatusHistory`
- `*.Designer.cs` and `SoftphoneDbContextModelSnapshot.cs`: EF auto-generated model snapshots/targets

## 2.6.6 Runtime Schema Creation Behavior (Important)
`DatabaseBootstrapper` uses:
- `EnsureCreatedAsync()`
- manual SQL `EnsureCallCdrSchemaAsync(...)`

This bypasses normal migration application flow at runtime. For enterprise deployment, choose one strategy:
- `Database.Migrate()` only, or
- bootstrap SQL only
to avoid drift/confusion.

---

## 2.7 Controllers and REST API Documentation

## 2.7.1 `AuthController` (`Controllers/AuthController.cs`)
Route base: `/api/auth`

### `POST /api/auth/login`
Auth: none

Request fields:
- `username`
- `password`

Input parsing:
- query/form/json/raw body supported via `RequestInputResolver`

Flow:
- Build `LoginRequest`
- `AuthService.LoginAsync`
- Return `LoginResponse` (JWT + session info)

Example request:
```json
{
  "username": "operator1",
  "password": "secret"
}
```

Example response:
```json
{
  "userId": 12,
  "accessToken": "<jwt>",
  "expiresAtUtc": "2026-02-22T18:30:00Z",
  "sessionId": "3c0b8c...",
  "username": "operator1",
  "displayName": "Jane Doe",
  "role": "User",
  "hasSoftphoneAccess": true,
  "ownedExtensionDn": "100",
  "pbxBase": "https://pbx.example.com"
}
```

### `POST /api/auth/logout`
Auth: JWT required

Flow:
- Reads `sid` claim
- `AuthService.LogoutAsync(sessionId)`
- Ends WebRTC session state + PBX session state
- Returns `204 No Content`

---

## 2.7.2 `SoftphoneController` (`Controllers/SoftphoneController.cs`)
Route base: `/api/softphone`
Auth: `[Authorize]`

### Session & Extension Routes
- `GET /session` -> `SessionSnapshotResponse`
- `GET /extensions` -> minimal selection/owned extension object
- `POST /extensions/select` -> accepts `extensionDn`, returns `202`
- `POST /devices/active` -> accepts `deviceId`, returns `202`

### SIP Config Route
- `GET /sip/config` -> `SipRegistrationConfigResponse`
- Used by disabled SIP.js code path and kept available.

### PBX Call Control Routes
- `POST /calls/outgoing` (`destination`)
- `POST /calls/{participantId}/answer`
- `POST /calls/{participantId}/reject`
- `POST /calls/{participantId}/end`
- `POST /calls/{participantId}/transfer` (`destination`)

Response pattern:
- `202 Accepted` for command-style operations

### Audio Stream Routes (PBX call control bridge)
- `GET /calls/{participantId}/audio` -> binary downlink stream
- `POST /calls/{participantId}/audio` -> binary uplink stream

Purpose:
- Backend bridges 3CX call-control participant audio to browser UI.

Example transfer request:
```json
{ "destination": "101" }
```

---

## 2.7.3 `CrmController` (`Controllers/CrmController.cs`)
Route base: `/api/crm`
Auth: `[Authorize(Policy = "SupervisorOnly")]`

### User CRUD
- `GET /users`
- `POST /users`
- `PUT /users/{id}`
- `DELETE /users/{id}`

### User Friendly Name APIs
- `POST /users/validate-friendly-name`
- `PUT /users/{id}/friendly-name`

### Department CRUD
- `GET /departments`
- `POST /departments`
- `PUT /departments/{id}`
- `DELETE /departments/{id}`

### 3CX System Utility APIs
- `GET /system/version`
- `GET /system/groups/default`
- `GET /system/groups/{groupId}/members`
- `GET /system/3cx-users`

### Shared Parking APIs
- `POST /system/parking`
- `GET /system/parking/{number}`
- `DELETE /system/parking/{parkingId}`

### CDR / Analytics APIs
- `GET /calls/history?take=...&skip=...&operatorUserId=...`
- `GET /calls/analytics?days=...` (clamped to 1..90)

Example create user request (abridged):
```json
{
  "username": "agent100",
  "password": "Agent100!",
  "firstName": "Agent",
  "lastName": "One",
  "emailAddress": "agent100@example.com",
  "ownedExtension": "100",
  "role": "User",
  "language": "EN",
  "vmEmailOptions": "Notification",
  "sendEmailMissedCalls": true,
  "require2Fa": false,
  "callUsEnableChat": true,
  "departmentRoles": [
    { "appDepartmentId": 2, "roleName": "users" }
  ]
}
```

Example call analytics response (abridged):
```json
{
  "periodStartUtc": "2026-02-15T00:00:00Z",
  "periodEndUtc": "2026-02-22T00:00:00Z",
  "totalCalls": 128,
  "activeCalls": 3,
  "answeredCalls": 101,
  "missedCalls": 14,
  "failedCalls": 13,
  "totalTalkSeconds": 18342,
  "averageTalkSeconds": 181.6,
  "totalOperators": 12,
  "activeOperators": 4,
  "operatorKpis": []
}
```

---

## 2.8 SignalR Hub API (`Hubs/SoftphoneHub.cs`)

Hub route: `/hubs/softphone`
Auth: `[Authorize]`

## 2.8.1 Connection Lifecycle
### `OnConnectedAsync`
- Validates JWT claims (`sid`, username)
- Ensures backend PBX session exists (`CallManager.EnsureSessionAsync`)
- Adds connection to SignalR group `session:{sessionId}`
- Tracks presence (`SessionPresenceRegistry`)
- Sends:
  - `SessionSnapshot(...)`
  - `BrowserCallsSnapshot(...)`

### `OnDisconnectedAsync`
- Removes group membership
- Updates presence registry
- Ends WebRTC browser calls for disconnected session (`WebRtcCallManager.HandleSessionDisconnectedAsync`)

## 2.8.2 Hub Methods (Client -> Server)
- `PlaceBrowserCall(destinationExtension)` -> returns `BrowserCallView`
- `AnswerBrowserCall(callId)`
- `RejectBrowserCall(callId)`
- `EndBrowserCall(callId)`
- `SendWebRtcSignal(WebRtcSignalRequest)`
- `MarkCallConnected(callId)`

Error behavior:
- Domain exceptions become `HubException(message)`.

## 2.8.3 Hub Client Contract (`ISoftphoneHubClient`)
Server emits:
- `SessionSnapshot(SessionSnapshotResponse snapshot)`
- `SoftphoneEvent(SoftphoneEventEnvelope envelope)`
- `BrowserCallsSnapshot(IReadOnlyList<BrowserCallView> calls)`
- `BrowserCallUpdated(BrowserCallView call)`
- `WebRtcSignal(WebRtcSignalMessage signal)`

---

## 2.9 Services Deep Dive (Backend Core Logic)

## 2.9.1 `PasswordHasher`
Purpose:
- PBKDF2 hash/verify for app passwords

Notable behavior:
- Hash format: `pbkdf2$iterations$salt$hash`
- `VerifyPassword` supports legacy plaintext equality fallback if stored hash is not PBKDF2-prefixed

Enterprise note:
- Remove plaintext fallback after migration period.

## 2.9.2 `UserDirectoryService`
Purpose:
- Read-only user lookup by username/id from DB
- Returns `AppUserRecord` (not EF tracked entity)

Why:
- Safe, lightweight auth/session lookup service.

## 2.9.3 `JwtTokenService`
Purpose:
- Generate JWT and `LoginResponse`

Responsibilities:
- Validate signing key length (>= 32 chars)
- Create JWT claims incl. session id and user id
- Compute expiry from config
- Build display name fallback from first/last name or username

## 2.9.4 `AuthService`
Purpose:
- Login/logout orchestration

`LoginAsync` flow:
- Validate non-empty credentials
- Load user by username
- Check `IsActive`
- Verify password
- Validate 3CX app credentials in config
- Create server-side softphone session if user has `OwnedExtension`
- Return JWT/login response

`LogoutAsync` flow:
- End WebRTC calls
- Remove PBX softphone session

## 2.9.5 `SipConfigurationService`
Purpose:
- Produce frontend SIP/WebRTC registration config for current user

Logic:
- Returns disabled response if `SipWebRtc.Enabled=false`
- Validates global SIP ws/domain config
- Loads current user
- Builds AOR/auth username/password/display name using user SIP fields with fallbacks
- Returns ICE server list from config after normalization/dedup

## 2.9.6 `SessionRegistry`
Purpose:
- In-memory session map (`sessionId -> SoftphoneSession`)

Methods:
- `TryGet`
- `Add`
- `RemoveAsync` (disposes `SoftphoneSession`)
- `List`

## 2.9.7 `SessionPresenceRegistry`
Purpose:
- Tracks online SignalR connections per session
- Used by `WebRtcCallManager` to allow browser calls only to online peers

## 2.9.8 `EventDispatcher`
Purpose:
- Thin adapter to publish snapshots/events to SignalR session groups

Why:
- Decouples `CallManager` from direct hub context usage.

## 2.9.9 `ThreeCxClientFactory`
Purpose:
- Creates per-session `ThreeCxCallControlClient` using global reconnect config and logger factory.

## 2.9.10 `ThreeCxConfigurationClient`
Purpose:
- Shared 3CX XAPI admin client (`/xapi/v1/*`) with OAuth token caching

Key features:
- Bearer token cache with `_tokenGate`
- Retry on 401 by invalidating token and cloning request
- GET/POST/PATCH/DELETE wrappers returning `JsonElement`
- `GetVersionProbeAsync` extracts `X-3CX-Version` header
- Error normalization to app exceptions (`BadRequest`, `NotFound`, etc.)
- Base URL normalization and validation

Used by:
- `CrmManagementService`

## 2.9.11 `ThreeCxCallControlClient`
Purpose:
- Per-session low-level 3CX Call Control transport (HTTP + websocket)

Main responsibilities:
- Get `/callcontrol` topology (`GetFullInfoAsync`)
- Make calls (`MakeCallAsync`, `MakeCallFromDeviceAsync`)
- Control participants (answer/drop/divert/routeto/transferto)
- Participant audio stream upload/download
- WebSocket connection to `/callcontrol/ws`
- WebSocket reconnect loop
- Emit events:
  - `WsEventReceived`
  - `WsConnectionStateChanged`

Important algorithms:
- Answer fallback inside `ControlParticipantAsync` retries alternate participant/DN when 422/404 on answer
- WebSocket reconnect attempts bounded by config
- `ResponseOwnedStream` ensures HTTP response lifetime follows returned stream lifetime

## 2.9.12 `CallManager` (Most Critical Backend Service)
Purpose:
- Core orchestration for PBX-backed softphone sessions

Primary responsibilities:
- Create/recover per-user PBX session
- Cache 3CX topology/devices/participants
- Build frontend session snapshots
- Execute PBX call commands
- Handle 3CX websocket upsert/remove events
- Resolve participant/DN/control fallback heuristics
- Persist PBX CDR updates

### Session lifecycle
- `EnsureSessionAsync` recreates missing in-memory session from JWT username
- `CreateSessionAsync`:
  - initializes 3CX client
  - loads topology
  - validates owned extension exists and is extension DN
  - validates configured control DN if present
  - falls back to AppId route point as control DN if suitable
  - subscribes to 3CX websocket events
  - initializes selected extension and snapshot
  - persists bootstrap CDR rows for existing calls

### Device/extension management
- `SelectExtensionAsync` enforces user can only select their owned extension
- `SetActiveDeviceAsync` validates selected device exists in cached device map

### Outgoing calls
- `MakeOutgoingCallAsync`
  - requires selected extension + active device
  - supports pseudo-device `not_registered_dev` for server-routed calling
  - falls back from `MakeCallFromDeviceAsync` to `MakeCallAsync` on 422

### Participant control and answer heuristics
Methods:
- `AnswerCallAsync`, `RejectCallAsync`, `EndCallAsync`, `TransferCallAsync`
- internal `ControlParticipantAsync`

Key behavior:
- For answer, optionally refresh topology before action
- Builds candidate list across selected DN and control DN
- Orders candidates by answerability, ringing state, DN affinity
- Retries on 422/403/404 across candidates
- If no directly answerable candidate, attempts route-point workaround:
  - route ringing participant to control DN
  - refresh topology
  - answer participant on control DN

This is a major design decision to compensate for 3CX call-control edge cases.

### WebSocket event processing
- `HandleWsEventAsync`
  - parses entity path
  - on `Upset`, fetches entity payload and calls `ProcessUpsertAsync`
  - on `Remove`, calls `ProcessRemoveAsync`
- `ProcessUpsertAsync`
  - updates topology cache
  - updates device/participant cache
  - builds event envelope + snapshot
  - emits PBX CDR update
- `ProcessRemoveAsync`
  - removes device/participant
  - emits call-ended/device-removed event
  - emits final PBX CDR update

### Snapshot building
- `BuildSnapshotLocked` returns deterministic view:
  - devices sorted by user agent/device id
  - calls sorted by status priority then participant id
- Selected extension state rebuild also injects pseudo device:
  - `Web App / server route`

### PBX CDR persistence integration
- `PersistPbxCdrUpdateAsync` calls `CallCdrService.UpsertPbxCallAsync`
- Failures are logged and do not break runtime call handling

## 2.9.13 `WebRtcCallManager`
Purpose:
- Browser-to-browser call state and WebRTC signaling relay (not PBX calls)

Responsibilities:
- Maintain in-memory browser call records
- Enforce one active browser call per session
- Validate online target extension using `SessionPresenceRegistry`
- Publish call updates to both session groups
- Relay `offer/answer/ice` messages
- Persist browser CDRs for both caller and callee
- End calls when session disconnects

State machine:
- `Ringing` -> `Connecting` -> `Connected` -> `Ended`

Notable business rules:
- Prevent self-call
- Prevent calling offline extension
- Prevent concurrent active call for caller/callee
- Only callee can answer
- End reason varies (`rejected`, `canceled`, `ended`, `peer_disconnected`)

## 2.9.14 `CallCdrService`
Purpose:
- Persist and query local CDR data for PBX and browser calls

### Write paths
- `UpsertPbxCallAsync(PbxCallCdrUpdate)`
- `UpsertWebRtcCallAsync(WebRtcCallCdrUpdate)`

Features:
- Call correlation via `TrackingKey`
- Upsert logic for active and historical calls
- Normalized ended status
- Status history dedupe (`AppendStatusHistoryIfNeededAsync`)
- Talk/total duration derivation

### Read paths
- `GetCallHistoryAsync(...)`
  - paging
  - joins operator user display name
  - loads status history per call
- `GetCallAnalyticsAsync(...)`
  - period-filtered aggregates
  - operator KPI list
  - answered/missed/failed counts
  - talk-time metrics

## 2.9.15 `CrmServiceSupport`
Purpose:
- Internal helper utilities for `CrmManagementService`

Functions:
- normalize optional/default strings
- validate 3CX role names
- JSON serialize/deserialize helper
- extract `value[]` arrays and typed values from `JsonElement`
- random password generation

## 2.9.16 `CrmManagementService`
Purpose:
- Supervisor business orchestration across local CRM DB and 3CX XAPI admin endpoints

Major capabilities:
- User CRUD with local DB + 3CX user sync
- Department CRUD with local DB + 3CX group sync
- Friendly-name validation/update
- Shared parking management
- 3CX utility queries (users/groups/version)

### User create/update/delete flow
Create:
- Validate request and department roles
- Check local username/email uniqueness
- Check 3CX email uniqueness
- Create local `AppUserEntity`
- Create 3CX user via `/xapi/v1/Users`
- Assign group rights via `AssignRolesInThreeCxAsync`
- Optional friendly-name validation/update
- Save local user + memberships
- Roll back 3CX user on failure

Update:
- Load local user + memberships
- Patch 3CX user if linked
- Optionally patch friendly names
- Update local fields and memberships
- Optional password rehash (`newPassword`)

Delete:
- Prevent deleting last active supervisor
- Delete 3CX user (best effort)
- Delete local user (cascades memberships/CDRs)

### Department create/update/delete flow
Create:
- Validate uniqueness locally and in 3CX
- Create 3CX group
- Optional routing config patch
- Optional live-chat website link creation
- Save local department
- Roll back 3CX group on failure

Update:
- Patch 3CX group
- Optional routing patch
- Optional live-chat link create (if changed)
- Update local serialized props/routing + metadata

Delete:
- Calls 3CX `Pbx.DeleteCompanyById`
- Deletes local department

### Shared parking flow
- Create via `/xapi/v1/Parkings`
- Lookup by number via `Pbx.GetByNumber`
- Delete via `/xapi/v1/Parkings({id})`

---

## 2.10 Background Services
There are no `IHostedService` background services.
Background-like behavior is implemented inside singleton services:
- `ThreeCxCallControlClient` WebSocket receive/reconnect loops
- SignalR hub connection lifecycle callbacks
- In-memory call/session registries

---

## Part 3. Frontend Documentation (Complete)

## 3.1 Frontend Folder Structure (`pbx-demo-frontend`)

```text
pbx-demo-frontend/
  package.json
  package-lock.json
  vite.config.ts
  tailwind.config.ts
  postcss.config.cjs
  tsconfig.json
  tsconfig.app.json
  tsconfig.node.json
  .env
  .env.example
  index.html
  src/
    main.tsx
    App.tsx
    index.css
    vite-env.d.ts
    domain/
      softphone.ts
      crm.ts
    services/
      httpClient.ts
      softphoneApi.ts
      crmApi.ts
      signalrClient.ts
      mediaDevices.ts
      authStorage.ts
    state/
      sessionStore.ts
    hooks/
      useMicrophone.ts
      useSoftphoneSession.ts
    components/
      DialPad.tsx
      CallList.tsx
      IncomingCallModal.tsx
    pages/
      LoginPage.tsx
      SoftphonePage.tsx
      SupervisorPage.tsx
      supervisor/
        shared.ts
        SupervisorNav.tsx
        DashboardPage.tsx
        CdrPage.tsx
        UserReadPage.tsx
        UserCreatePage.tsx
        UserUpdatePage.tsx
        DepartmentReadPage.tsx
        DepartmentCreatePage.tsx
        DepartmentUpdatePage.tsx
        ParkingToolsPage.tsx
```

---

## 3.2 Frontend Build, Tooling, and Config

### `package.json`
Scripts:
- `npm run start` -> `vite --port 8081`
- `npm run build` -> `tsc -b && vite build`
- `npm run preview`

Dependencies:
- `react`, `react-dom`
- `@microsoft/signalr`
- `sip.js`

Dev dependencies:
- Vite + React plugin
- TypeScript
- Tailwind + PostCSS + Autoprefixer

### `vite.config.ts`
Dev server:
- Port `8081`
- Proxy `/api` -> `http://localhost:8080`
- Proxy `/hubs` -> `http://localhost:8080` with websocket enabled

### TypeScript Config
- Strict mode enabled
- Bundler module resolution
- `jsx: react-jsx`
- project references (`tsconfig.json` -> app + node configs)

### Tailwind + Global Style System
- Custom color palette and shadow system (`tailwind.config.ts`)
- Custom component utility classes in `src/index.css`
- Branded typography (`Outfit`, `Plus Jakarta Sans`)
- Layout classes for supervisor dashboards, KPI cards, graphs, event logs, banners

### Frontend Environment Files
`pbx-demo-frontend/.env.example`
- `VITE_API_BASE`
- STUN/TURN variables for browser WebRTC ICE config

`pbx-demo-frontend/.env`
- Contains local override
- Current file includes `VITE_API_BASE= https://localhost:7138/` (leading space). This is tolerated because `httpClient.ts` trims the value.

---

## 3.3 Frontend Architecture and Component Model

### 3.3.1 App Shell (`src/App.tsx`)
No router library is used. Navigation is state-driven:
- `activeView = 'login' | 'operator' | 'supervisor'`

Top-level responsibilities:
- Consume `useSoftphoneSession`
- Role-based module switching
- Session expiry status banner
- Render:
  - `LoginPage`
  - `SoftphonePage`
  - `SupervisorPage`

Architecture decisions:
- Manual workspace routing keeps dependencies small
- All session/call state centralized in hook
- `SupervisorPage` is isolated and only needs `accessToken`

### 3.3.2 State Management
Pattern:
- `useReducer` + local component state
- Reducer in `src/state/sessionStore.ts`

`SessionState` tracks:
- `auth`
- `snapshot`
- `browserCalls`
- `events`
- `bootstrapLoading`
- `busy`
- `errorMessage`

Reducer actions:
- auth/snapshot setters
- browser call snapshot/upsert
- busy/error flags
- event log push/clear

Sorting behavior:
- Browser calls are sorted by status priority then creation time.

### 3.3.3 Routing Logic
- No URL-based routing
- `App.tsx` chooses operator/supervisor based on auth role + `hasSoftphoneAccess`
- `SupervisorPage` contains its own internal section routing via `SupervisorSection` string union and `SupervisorNav`

---

## 3.4 Frontend Domain Types (`src/domain`)

### `src/domain/softphone.ts`
Defines all softphone-related contracts mirrored from backend:
- login request/response
- stored auth session
- session snapshot
- PBX call/device models
- browser call + WebRTC signal models
- SIP registration and call status models
- utility `isIncomingCall(...)`

### `src/domain/crm.ts`
Defines supervisor API request/response models mirrored from backend CRM DTOs:
- user/department CRUD contracts
- friendly name and parking contracts
- CDR history and analytics contracts

---

## 3.5 Frontend Service Layer (`src/services`)

### `httpClient.ts`
Purpose:
- Shared HTTP utility with auth header injection and error parsing

Key functions:
- `getApiBase()`
- `buildApiUrl(path)`
- `requestJson<T>()`
- `requestNoContent()`

Important behavior:
- Trims `VITE_API_BASE`
- Rewrites loopback API host to browser host for remote access cases
- Wraps failed responses in `ApiRequestError(statusCode, message, payload)`

### `softphoneApi.ts`
REST client for auth + softphone endpoints:
- `login`, `logout`
- `getSessionSnapshot`
- `getSipRegistrationConfig`
- `selectExtension`, `setActiveDevice`
- PBX call actions
- `openCallAudioDownlink`
- `uploadCallAudioUplink`

Notable:
- Audio endpoints use streaming `fetch`
- Uplink sets `duplex: "half"` (browser compatibility consideration)

### `crmApi.ts`
REST client for supervisor endpoints:
- user CRUD
- department CRUD
- friendly name validation/update
- parking tools
- version / 3CX utility reads
- call history + analytics

### `signalrClient.ts`
Builds SignalR connection to `/hubs/softphone`:
- optional force WebSocket mode (skip negotiation)
- automatic reconnect timings `[0, 1000, 3000, 6000, 10000]`

### `mediaDevices.ts`
Browser media support helpers:
- `getMediaDevices()`
- `requireMediaDevicesWithGetUserMedia()`
- secure-context/HTTPS guidance messages
- localhost exception handling

### `authStorage.ts`
Local storage wrapper:
- load/save/clear auth session
- validation of stored shape
- expiry check (30s buffer)

---

## 3.6 Custom Hooks and Runtime Orchestration

## 3.6.1 `useMicrophone.ts`
Simple standalone hook (not used by main app hook) for:
- microphone permission request
- mute state
- stream lifecycle cleanup

## 3.6.2 `useSoftphoneSession.ts` (Frontend Core)
This is the frontend orchestrator and most important client-side module.

### Responsibilities
- Auth session bootstrap and persistence
- REST snapshot loading
- SignalR connection lifecycle
- Browser-to-browser WebRTC signaling + peer connection management
- PBX call-control audio bridge (HTTP streaming PCM)
- Media device enumeration and switching
- Optional SIP.js registration path (present but disabled)
- Unified API exposed to `App.tsx`

### Major Subsystems

#### A. Auth and Bootstrap
- Loads auth from `authStorage`
- Validates expiry
- If user has softphone access:
  - refreshes snapshot
  - connects SignalR
  - sets voice bridge readiness
- If unauthorized, clears local state and forces re-login

#### B. SignalR Hub Integration
Binds server events:
- `SessionSnapshot`
- `SoftphoneEvent`
- `BrowserCallsSnapshot`
- `BrowserCallUpdated`
- `WebRtcSignal`

Client invokes hub methods:
- `PlaceBrowserCall`
- `AnswerBrowserCall`
- `RejectBrowserCall`
- `EndBrowserCall`
- `SendWebRtcSignal`
- `MarkCallConnected`

Fallback logic:
- If SignalR negotiation fetch fails, retries with forced WebSocket transport.

#### C. Browser WebRTC Calls (In-App Calls)
Data structures:
- `peerConnectionsRef`
- `pendingIceCandidatesRef`
- `pendingOffersRef`
- `browserCallsRef`

Flow:
- Ensure local mic stream
- Create `RTCPeerConnection` with ICE from env
- Send/receive `offer/answer/ice` via hub
- Mark connected via hub when RTCPeerConnection reaches `connected`
- End call on RTCPeerConnection `failed`
- Clean up audio and peer connection on end/close

#### D. PBX Call Control Audio Bridge (Current Primary Voice Path)
This is the active voice path because `DIRECT_SIP_JS_ENABLED = false`.

Bridge behavior:
- Detect connected PBX call from snapshot
- Open downlink binary audio stream (`GET /api/softphone/calls/{id}/audio`)
- Open uplink binary stream (`POST /api/softphone/calls/{id}/audio`)
- Convert PCM16 <-> Float32
- Downsample microphone audio to 8kHz
-  playback via `AudioContext` and `MediaStreamDestination`
- Attach audio to hidden `<audio>` element

Important implementation details:
- Uses `ScriptProcessorNode` (deprecated but supported in many browsers)
- Uses `AbortController` for uplink/downlink stop
- Starts/stops bridge automatically based on PBX connected call presence

#### E. SIP.js (Disabled Path)
Code exists for direct SIP registration:
- `connectSip()`
- `stopSip()`
- `answerSipCall`, `rejectSipCall`, `hangupSipCall` wrap PBX call-control actions
Current behavior:
- `DIRECT_SIP_JS_ENABLED=false`
- UI reuses SIP labels for call-control bridge status, which is functional but terminology is mixed.

#### F. Media Devices and Audio Routing
- Enumerates microphones/speakers
- Supports `setSinkId` when browser allows it
- Replaces sender audio track when mic changes
- Keeps local stream tracks muted/unmuted based on UI toggle

#### G. Error Handling
- Normalizes API and hub errors
- Detects unauthorized and resets session
- Emits client-side events into local event log for debugging UX

---

## 3.7 Frontend Pages and Components (File-by-File)

## 3.7.1 Shared Components

### `src/components/DialPad.tsx`
Purpose:
- Numeric keypad with digit labels
- Emits dial digits to parent
- Handles disabled state

### `src/components/CallList.tsx`
Purpose:
- Browser call list UI
- Renders per-call status, direction, and actions
- Supports answer/reject/end callbacks

### `src/components/IncomingCallModal.tsx`
Purpose:
- Floating incoming call toast for browser calls
- Answer/reject quick actions

---

## 3.7.2 `src/pages/LoginPage.tsx`
Purpose:
- Login form UI

Behavior:
- Local form state (`username`, `password`)
- Calls `onLogin(form)` on submit
- No advanced validation beyond `required`

---

## 3.7.3 `src/pages/SoftphonePage.tsx`
Purpose:
- Operator workspace UI combining:
  - PBX voice control panel
  - compact in-app dialer
  -  snapshot
  - CRM contact/request draft panels
  - browser call list
  - event stream

Important note:
- Contact profile and request registration sections are frontend-only preview state. They do not persist to backend.

Key UI sections:
- Status badges (user, extension, PBX, SignalR, mic, voice bridge)
- 3CX call control audio panel (`onAnswerSipCall`, `onRejectSipCall`, `onHangupSipCall`)
- Active PBX device selector
- Audio device selectors (mic/speaker)
- Compact softphone with keyboard dialing + `DialPad`
-  metrics derived from browser call list
- CRM contact quick-edit form (local only)
- Request registration form (local only)
- Live notes list (local only)
- Browser call list and event log
- Hidden audio elements for browser WebRTC and PBX audio bridge streams

Local state includes:
- dial string
- UI toggle for compact dialer
- contact/request drafts
- live notes
- transient form notice

---

## 3.7.4 `src/pages/SupervisorPage.tsx`
Purpose:
- Supervisor shell coordinating all business/admin pages

Responsibilities:
- Section routing via `SupervisorSection`
- Load all supervisor datasets:
  - users
  - departments
  - 3CX version
  - CDR history
  - analytics
- Own all CRUD actions and pass props to subpages
- Manage notices/errors/busy state

Key orchestration helpers:
- `refreshDirectory()`
- `refreshCdr()`
- `refreshAnalytics()`
- `refreshAll()`
- `runAction()` wrapper for busy/error handling
- `renderSection()` for section component dispatch

---

## 3.7.5 Supervisor Shared Utilities (`src/pages/supervisor/shared.ts`)
Purpose:
- Shared form state shapes and mappers
- UI constants (`ROLE_OPTIONS`)
- default department props
- formatter helpers (`formatUtc`, `formatDurationSeconds`)
- analytics helper functions (`computeQueueLoad`, `buildStatusChartData`)

---

## 3.7.6 Supervisor Navigation (`SupervisorNav.tsx`)
Purpose:
- Side navigation for supervisor sections

Groups:
- Insights
- User CRUD
- Department CRUD
- System (Shared Parking)

---

## 3.7.7 Dashboard and Analytics Pages
### `DashboardPage.tsx`
Purpose:
- KPI dashboard for call analytics
- Operator performance graph
- CDR status distribution graph

Uses:
- `CrmCallAnalyticsResponse`
- derived bar widths and chart datasets from parent

### `CdrPage.tsx`
Purpose:
- Paginated CDR history list
- Page size selector
- next/previous pagination
- renders status history timeline pills

---

## 3.7.8 User CRUD Pages
### `UserReadPage.tsx`
- List users
- Edit/delete actions
- New user entry point

### `UserCreatePage.tsx`
- Form for user creation
- Includes department role selection and chat-friendly-name fields

### `UserUpdatePage.tsx`
- Form for editing selected user
- Supports `newPassword`
- Toggles `callUsEnableChat` and `isActive`
- Requires user selection from read page

---

## 3.7.9 Department CRUD Pages
### `DepartmentReadPage.tsx`
- List departments and 3CX group mapping
- Edit/delete actions
- New department entry point

### `DepartmentCreatePage.tsx`
- Create department form
- Language/timezone/live-chat fields
- Basic route target + number inputs

### `DepartmentUpdatePage.tsx`
- Edit selected department form
- Same shape as create page
- Requires selection from read page

---

## 3.7.10 System Utility Page
### `ParkingToolsPage.tsx`
- Shared parking create/find/delete UI
- Group IDs input (comma-separated)
- Parking number lookup
- Displays found/created parking info string

---

## 3.8 Frontend/Backend Communication (Step-by-Step)

## 3.8.1 Login and Session Bootstrap
1. `LoginPage` submits credentials to `useSoftphoneSession.login`.
2. `softphoneApi.login` calls `POST /api/auth/login`.
3. Backend authenticates and returns JWT + session metadata.
4. Frontend stores session in `localStorage`.
5. Hook bootstrap runs:
   - `GET /api/softphone/session`
   - connect SignalR `/hubs/softphone`
6. Backend hub sends `SessionSnapshot` + `BrowserCallsSnapshot`.
7. UI renders operator/supervisor modules based on role and extension access.

## 3.8.2 Browser-to-Browser Call
1. Operator clicks “Call in-app”.
2. Hook invokes hub `PlaceBrowserCall(destination)`.
3. Backend `WebRtcCallManager` creates browser call record and emits updates.
4. Frontend creates `RTCPeerConnection`, local offer SDP.
5. Hook sends `SendWebRtcSignal(offer)` via hub.
6. Other client receives `WebRtcSignal`, stores pending offer.
7. On answer, callee client creates answer SDP and returns signal.
8. ICE candidates exchanged via hub.
9. When peer connection reaches connected, client invokes `MarkCallConnected`.
10. Backend updates browser call status and persists browser CDR updates.

## 3.8.3 PBX Call Control (3CX Call)
1. User selects extension and active device.
2. User clicks “Call via 3CX”.
3. Frontend `POST /api/softphone/calls/outgoing`.
4. Backend `CallManager` chooses `MakeCallFromDeviceAsync` or `MakeCallAsync`.
5. 3CX websocket emits participant/device events.
6. Backend updates session cache and publishes `SessionSnapshot` and `SoftphoneEvent`.
7. Frontend receives snapshot updates and detects connected PBX call.
8. Hook starts audio bridge:
   - GET downlink audio stream
   - POST uplink audio stream
   - PCM conversion/playback/mic capture
9. CDR updates are written locally by `CallCdrService`.

## 3.8.4 Supervisor CRUD and Analytics
1. `SupervisorPage` loads data through `crmApi`.
2. Backend `CrmController` delegates to `CrmManagementService` / `CallCdrService`.
3. `CrmManagementService` syncs local DB + 3CX XAPI as needed.
4. `CallCdrService` returns paged history and computed analytics.
5. UI updates lists/forms/KPI graphs.

---

## Part 4. Business Flow (End-to-End User Journey)

## 4.1 Operator Login to Call Handling
1. User logs in with CRM app credentials.
2. Backend validates local user and may create PBX softphone session.
3. JWT is issued and stored.
4. Frontend bootstraps session snapshot and SignalR.
5. Operator binds extension (owned extension only).
6. Operator selects active 3CX device or server route pseudo-device.
7. Operator enables microphone.
8. Incoming or outgoing call occurs.
9. Backend propagates 3CX events to frontend.
10. Frontend starts audio bridge for connected PBX call.
11. Events and calls are logged in local UI state.
12. CDR rows and status history are persisted in SQL Server.

## 4.2 Supervisor Admin Flow
1. Supervisor logs in.
2. `App.tsx` enables supervisor module.
3. `SupervisorPage` loads users/departments/CDR/analytics/version.
4. Supervisor creates/updates/deletes user or department.
5. Backend validates request and synchronizes 3CX XAPI.
6. Local DB is updated.
7. Supervisor UI refreshes and shows notices/errors.

## 4.3 Sequence-Style Request Travel (PBX Inbound Call)
1. External caller reaches 3CX.
2. 3CX routes to extension / route point.
3. 3CX emits websocket `Upset` event to backend call-control websocket.
4. `ThreeCxCallControlClient` raises `WsEventReceived`.
5. `CallManager.HandleWsEventAsync` fetches entity payload.
6. `CallManager.ProcessUpsertAsync` mutates session cache and builds snapshot.
7. Backend publishes `SoftphoneEvent` + `SessionSnapshot` to SignalR session group.
8. Frontend hook receives snapshot and updates UI.
9. Operator answers via 3CX call-control action endpoint.
10. Backend executes participant control with fallback heuristics if needed.
11. Connected call triggers audio bridge start and CDR updates.

---

## Part 5. How To Run the Project (Setup Guide)

## 5.1 Prerequisites
- .NET SDK 8.x
- Node.js 18+ (recommended 20+)
- npm
- SQL Server instance (or LocalDB for dev)
- 3CX PBX with API app credentials and accessible call-control/XAPI endpoints
- HTTPS-capable browser for microphone/WebRTC (or localhost)

## 5.2 Required Software
- .NET CLI
- Node/npm
- SQL Server / SQL Server Express / LocalDB
- (Optional) `dotnet-ef` tool for migrations management

## 5.3 Environment Setup

### Backend configuration
Edit:
- `pbx-demo-backend/appsettings.json`
- `pbx-demo-backend/softphone.config.json`

Do not use committed secrets in production. Rotate and move to secret storage.

### Frontend configuration
Edit:
- `pbx-demo-frontend/.env`

Recommended local options:
- Use Vite proxy with empty `VITE_API_BASE`
- Or set `VITE_API_BASE` to backend origin

## 5.4 Database Setup
Current app behavior:
- Backend auto-creates DB schema on startup using `EnsureCreated()`
- Also patches CDR tables via raw SQL (`DatabaseBootstrapper.EnsureCallCdrSchemaAsync`)

Connection string source:
- `ConnectionStrings:SoftphoneDb` in `pbx-demo-backend/appsettings.json`

## 5.5 Migrations
Migrations exist in `pbx-demo-backend/Migrations`, but runtime does not call `Database.Migrate()`.

If you choose migration-driven setup manually:
```powershell
dotnet tool install --global dotnet-ef
dotnet ef database update --project pbx-demo-backend\pbx-demo-backend.csproj
```

Recommendation:
- Standardize on either migrations or bootstrap SQL before production.

## 5.6 Seeding Data
No separate seed command.
Seeding occurs automatically in `DatabaseBootstrapper.InitializeAsync()`:
- Seeds users from `Softphone:Users` in config if `Users` table is empty
- Hashes passwords with PBKDF2
- Creates fallback supervisor if no valid configured users exist

## 5.7 Running Backend (Development)
Option A, align with Vite proxy default (`8080`):
```powershell
$env:ASPNETCORE_URLS="http://localhost:8080"
dotnet run --project pbx-demo-backend\pbx-demo-backend.csproj
```

Option B, use launch settings default ports:
```powershell
dotnet run --project pbx-demo-backend\pbx-demo-backend.csproj
```
Then set frontend `VITE_API_BASE` to `https://localhost:7138` or `http://localhost:5201`.

## 5.8 Running Frontend (Development)
```powershell
cd pbx-demo-frontend
npm install
npm run start
```

Frontend URL (default):
- `http://localhost:8081`

## 5.9 Running in Production Mode
Backend:
```powershell
dotnet publish pbx-demo-backend\pbx-demo-backend.csproj -c Release -o .\artifacts\backend
```

Frontend:
```powershell
cd pbx-demo-frontend
npm ci
npm run build
```

Serve frontend static assets using:
- Nginx / Apache / IIS / CDN
and route API/hub traffic to backend.

## 5.10 Docker Setup
No `Dockerfile` / `docker-compose.yml` present in repo.

## 5.11 CI/CD
No `.github/workflows` (or other CI config) present in repo.

---

## Part 6. Deployment Guide

## 6.1 Environment Configuration
Move these to secure configuration providers:
- 3CX App ID / App Secret
- SQL Server credentials
- JWT signing key
- SIP passwords per user

Recommended sources:
- environment variables
- Azure Key Vault / AWS Secrets Manager / HashiCorp Vault
- platform secret mounts

## 6.2 Build Steps
1. Build backend (`dotnet publish`)
2. Build frontend (`npm ci && npm run build`)
3. Deploy backend service
4. Deploy frontend static assets
5. Configure reverse proxy for:
   - `/api/*`
   - `/hubs/*` (websockets)
6. Configure TLS certificates
7. Validate 3CX outbound connectivity from backend host
8. Validate frontend microphone and websocket access from client network

## 6.3 Production Optimizations
- Use HTTPS everywhere
- Disable SignalR detailed errors
- Restrict CORS origins
- Increase HTTP client resilience (timeouts/retries policy)
- Add health endpoints
- Add structured logging sink
- Replace bootstrap `EnsureCreated` with `Migrate` strategy
- Cache or page large CRM lists from 3CX if scale grows

## 6.4 Scaling Considerations
Current design is stateful and single-node oriented:
- `SessionRegistry` and `WebRtcCallManager` keep in-memory state
- SignalR group membership and session state are local to process
- `CallManager` stores PBX session clients per app instance

If scaling horizontally:
- Use sticky sessions at load balancer, or
- Externalize session/call state (Redis/distributed state), and
- Rework PBX websocket ownership model to avoid duplicate subscriptions

## 6.5 Security Hardening Checklist
- Rotate all committed secrets immediately
- Strong JWT signing key via secret store
- Restrict CORS
- Enable HTTPS metadata in JWT in production
- Disable demo accounts and plaintext fallback password compatibility
- Audit logs for auth and supervisor actions
- Limit supervisor endpoints with additional controls (IP/VPN/MFA)
- Validate/sanitize free-form inputs consistently

## 6.6 Monitoring Recommendations
Add:
- Backend health checks (`/health`)
- Metrics:
  - active sessions
  - hub connections
  - 3CX websocket reconnect count
  - CDR write failures
  - API latency by route
- Structured logs with correlation id / trace id
- Frontend telemetry for:
  - SignalR reconnects
  - audio bridge failures
  - browser WebRTC negotiation failures

---

## Part 7. Troubleshooting Guide

## 7.1 Build Errors
### Frontend cannot compile due TS type issues
- Check strict mode violations in `tsconfig.app.json`
- Reinstall deps: `npm ci`
- Delete `node_modules` and retry (local only)

### Backend build succeeds but `/swagger` returns 404
Cause:
- `Swashbuckle.AspNetCore` package exists, but `Program.cs` does not call `AddSwaggerGen()` / `UseSwagger()` / `UseSwaggerUI()`.

## 7.2 Database Errors
### Connection failures at startup
- Verify `ConnectionStrings:SoftphoneDb` in `pbx-demo-backend/appsettings.json`
- Confirm SQL Server reachable from backend host
- Confirm credentials and TLS trust settings

### Schema confusion after migration/manual changes
Cause:
- runtime uses `EnsureCreated` + manual SQL patch, not `Migrate`
Fix:
- choose a single schema management strategy and reset/reconcile DB

## 7.3 Authentication Issues
### Login returns 401
- User not seeded or inactive
- Wrong password
- `PasswordHash` format mismatch (legacy/plaintext vs pbkdf2)
- DB contains stale data conflicting with config expectations

### Frequent re-login prompts
- JWT expired (`TokenLifetimeMinutes`)
- frontend `authStorage` expires session with 30s buffer
- system clock skew issues across client/server

## 7.4 API Failures
### Supervisor endpoints return 403
- JWT role is not `Supervisor`
- verify user `Role` in DB and login response

### Softphone command returns 422/403 from upstream
- 3CX call-control limitation on selected participant leg
- ensure route point (`ControlDn`) is configured for inbound answer reliability
- backend has fallback logic, but PBX permissions/topology still matter

## 7.5 SignalR / Realtime Problems
### Frontend says hub unreachable
- Vite proxy points to `http://localhost:8080`
- backend may actually be on `5201/7138`
- set `ASPNETCORE_URLS` or `VITE_API_BASE`

### SignalR negotiation fetch fails remotely
`useSoftphoneSession` retries forced WebSocket. If still failing:
- reverse proxy may block websocket upgrade
- CORS/TLS/origin mismatch
- wrong hub path routing

## 7.6 Audio / Microphone Issues
### Mic permission denied
- non-HTTPS origin (except localhost)
- browser permission blocked
- use secure context and modern browser

### Speaker device selection not working
- browser lacks `HTMLAudioElement.setSinkId`
- UI correctly warns and falls back to default output device

### PBX audio bridge fails
- `/audio` endpoints unavailable or participant invalid
- browser lacks streaming upload support for `duplex: "half"`
- backend/3CX stream endpoint interruption

## 7.7 Configuration Problems
### `VITE_API_BASE` has extra spaces
- frontend `httpClient` trims it, so leading/trailing spaces are tolerated
- still clean it up to avoid confusion

### `.env.example` backend port not applied
- project does not load `.env` automatically
- set `$env:ASPNETCORE_URLS` explicitly before `dotnet run`

---

## Part 8. Developer Guide

## 8.1 Local Development Workflow
1. Start backend with explicit port alignment.
2. Start frontend Vite dev server.
3. Login using seeded config users.
4. Validate operator and supervisor module access paths.
5. Test:
   - browser call
   - PBX call control
   - supervisor CRUD
   - CDR refresh

## 8.2 Adding a New Backend Endpoint
1. Add request/response DTO in `Domain/Models.cs` or `Domain/CrmContracts.cs`
2. Implement service method in `Services/*`
3. Add controller action
4. Reuse `AppException` types for domain errors
5. Mirror contract in frontend `src/domain/*.ts`
6. Add frontend API wrapper in `src/services/*Api.ts`

## 8.3 Adding a New Supervisor Page
1. Add page component under `src/pages/supervisor/`
2. Update `SupervisorSection` in `shared.ts`
3. Add nav item in `SupervisorNav.tsx`
4. Add state and handlers in `SupervisorPage.tsx`
5. Add API client calls in `crmApi.ts` if needed

## 8.4 Concurrency and Thread Safety Notes
Backend:
- Session mutation guarded by `session.Gate`
- global registries use `ConcurrentDictionary`
- do not mutate `SoftphoneSession` state outside gate
- keep async callbacks resilient to disposed sessions

## 8.5 Data Contract Discipline
- Backend DTOs and frontend TS interfaces are tightly mirrored
- Any backend contract change should be propagated to:
  - `src/domain/softphone.ts` or `src/domain/crm.ts`
  - UI pages/components consuming the payload

## 8.6 Known Architectural Tradeoffs
- In-memory session state limits horizontal scale
- `EnsureCreated` + migrations mixed strategy
- Supervisor page centralizes many concerns in one component
- `useSoftphoneSession` is very large and should eventually be split by subsystem
- Call-control audio bridge uses deprecated `ScriptProcessorNode`

---

## Part 9. Contribution Guide

## 9.1 Contribution Principles
- Preserve contract compatibility unless versioning is planned
- Do not commit secrets
- Keep backend errors using `AppException` types for consistency
- Keep frontend API wrappers typed and centralized

## 9.2 Coding Standards (Recommended)
- Backend:
  - keep service orchestration in `Services`
  - controllers thin
  - use `CancellationToken`
  - log failures with contextual IDs
- Frontend:
  - keep domain types in `src/domain`
  - keep fetch logic in `src/services`
  - prefer small presentational components for new UI sections
  - avoid duplicating API error parsing logic

## 9.3 PR Checklist
- Backend builds locally
- Frontend builds locally
- Contracts updated on both sides
- No secrets added to repo
- New endpoints documented
- Supervisor/Operator flows manually smoke tested if impacted

## 9.4 Recommended Future Refactors for Contributors
- Split `useSoftphoneSession` into:
  - auth/bootstrap
  - signalr
  - browser-webrtc
  - pbx-audio-bridge
  - media devices
- Split `SupervisorPage` into container hooks + section containers
- Standardize DB migration strategy
- Add automated tests and CI workflow

---

## Part 10. File-by-File Inventory (Authored + Generated Catalog)

## 10.1 Top Level
- `README.md`: existing project README (outdated path references to `backend/`/`frontend/`)
- `.github/`: no workflow files detected

## 10.2 Backend Authored Files
- `pbx-demo-backend/Program.cs`: app startup, DI, auth, middleware, endpoint mapping
- `pbx-demo-backend/pbx-demo-backend.csproj`: backend package dependencies and target framework
- `pbx-demo-backend/pbx-demo-backend.slnx`: solution wrapper
- `pbx-demo-backend/pbx-demo-backend.csproj.user`: local Visual Studio debug profile selection
- `pbx-demo-backend/appsettings.json`: app + DB config (contains sensitive values in repo)
- `pbx-demo-backend/softphone.config.json`: 3CX + SIP config (contains sensitive values in repo)
- `pbx-demo-backend/.env.example`: example `ASPNETCORE_URLS`
- `pbx-demo-backend/.gitignore`: backend ignore rules
- `pbx-demo-backend/BACKEND_DOCS.md`: existing backend deep docs
- `pbx-demo-backend/Controllers/*.cs`: REST API controllers
- `pbx-demo-backend/Hubs/SoftphoneHub.cs`: SignalR hub + client contract
- `pbx-demo-backend/Domain/*.cs`: constants, errors, entities, DTOs, runtime models
- `pbx-demo-backend/Infrastructure/*.cs`: middleware/helpers/db context
- `pbx-demo-backend/Services/*.cs`: all business logic and integrations
- `pbx-demo-backend/Properties/launchSettings.json`: local dev launch profiles
- `pbx-demo-backend/Samples/fullinfo.sample.json`: sample 3CX callcontrol topology payload
- `pbx-demo-backend/Samples/websocket.sample.json`: sample 3CX websocket event payload

## 10.3 Backend Generated/Tool-Managed Files
- `pbx-demo-backend/Migrations/*Designer.cs`: EF auto-generated migration target models
- `pbx-demo-backend/Migrations/SoftphoneDbContextModelSnapshot.cs`: EF snapshot
- `pbx-demo-backend/bin/**`: build outputs
- `pbx-demo-backend/obj/**`: intermediate outputs

## 10.4 Frontend Authored Files
- `pbx-demo-frontend/package.json`: scripts and dependency manifest
- `pbx-demo-frontend/package-lock.json`: npm lockfile (tool-managed but important)
- `pbx-demo-frontend/vite.config.ts`: dev server + proxy config
- `pbx-demo-frontend/tailwind.config.ts`: theme tokens and animations
- `pbx-demo-frontend/postcss.config.cjs`: Tailwind + Autoprefixer setup
- `pbx-demo-frontend/tsconfig*.json`: TS configs
- `pbx-demo-frontend/.env`, `.env.example`: frontend runtime config
- `pbx-demo-frontend/.gitignore`: frontend ignore rules
- `pbx-demo-frontend/index.html`: Vite HTML entry
- `pbx-demo-frontend/src/main.tsx`: React bootstrap
- `pbx-demo-frontend/src/App.tsx`: app shell, module switching
- `pbx-demo-frontend/src/index.css`: global styles + component utility classes
- `pbx-demo-frontend/src/domain/*.ts`: shared typed contracts
- `pbx-demo-frontend/src/services/*.ts`: API, SignalR, media, auth storage helpers
- `pbx-demo-frontend/src/state/sessionStore.ts`: reducer and state model
- `pbx-demo-frontend/src/hooks/useMicrophone.ts`: simple mic hook
- `pbx-demo-frontend/src/hooks/useSoftphoneSession.ts`: primary runtime orchestration hook
- `pbx-demo-frontend/src/components/*.tsx`: reusable call UI widgets
- `pbx-demo-frontend/src/pages/*.tsx`: top-level pages
- `pbx-demo-frontend/src/pages/supervisor/*.tsx`: supervisor feature pages and helpers

## 10.5 Frontend Generated Files
- `pbx-demo-frontend/tsconfig.app.tsbuildinfo`, `tsconfig.node.tsbuildinfo`: TypeScript incremental build metadata
- `pbx-demo-frontend/dist/**`: Vite build output
- `pbx-demo-frontend/node_modules/**`: dependencies

---

# Production-Ready `README.md` (Generated)

```md
# PBX Demo Command Center (3CX + CRM + WebRTC)

Unified operator and supervisor workspace for:
- Browser-to-browser WebRTC calls (SignalR signaling)
- 3CX PBX call control and extension monitoring
- Supervisor CRM administration (users, departments, parking)
- Local call history (CDR) and analytics dashboards

## Table of Contents
- [Features](#features)
- [Architecture](#architecture)
- [Tech Stack](#tech-stack)
- [Project Structure](#project-structure)
- [Installation](#installation)
- [Configuration](#configuration)
- [Usage](#usage)
- [API Documentation](#api-documentation)
- [Deployment](#deployment)
- [Troubleshooting](#troubleshooting)
- [Screenshots](#screenshots)
- [Contribution Guide](#contribution-guide)
- [License](#license)

## Features

### Operator
- JWT login and session persistence
- Real-time PBX session snapshot via SignalR
- 3CX call control actions (outgoing, answer, reject, end, transfer)
- PBX audio bridge via backend participant audio streaming
- Browser-to-browser WebRTC calling inside the app
- Device selection (PBX device, microphone, speaker)
- Live event stream UI

### Supervisor
- User CRUD synced with 3CX
- Department CRUD synced with 3CX groups
- Shared parking create/find/delete tools
- Call history (CDR) page with paging and status timeline
- Call analytics KPI dashboard and charts

## Architecture

```text
React (Vite) frontend
  -> REST (/api/*)
  -> SignalR (/hubs/softphone)
  -> audio stream endpoints (/api/softphone/calls/{id}/audio)
ASP.NET Core backend
  -> SQL Server (local CRM + CDR)
  -> 3CX XAPI (/xapi/v1/*)
  -> 3CX Call Control API (/callcontrol, /callcontrol/ws)
```

## Tech Stack

### Backend
- .NET 8
- ASP.NET Core Web API
- SignalR
- EF Core 8 + SQL Server
- JWT Bearer Authentication

### Frontend
- React 18 + TypeScript
- Vite
- Tailwind CSS
- Microsoft SignalR client
- Browser WebRTC APIs
- SIP.js (code path present, currently disabled by feature flag)

## Project Structure

- `pbx-demo-backend/` ASP.NET Core backend
- `pbx-demo-frontend/` React frontend
- `pbx-demo-backend/BACKEND_DOCS.md` existing backend deep-dive notes
- `README.md` project overview

## Installation

## Prerequisites
- .NET SDK 8.x
- Node.js 18+ (recommended 20+)
- npm
- SQL Server / LocalDB
- 3CX PBX API app credentials

## 1) Backend setup
Edit:
- `pbx-demo-backend/appsettings.json`
- `pbx-demo-backend/softphone.config.json`

Important:
- Replace demo secrets and passwords.
- Do not use committed values in production.

## 2) Frontend setup
Edit:
- `pbx-demo-frontend/.env`

If using Vite proxy, you can keep `VITE_API_BASE` empty.

## Configuration

## Backend configuration files

### `pbx-demo-backend/appsettings.json`
Contains:
- logging
- SQL connection string
- `Softphone` config (JWT + reconnect + seed users)

### `pbx-demo-backend/softphone.config.json`
Contains:
- 3CX PBX base URL
- 3CX app id/secret
- SIP/WebRTC config (optional direct SIP path)

## Frontend environment variables

### `pbx-demo-frontend/.env`
- `VITE_API_BASE`
- `VITE_STUN_SERVERS`
- `VITE_TURN_SERVERS`
- `VITE_TURN_USERNAME`
- `VITE_TURN_PASSWORD`

## Local port alignment (important)
Frontend Vite proxy (`vite.config.ts`) points to `http://localhost:8080`.

Either:
- run backend on `8080`, or
- set `VITE_API_BASE` to backend actual port (`https://localhost:7138` / `http://localhost:5201`), or
- update the Vite proxy target

## Running the Project

## Start backend (recommended for Vite proxy)
```powershell
$env:ASPNETCORE_URLS="http://localhost:8080"
dotnet run --project pbx-demo-backend\pbx-demo-backend.csproj
```

## Start frontend
```powershell
cd pbx-demo-frontend
npm install
npm run start
```

Frontend will run on `http://localhost:8081`.

## Usage

1. Open the frontend in browser.
2. Login with a user defined in `Softphone:Users` (seeded to DB on first run).
3. If the account has an extension, bind/select the extension.
4. Select an active 3CX device (or server-route pseudo device).
5. Enable microphone.
6. Use:
   - **Call in-app** for browser-to-browser WebRTC calls
   - **Call via 3CX** for PBX calls
7. If user role is `Supervisor`, switch to **Supervisor Panel** for CRM/CDR pages.

## API Documentation

API reference is available in the project technical manual and backend source:
- Controllers: `pbx-demo-backend/Controllers/*.cs`
- Hub contract: `pbx-demo-backend/Hubs/SoftphoneHub.cs`

Quick endpoints:
- `POST /api/auth/login`
- `POST /api/auth/logout`
- `GET /api/softphone/session`
- `POST /api/softphone/calls/outgoing`
- `POST /api/softphone/calls/{participantId}/answer`
- `GET /api/crm/users` (Supervisor)
- `GET /api/crm/calls/history` (Supervisor)
- `GET /api/crm/calls/analytics` (Supervisor)

Note:
- Swashbuckle package is installed, but Swagger is not enabled in `Program.cs` by default.

## Deployment

### Backend
```powershell
dotnet publish pbx-demo-backend\pbx-demo-backend.csproj -c Release -o .\artifacts\backend
```

### Frontend
```powershell
cd pbx-demo-frontend
npm ci
npm run build
```

### Production recommendations
- Use HTTPS
- Restrict CORS
- Move secrets to environment/vault
- Add health checks and monitoring
- Standardize DB migration strategy (`Migrate` vs `EnsureCreated`)

## Troubleshooting

### Frontend cannot reach backend
Check port alignment:
- Vite proxy expects `http://localhost:8080`
- backend launch settings default to `5201/7138`

### `/swagger` not available
Swagger package exists but middleware/services are not enabled in `Program.cs`.

### PBX answer fails with upstream validation errors
Use a configured route point (`ControlDn`) for better inbound answer reliability with 3CX call control.

### Microphone access denied
Use HTTPS (or localhost) and grant browser microphone permission.

## Screenshots

Add screenshots here after deployment validation:

- `docs/screenshots/login.png` (Login screen)
- `docs/screenshots/operator-panel.png` (Operator workspace)
- `docs/screenshots/supervisor-dashboard.png` (Supervisor dashboard)
- `docs/screenshots/cdr-page.png` (CDR page)

Example placeholders:
```md
![Login Screen](docs/screenshots/login.png)
![Operator Panel](docs/screenshots/operator-panel.png)
![Supervisor Dashboard](docs/screenshots/supervisor-dashboard.png)
![CDR Page](docs/screenshots/cdr-page.png)
```

## Contribution Guide

1. Create a feature branch.
2. Do not commit secrets or real credentials.
3. Keep backend DTOs and frontend TS contracts in sync.
4. Update docs for new APIs/pages.
5. Validate operator and supervisor flows locally before PR.

## License

No license file is currently present in this repository.

Add a `LICENSE` file (recommended: MIT or internal proprietary license) and update this section accordingly.
```

## Notes for Handover Team (High Priority)
1. Rotate all secrets currently committed in `pbx-demo-backend/appsettings.json` and `pbx-demo-backend/softphone.config.json`.
2. Fix local runtime port alignment between `pbx-demo-frontend/vite.config.ts` and backend launch defaults.
3. Decide on one database schema strategy (`EnsureCreated` vs EF migrations) before production.
4. Consider splitting `pbx-demo-frontend/src/hooks/useSoftphoneSession.ts` and `pbx-demo-frontend/src/pages/SupervisorPage.tsx` for maintainability.
5. Add CI/CD and automated tests before enterprise rollout.

If you want, I can also generate this as actual files (`docs/TECHNICAL_MANUAL.md`, updated `README.md`, and `docs/API_REFERENCE.md`) once write access is available.