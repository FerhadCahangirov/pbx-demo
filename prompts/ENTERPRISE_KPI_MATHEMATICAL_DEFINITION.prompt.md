# Enterprise KPI Mathematical Definition Prompt (Queue Management)

## Role

You are a telephony analytics mathematician and enterprise call-center KPI architect.

You are defining mathematically precise KPI formulas for a Queue Management System integrated with 3CX XAPI.

These definitions will become the immutable analytics contract of the system.

After approval:

- KPI formulas may not change without explicit contract revision
- backend services must implement these formulas exactly
- frontend dashboards must display these computed values exactly
- aggregation workers must use the same formulas and denominator policies

No vague explanations. No approximations. Use formal definitions only.

## Objective

Define mathematically rigorous KPI definitions for:

- queue performance
- agent performance
- service level compliance
- time-based analytics
- multi-queue comparison
- system load / congestion indicators

## Data Model Context (Use In Definitions)

Assume the analytics engine has access to a normalized queue-call fact/projection model with fields such as:

- `QueuedAtUtc`
- `AnsweredAtUtc` (nullable)
- `CompletedAtUtc` (nullable)
- `AbandonedAtUtc` (nullable)
- `WaitingMs` (nullable)
- `TalkingMs` (nullable)
- `WrapUpMs` / ACW (nullable)
- `Disposition` (`Answered`, `Abandoned`, `Missed`, `Transferred`, etc.)
- `SlaThresholdSec` (per queue/call effective threshold)
- `QueueId`
- `AnsweredByAgentId` (nullable)

If a value is unavailable in realtime and is backfilled later (e.g., from call history/log reconciliation), define:

- realtime provisional behavior
- finalized historical behavior

## Required Definition Format (For Every KPI)

For each KPI, include:

1. Formal mathematical formula
2. Variable definitions
3. Time-window definition
4. Inclusion criteria
5. Exclusion criteria
6. Edge-case handling
7. Null/zero handling
8. Aggregation method (`sum`, `mean`, weighted mean, percentile, etc.)
9. Realtime calculation strategy
10. Historical/finalized aggregation strategy

## Global Time and Duration Definitions (Mandatory)

Define these symbols first and use them consistently:

- `T_arrival` = queue arrival time (queue-enter timestamp)
- `T_answer` = answer timestamp (first valid answer for the queue-call in scope)
- `T_end` = final completion timestamp for the queue-call in scope
- `T_abandon` = abandon timestamp when caller exits before answer
- `W` = waiting duration
- `D` = talk duration
- `A` = after-call work duration (ACW / wrap-up), if available
- `Delta_t` = selected analytics period

Formal duration definitions:

- Answered call waiting time: `W = T_answer - T_arrival`
- Abandoned call waiting time: `W = T_abandon - T_arrival`
- Talk duration: `D = T_end - T_answer`

All final KPI durations must be reported in seconds unless otherwise stated.

Define timezone policy explicitly for bucketing (recommended: compute in queue local timezone, store UTC boundaries).

## Required KPI Definitions

You must define all items below and freeze the denominator/inclusion policy choices.

### 1. Total Calls (TC)

Define:

- `TC = total number of queue-calls entering queue within Delta_t`

Clarify and freeze:

- counting unit (logical queue-call vs call leg/segment)
- handling of requeue events (same logical call re-entering same queue)
- handling of transfers between queues

### 2. Answered Calls (AC)

Define:

- `AC = count of queue-calls answered`

Must choose and freeze one windowing basis:

- arrival-window counted, or
- answer-window counted

Justify the choice and state implications.

### 3. Abandoned Calls (AB)

Define:

- `AB = count of queue-calls where T_abandon exists and T_answer is null`

Must define short-abandon policy:

- include all abandons, or
- exclude abandons with `W < theta_short_abandon`

If excluding, define `theta_short_abandon` and how excluded calls affect `TC`, `AB`, and `SL%` denominators.

### 4. Abandonment Rate (AR)

Define:

- exact formula
- whether output is ratio or percent
- rounding precision

Example structure:

- `AR = AB / TC_eligible`

where `TC_eligible` must be defined explicitly.

### 5. Average Waiting Time (AWT)

Define at least:

- `AWT_answered`
- `AWT_all`

Specify which is the primary KPI and which is secondary.

Define whether abandoned waits are included in the primary KPI.

### 6. Average Talk Time (ATT)

Define:

- `ATT = mean(D)` over eligible answered calls

Clarify:

- transferred calls included or excluded
- conference calls included or excluded
- zero/negative durations handling

### 7. Service Level (SL%)

Define a precise service level formula with:

- `theta_sla` (seconds)
- numerator eligibility
- denominator eligibility
- short-abandon policy interaction
- whether abandons remain in denominator

Explicitly define:

- `WithinSLA = {calls | answered and W <= theta_sla}`

### 8. Agent Utilization (AU%)

Define:

- talk-time numerator
- logged-in denominator

Define `LoggedInTime` precisely (source, inclusion/exclusion of breaks if modeled).

### 9. Agent Occupancy Rate (AO%)

Define:

- `AO = (TalkTime + ACW) / AvailableTime`

Define:

- `ACW`
- `AvailableTime`
- behavior when `AvailableTime = 0`

### 10. Peak Concurrency (PC)

Define:

- `PC = max simultaneous active queue calls during Delta_t`

Must define:

- event-driven exact method or sampling approximation
- sampling interval if sampled
- tie handling and bucket assignment

### 11. Queue Congestion Index (QCI)

Define a composite metric using at least:

- waiting calls count
- average waiting time
- SLA breach rate

Provide:

- exact formula
- normalized inputs
- weight constants
- final range (recommended `0..100`)

### 12. Agent Ranking Score (ARS)

Define composite score using:

- SLA compliance
- average handling time (or inverse)
- answer rate
- utilization (or occupancy)

Define:

- `w1, w2, w3, w4`
- `sum(w_i) = 1`
- normalization method for each component

### 13. Time Bucket Aggregation Rules

Define exact bucketing for:

- hourly
- daily
- monthly

Must define:

- bucket boundary inclusivity (`[start, end)`)
- timezone conversion rules
- daylight saving transitions
- late-arriving event reconciliation behavior

### 14. Multi-Queue Comparison Index (MQI)

Define normalized comparison formula using at least:

- service level
- waiting-time inverse
- abandonment inverse

Choose and freeze normalization method:

- Min-Max scaling, or
- Z-score

Define:

- normalization window/population
- handling of zero variance
- weights (`alpha`, `beta`, `gamma`)

## Required Additional Definitions

### Percentiles (If Used)

If you define `P90 wait` / `P95 talk` / similar:

- specify percentile estimator
- interpolation method
- minimum sample size policy

### Real-Time vs Finalized KPI Policy

For every KPI, classify as:

- exact in realtime
- provisional in realtime
- finalized only after reconciliation

If provisional, define how revisions are published (e.g., overwrite prior buckets).

### Rounding and Precision Rules

Define:

- integer rounding policy for durations shown in UI
- decimal precision for rates/percentages
- internal storage precision vs display precision

## Edge Case Handling (Mandatory)

Define deterministic behavior for:

- `TC = 0`
- `AC = 0`
- no agents logged in
- division by zero
- null timestamps
- negative durations
- duplicate events
- out-of-order events
- calls spanning bucket boundaries
- calls arriving before `Delta_t` and ending within `Delta_t`

You must explicitly state inclusion basis for time-windowed counting:

- arrival-time anchored, answer-time anchored, end-time anchored, or KPI-specific policy

## Output Format (Strict)

Output in this order:

1. Variable glossary table
2. Global time definitions and windowing policy
3. Eligibility and exclusion policies (including short abandon and SLA denominator)
4. Formal KPI formula list (all required KPIs)
5. Time bucket aggregation rules
6. Realtime vs finalized calculation rules
7. Rounding and precision rules
8. Final immutable KPI contract summary

## Contract Lock Rule

After generating this KPI definition, formulas are considered locked.

Future changes must use:

```text
KPI CONTRACT REVISION PROPOSAL:
Old formula:
New formula:
Business reason:
Impact analysis:
```

## Final Instruction

Generate mathematically precise, enterprise-grade KPI definitions suitable for:

- financial reporting
- operational dashboards
- executive decision-making
- performance bonuses
- SLA compliance auditing

No ambiguity. Formal definitions only.
