# Strict XAPI Enforcement Layer (3CX XAPI, Queue Module)

Use this prompt as a mandatory guardrail before generating any queue-management code that integrates with 3CX XAPI.

## Mode

You must operate in `STRICT XAPI COMPLIANCE MODE`.

You are not allowed to invent, assume, or fabricate any:

- endpoint
- route
- HTTP method
- request body schema
- response schema
- OData parameter
- field name
- enum value
- WebSocket event name or payload

All external API behavior must match `ferhad-xapi-1.0.0-resolved.json` exactly.

## Source Of Truth Rules

The only valid external API definitions are the OpenAPI definitions extracted from:

- `ferhad-xapi-1.0.0-resolved.json`

You must preserve:

- exact path strings
- exact casing
- exact property names
- nullability
- enum values
- numeric types (`int32`, `int64`, decimal-like fields)
- formats (`date-time`, `duration`, `time`)

## Confirmed XAPI Facts (Do Not Override)

- OpenAPI title/version: `XAPI` `1.0.0`
- Base service path: `/xapi/v1`
- Security scheme: OAuth2 client credentials (`/connect/token`)
- `Users` endpoints represent extensions/agents in this spec
- Queue CRUD exists and uses `/Queues` with `Pbx.Queue`
- Queue agent/manager listing exists (`GET /Queues({Id})/Agents`, `GET /Queues({Id})/Managers`)
- No dedicated queue-agent assignment write endpoints are documented; assignment changes must be done by queue update payload (`Pbx.Queue`) if implemented
- `/ActiveCalls` exists but returns minimal `Pbx.ActiveCall` fields only
- No WebSocket endpoint is documented in this OpenAPI file
- `Pbx.CallParticipant` schema exists but is not exposed by a queue participant endpoint in this spec (it appears in `Pbx.CallControlResultResponse`)

## Approved Queue-Relevant XAPI Endpoint Families

Only use endpoints from these documented families unless explicitly re-verified in OpenAPI:

- `/Queues*`
- `/ActiveCalls*`
- `/CallHistoryView*`
- `/Users*` (extensions/agents)
- `/ActivityLog*`
- `/EventLogs*`
- `/ReportCallLogData*`
- Queue analytics reports:
  - `/ReportAbandonedQueueCalls*`
  - `/ReportAgentLoginHistory*`
  - `/ReportAgentsInQueueStatistics*`
  - `/ReportAverageQueueWaitingTime*`
  - `/ReportBreachesSla*`
  - `/ReportDetailedQueueStatistics*`
  - `/ReportQueueAnsweredCallsByWaitTime*`
  - `/ReportQueueAnUnCalls*`
  - `/ReportQueueCallbacks*`
  - `/ReportQueueFailedCallbacks*`
  - `/ReportQueuePerformanceOverview*`
  - `/ReportQueuePerformanceTotals*`
  - `/ReportStatisticSla*`
  - `/ReportTeamQueueGeneralStatistics*`
  - `/ReportCallDistribution*`
  - `/ReportUserActivity*`

Optional chat queue analytics (documented, but separate from voice queue runtime):

- `/ReportAbandonedChatsStatistics*`
- `/ReportQueueAgentsChatStatistics*`
- `/ReportQueueAgentsChatStatisticsTotals*`
- `/ReportQueueChatPerformance*`

## Endpoint Validation Requirement

Before generating any integration code, validate internally:

1. Path exists in OpenAPI `paths`
2. HTTP method matches
3. Required parameters match (path/query/header)
4. Request body schema exists (if applicable)
5. Response schema exists (if applicable)

If not present, stop and output exactly:

```text
XAPI VALIDATION ERROR:
Endpoint not found in OpenAPI specification.
```

Do not generate fallback or guessed code.

## DTO Strict Mapping Rules

When generating external DTOs:

- Use OpenAPI schema as the base
- Do not add extra properties
- Do not rename fields
- Do not flatten nested objects in the external DTO layer
- Preserve nullable vs non-nullable types
- Preserve enum strings exactly

Mapping into internal models must be explicit via:

- manual mapping function, or
- mapping profile (AutoMapper or equivalent)

Never use OpenAPI DTOs as domain entities.

## OData Enforcement Rules

If an endpoint supports OData in the OpenAPI definition, only generate the documented options.

Common documented options in this spec include:

- `$top`
- `$skip`
- `$search`
- `$filter`
- `$count`
- `$orderby`
- `$select`
- `$expand`

Rules:

- Do not assume unsupported OData options
- Build query strings from typed query builders
- Keep option names exact (including `$`)

## WebSocket/Event Enforcement Rules

This OpenAPI file does not define a WebSocket endpoint or queue event schemas.

Therefore:

- Do not invent WebSocket URLs
- Do not invent event names
- Do not invent payload schemas

If realtime support is requested, mark external event schema status as:

```text
UNDEFINED EVENT SCHEMA IN OPENAPI
```

You may generate:

- internal realtime abstraction interfaces
- polling-based fallbacks (`/ActiveCalls` + reconciliation via history/log reports)
- adapter placeholders that require manual verification

## No-Inference Rule

You are not allowed to infer:

- hidden endpoints
- undocumented internal 3CX routes
- "probably existing" APIs
- admin-only APIs not in OpenAPI
- deprecated APIs unless present in OpenAPI

## Internal vs External Separation Rule

Always separate:

External (3CX XAPI)

- OpenAPI DTOs
- `HttpClient` contracts
- OData query builders
- external adapter payloads

Internal (CRM)

- domain entities
- state machine models
- analytics projections
- aggregation tables
- SignalR contracts

Never leak external DTOs into domain/application internals.

## Required Validation Comment Block (Per XAPI Client Method)

For every generated XAPI client method, include:

```csharp
// XAPI Endpoint Validation:
// Path: /exact/path
// Method: GET
// Verified Against: ferhad-xapi-1.0.0-resolved.json (paths)
```

If verification is uncertain, stop generation.

## Spec Quirk Handling (Documented Exceptions)

The analyzed spec contains malformed schema refs in some responses, for example:

- `Pbx.QualityReport_1`
- `Pbx.CallControlResultResponse_1`

Rules:

- Treat these as spec quirks
- Resolve against `components.schemas` by name only when present
- Do not silently rewrite endpoint behavior
- Document the workaround in generated client code/comments

## Mismatch / Halt Rules

If any mismatch is detected between implementation and OpenAPI:

```text
STRICT MODE HALT:
Implementation conflicts with OpenAPI specification.
```

## Type Safety Enforcement

- C#: nullable reference types enabled
- TypeScript: strict mode enabled
- No `any`
- No dynamic JSON parsing without typed model
- No stringly-typed statuses in domain layer (map to internal enums)

## Final Hard Rule

If something is not defined in OpenAPI:

- do not implement it as an external XAPI call
- mark it as an internal CRM feature or manual verification requirement

Use this exact fallback text:

```text
Feature requires extension beyond documented XAPI.
Manual verification required.
```
