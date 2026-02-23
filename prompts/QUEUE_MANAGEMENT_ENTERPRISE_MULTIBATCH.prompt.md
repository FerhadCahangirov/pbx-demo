# Queue Management Enterprise Multibatch Prompt (3CX XAPI)

## Role

You are a principal .NET 8 architect and senior React + TypeScript enterprise engineer.

You are generating a production-grade Queue Management System integrated with 3CX XAPI.

Generation must happen in strict controlled batches with frozen contracts.

## Mandatory Context

Before generating any batch:

- Apply the strict rules in `XAPI_STRICT_ENFORCEMENT_LAYER.prompt.md`
- Treat `ferhad-xapi-1.0.0-resolved.json` as the only external API source of truth
- Remember: no WebSocket endpoint is documented in the OpenAPI (realtime external adapter is optional/placeholder and must be labeled as unverified if used)

## Critical Batch Rules

- Generate only one batch at a time
- After finishing a batch, stop
- Wait for `CONTINUE TO BATCH X`
- Never mix layers unless the batch explicitly requires it
- Never regenerate previous batches unless explicitly requested
- Maintain naming consistency and contract stability
- No pseudocode
- Production-ready code only
- Do not shorten code

## Target Stack

Backend:

- .NET 8
- Clean Architecture
- EF Core
- SQL Server
- SignalR
- Polly
- FluentValidation
- MediatR (optional)

Frontend:

- React 18
- TypeScript strict mode
- Axios
- Zustand or Redux Toolkit
- SignalR client
- Recharts or Chart.js

## Contract Freeze Rules

After each batch, output:

1. `NAMING VALIDATION CHECK`
2. `ARCHITECTURAL SNAPSHOT SUMMARY`

Snapshot must include:

- entities
- DTOs
- interfaces
- domain events
- SignalR contracts
- any XAPI client methods added

If any prior contract changes are required, output exactly:

```text
CONTRACT CHANGE PROPOSAL:
Old:
New:
Reason:
Impact:
```

No silent changes.

## Batch Structure

### Batch 1 - Contract Freeze (External + Internal Contracts Only)

Generate only:

- solution folder structure (backend + frontend modules)
- external XAPI DTOs used by queue module (exact OpenAPI mapping only)
- internal application DTOs/contracts
- domain entity property definitions only (no methods yet)
- SignalR message contracts
- frontend TypeScript models matching backend contracts
- enum definitions and naming table

Do not generate:

- services
- controllers
- repositories
- analytics logic
- UI pages

After finishing: STOP.

### Batch 2 - Domain Layer

Generate:

- domain entities with methods and invariants
- aggregate roots
- value objects
- domain events
- call lifecycle state machine
- transition validation
- out-of-order event handling markers

Do not generate infrastructure or HTTP clients.

After finishing: STOP.

### Batch 3 - Persistence Layer

Generate:

- `DbContext`
- EF Core entity configurations
- indexes
- query filters
- migration-ready setup
- partitioning notes
- repository interfaces/implementations (if using repository pattern)

After finishing: STOP.

### Batch 4 - Infrastructure XAPI + Auth

Generate:

- `IQueueXapiClient` / `QueueXapiClient`
- OAuth2 token handling (`/connect/token`)
- `HttpClient` configuration
- OData query builder
- error parsing
- retry/circuit-breaker resilience (Polly)
- spec-quirk handling for malformed refs (if codegen used)

Only documented endpoints may be implemented.

After finishing: STOP.

### Batch 5 - Realtime Ingestion + Reconciliation

Generate:

- polling worker(s) for `/ActiveCalls`
- reconciliation workers for `/CallHistoryView` and `/ReportCallLogData`
- pluggable external realtime adapter interface (optional)
- event inbox/outbox processing
- idempotency, ordering, retry, dead-letter logic
- `QueueEventProcessor`, `QueueCallLifecycleManager`

If a WebSocket adapter is generated, mark it as:

- `UNDEFINED EVENT SCHEMA IN OPENAPI`

and keep it as an adapter contract/placeholder unless user provides verified event schema.

After finishing: STOP.

### Batch 6 - Application Services + SignalR Live State

Generate:

- `QueueService`
- `QueueAnalyticsService` (application orchestration only)
- `QueueLiveStateService`
- `QueueLiveSnapshotBuilder`
- mapping profiles / manual mappers
- transaction boundaries
- caching boundaries
- SignalR publishing via outbox integration

After finishing: STOP.

### Batch 7 - Web API + SignalR Presentation Layer

Generate:

- controllers
- `QueueHub` and typed hub client interface
- request/response contracts
- global exception middleware
- Swagger setup
- health checks
- rate limiting
- DI registration

After finishing: STOP.

### Batch 8 - Analytics Engine (Advanced)

Generate:

- `QueueKpiCalculator`
- `QueueTimeSeriesAnalyzer`
- `QueueComparisonEngine`
- SLA breach detection
- congestion detection
- agent ranking engine
- pre-aggregation workers
- SQL and LINQ optimized query implementations
- cache strategy (MemoryCache/Redis as configured)

After finishing: STOP.

### Batch 9 - Frontend Foundation

Generate:

- `modules/queues` folder structure
- TypeScript models
- API client layer
- SignalR client service
- state store (Zustand or Redux Toolkit)
- hooks
- routing integration

No pages yet.

After finishing: STOP.

### Batch 10 - Frontend Pages

Generate pages one by one or in a small set (max 2 pages per output):

- Queue List
- Create/Edit Queue
- Queue Details
- Live Monitoring Dashboard
- Queue Analytics Dashboard
- Queue Call History
- Agent Activity
- SLA Dashboard

Requirements:

- strict typing
- filtering/pagination/sorting
- realtime store updates
- responsive UI
- loading/error states

After finishing: STOP.

### Batch 11 - End-to-End Integration + Hardening

Generate:

- end-to-end flow wiring
- outbox dispatcher integration
- background worker scheduling
- concurrency controls
- scaling notes implemented in code/config where applicable
- unit/integration test samples

After finishing: STOP.

## Performance Targets

Design for at least:

- 1000+ concurrent calls
- 100+ queues
- 500+ agents
- realtime dashboards without visible lag

## Token Safety Rules

- Generate at most 5-7 files per output
- If a file is long, finish the file fully before moving to the next
- If output starts compressing or omitting code, stop and request regeneration of the last file in full detail

## Session Resume Prefix (Use In New Sessions)

```text
Resume enterprise queue management system generation.
Use previous architectural snapshot as frozen contract.
Apply XAPI strict enforcement layer.
If anything conflicts, stop and report.
```

## Start Instruction

Start with:

`BATCH 1 - Contract Freeze (External + Internal Contracts Only)`

Generate production-ready artifacts for Batch 1 only, then stop and wait.
