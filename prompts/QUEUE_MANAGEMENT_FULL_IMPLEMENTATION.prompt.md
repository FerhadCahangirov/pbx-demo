# Queue Management Full Implementation Prompt (XAPI-Grounded)

## Role

You are a principal .NET architect and senior React TypeScript engineer.

You previously analyzed the 3CX XAPI OpenAPI file (`ferhad-xapi-1.0.0-resolved.json`) and extracted queue-related endpoints and DTOs.

Now generate a production-ready Queue Management module for:

- Backend: ASP.NET Core (.NET 8)
- Frontend: React + TypeScript
- Database: SQL Server
- Realtime: SignalR
- Architecture: Clean Architecture (Domain / Application / Infrastructure / Persistence / WebApi)

Apply `XAPI_STRICT_ENFORCEMENT_LAYER.prompt.md` rules.

## Objective

Generate working code for:

- queue CRUD via documented XAPI `/Queues*` endpoints
- queue call lifecycle tracking
- event-driven synchronization (CRM as synchronized copy of 3CX)
- SignalR realtime monitoring
- queue analytics engine
- React frontend queue module
- scalable production architecture

This must be enterprise-grade and XAPI-accurate.

## Critical XAPI Constraints (Must Be Respected)

1. `3CX XAPI` is the external source of truth.
2. Queue CRUD is documented via `/Queues`, `/Queues({Id})`, and queue helper functions.
3. Queue agents/managers have documented list endpoints only:
   - `GET /Queues({Id})/Agents`
   - `GET /Queues({Id})/Managers`
4. Queue assignment writes are not documented as separate endpoints; if modifying membership, do so through `Pbx.Queue` update payload only if OpenAPI-backed.
5. `/ActiveCalls` exists but returns minimal `Pbx.ActiveCall` and does not fully expose queue participant detail.
6. Realtime WebSocket endpoint/event schemas are not documented in the OpenAPI.
   - Do not invent event names/payloads
   - If you generate an external realtime adapter, mark it as `UNDEFINED EVENT SCHEMA IN OPENAPI`
7. `Users` endpoints represent extensions/agents in this spec.
8. Use `/CallHistoryView` and `/ReportCallLogData*` for reconciliation/finalization.

## External XAPI Endpoints You Are Allowed To Implement (Queue Scope)

Queue CRUD and settings:

- `GET /Queues`
- `POST /Queues`
- `GET /Queues({Id})`
- `PATCH /Queues({Id})`
- `DELETE /Queues({Id})`
- `GET /Queues({Id})/Agents`
- `GET /Queues({Id})/Managers`
- `POST /Queues({Id})/Pbx.ResetQueueStatistics`
- `GET /Queues/Pbx.GetByNumber(number={number})`
- `GET /Queues/Pbx.GetFirstAvailableQueueNumber()`

Runtime and lifecycle inputs:

- `GET /ActiveCalls`
- `POST /ActiveCalls({Id})/Pbx.DropCall`
- `GET /CallHistoryView`
- `GET /CallHistoryView/Pbx.DownloadCallHistory()`
- `GET /ReportCallLogData/Pbx.GetCallLogData(...)`
- `GET /ReportCallLogData/Pbx.DownloadCallLog(...)`
- `GET /ReportCallLogData/Pbx.GetCallQualityReport(...)`
- `GET /ReportCallLogData/Pbx.GetOldCallLogData(...)`
- `GET /ReportCallLogData/Pbx.GetOldCallQualityReport(...)`

Extensions/agents:

- `GET /Users`
- `GET /Users({Id})`
- `GET /Users/Pbx.GetByNumber(number={number})`
- `GET /Users/Pbx.ExportExtensions()`
- optional admin actions only if explicitly requested and documented

Logs/diagnostics (supporting):

- `GET /ActivityLog/Pbx.GetFilter()`
- `GET /ActivityLog/Pbx.GetLogs(...)`
- `GET /EventLogs`

Queue analytics report endpoints (documented):

- `/ReportAbandonedQueueCalls/*`
- `/ReportAgentLoginHistory/*`
- `/ReportAgentsInQueueStatistics/*`
- `/ReportAverageQueueWaitingTime/*`
- `/ReportBreachesSla/*`
- `/ReportDetailedQueueStatistics/*`
- `/ReportQueueAnsweredCallsByWaitTime/*`
- `/ReportQueueAnUnCalls/*`
- `/ReportQueueCallbacks/*`
- `/ReportQueueFailedCallbacks/*`
- `/ReportQueuePerformanceOverview/*`
- `/ReportQueuePerformanceTotals/*`
- `/ReportStatisticSla/*`
- `/ReportTeamQueueGeneralStatistics/*`
- `/ReportCallDistribution/*`
- `/ReportUserActivity/*`

## Required Internal Module Design (Generate Code)

Generate the module using these internal components (names should match unless conflict is explicitly reported):

Backend domain/persistence entities:

- `QueueEntity`
- `QueueSettingsEntity`
- `QueueAgentEntity`
- `QueueScheduleEntity`
- `QueueWebhookMappingEntity`
- `ExtensionEntity` (recommended/required for agent FK consistency)
- `QueueCallEntity`
- `QueueCallEventEntity`
- `QueueCallHistoryEntity`
- `QueueAgentActivityEntity`
- `QueueWaitingSnapshotEntity`
- pre-aggregation entities (hour/day buckets)
- outbox/inbox/checkpoint entities as needed

Application contracts and services:

- `IQueueXapiClient` / `QueueXapiClient`
- `IQueueService` / `QueueService`
- `IQueueEventProcessor` / `QueueEventProcessor`
- `QueueCallStateMachine`
- `QueueCallLifecycleManager`
- `QueueLiveStateService`
- `QueueLiveSnapshotBuilder`
- `QueueAnalyticsService`
- `QueueKpiCalculator`
- `QueueTimeSeriesAnalyzer`
- `QueueComparisonEngine`

Web/API and realtime:

- `QueueController`
- `QueueHub`
- `IQueueHubClient`

Frontend module:

- `modules/queues/pages/*`
- `modules/queues/components/*`
- `modules/queues/hooks/*`
- `modules/queues/services/*`
- `modules/queues/models/*`
- `modules/queues/store/*`
- typed SignalR client

## Backend Implementation Requirements

### 1. Project Structure

Generate a Clean Architecture structure such as:

```text
src/
  QueueManagement.Domain/
  QueueManagement.Application/
  QueueManagement.Infrastructure/
  QueueManagement.Persistence/
  QueueManagement.WebApi/
```

### 2. Domain Layer

Generate:

- strong-typed entities
- aggregate roots
- value objects where useful
- domain events
- call lifecycle state machine
- invariants and transition validation

Lifecycle states to support internally:

- `EnteredQueue`
- `Waiting`
- `Ringing`
- `Answered`
- `Transferred`
- `Completed`
- `Missed`
- `Abandoned`

Must support:

- invalid transition prevention
- concurrency safety
- out-of-order events (mark/reconcile)

### 3. Application Layer

Generate:

- interfaces
- DTOs
- mappers
- services/orchestrators
- validation
- transaction boundaries
- idempotency safeguards
- structured logging

### 4. Infrastructure Layer

Generate:

- typed XAPI `HttpClient`
- OAuth2 client-credentials token handling
- Polly resilience
- OData query builder
- error parsing
- reconciliation workers
- polling worker(s) for `/ActiveCalls`
- optional external realtime adapter interface and placeholder implementation

Rules:

- Do not hardcode undocumented WebSocket event names
- If external realtime adapter is included, clearly label schema as unverified

### 5. Persistence Layer

Generate:

- EF Core `DbContext`
- fluent configurations
- indexes
- query filters
- migration-ready setup
- pre-aggregated tables for analytics
- partitioning notes/comments for SQL Server

### 6. SignalR Layer

Generate:

- typed hub interface
- group management (`queue:{id}`, dashboard groups)
- live snapshot broadcasting
- push events:
  - `QueueWaitingListUpdated`
  - `QueueActiveCallsUpdated`
  - `QueueAgentStatusChanged`
  - `QueueStatsUpdated`

### 7. Analytics Engine

Generate:

- KPI calculations
- time bucket aggregation
- SLA compliance calculations
- abandonment rate
- average wait/talk time
- peak hour detection
- agent ranking
- multi-queue comparison
- congestion detection
- optimized LINQ and SQL query examples
- caching strategy

Use mathematically consistent definitions (see KPI prompt if provided).

### 8. Runtime Sync and Reconciliation

Generate code for:

- polling snapshot ingestion (`/ActiveCalls`)
- event journal persistence
- idempotent event processing
- queue-call projection updates
- history reconciliation (`/CallHistoryView`)
- call-log reconciliation (`/ReportCallLogData*`)
- outbox-based SignalR publication

## Frontend Implementation Requirements (React + TypeScript)

### 1. Structure

Generate:

```text
src/modules/queues/
  pages/
  components/
  hooks/
  services/
  models/
  store/
```

Use:

- React 18
- TypeScript strict mode
- Axios
- Zustand or Redux Toolkit
- SignalR client

### 2. Pages

Generate:

- Queue List
- Create/Edit Queue
- Queue Details
- Live Monitoring Dashboard
- Queue Analytics Dashboard
- Queue Call History
- Agent Activity Page
- SLA Dashboard

Each page should include:

- filtering
- pagination
- sorting
- loading/error states
- realtime updates where applicable

### 3. SignalR Client

Generate:

- connection builder
- auto reconnect
- typed event handlers
- store integration

### 4. Charts/UI

Use:

- Recharts or Chart.js

Include:

- KPI cards
- time-series charts
- distribution/heatmap-like views

## End-To-End Flows To Implement

Generate code for:

- create queue -> persist in CRM DB mirror -> push to XAPI -> read-after-write sync
- call enters queue -> captured (poll/adapter) -> DB updated -> SignalR push
- agent answers -> lifecycle + agent activity + stats update
- call completes/abandons -> reconciliation finalizes durations/disposition
- analytics dashboard refreshes from aggregates/cache

## Non-Functional Requirements

Include:

- structured logging
- global exception middleware
- FluentValidation
- rate limiting
- health checks
- Swagger
- unit test examples (xUnit)
- integration test examples

## Performance Targets

Design for:

- 1000+ concurrent calls
- 100+ queues
- 500+ agents
- realtime dashboards without lag

Use:

- batched updates
- background workers
- channel-based processing
- caching
- outbox pattern

## Output Order

Generate in this order:

1. Full solution folder structure
2. Domain code
3. Application code
4. Infrastructure code
5. Persistence code
6. Web API controllers
7. SignalR hub
8. Analytics engine
9. React frontend code
10. SignalR client
11. End-to-end flow wiring

## Hard Rules

- No pseudocode
- Generate real C# and TypeScript
- Use production patterns
- Strong typing everywhere
- Maintain backend/frontend contract consistency
- Do not fabricate undocumented XAPI integrations

If an external capability is missing from OpenAPI, output:

```text
Feature requires extension beyond documented XAPI.
Manual verification required.
```
