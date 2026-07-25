# Implementation Gaps

Deferred work from Phases 1-3, organized for a one-off production event.

## Status Legend

| Status | Meaning |
| --- | --- |
| `Open` | Not started. |
| `In Progress` | Being implemented or tested. |
| `Completed` | Implemented and verified. |
| `Deferred` | Intentionally postponed. |
| `Not Planned` | Intentionally excluded. |

## Core Safety

| ID | Gap | Priority | Status | Details |
| --- | --- | --- | --- | --- |
| S1 | PostgreSQL concurrency tests | High | Completed | Testcontainers verifies concurrent guest confirms, imports, seat increases, and overrides cannot exceed capacity. |
| S2 | Retry transient transaction failures | Medium | Completed | Capacity mutations retry serialization, deadlock, and EF concurrency conflicts up to three times with bounded jitter. |
| S3 | Stale update protection | Medium | Completed | Guest and organizer forms submit PostgreSQL row versions; stale writes are rejected and the latest state is refreshed. |
| S4 | Server-enforced organizer authorization | High before deployment | Completed | Organizer mutations enforce policies inside the application service and derive audit actors from the authenticated principal. |

## Organizer Authentication

| ID | Gap | Priority | Status | Details |
| --- | --- | --- | --- | --- |
| A1 | Production organizer authentication | High before deployment | Open | Replace Development cookie personas with Azure App Service claims/group mapping to `Viewer`, `Operator`, and `ElevatedOperator`. ASP.NET Core Identity remains a future option. |

## Public RSVP Experience

| ID | Gap | Priority | Status | Details |
| --- | --- | --- | --- | --- |
| R1 | Event details on RSVP page | High | Open | Configure and display date, doors/start time, venue, address, and optional dress code. |
| R2 | Replace placeholder support contact | High before real invitations | Open | Replace `rsvp@example.test` with a monitored event support mailbox. |
| R3 | Browser, mobile, and accessibility verification | High | Open | Manually verify mobile layout, keyboard navigation, focus order, labels, rapid double-submit, and refresh/back behavior. Automated browser tests are optional for a one-off event. |
| R4 | Guard development seed data | Medium | Open | Test that the known development RSVP token cannot be seeded in staging or production. |
| R5 | Public page-view audit events | Low | Open | Record sanitized valid, invalid, expired, locked, and current-response page views without tokens. |

## Auditing And Diagnostics

| ID | Gap | Priority | Status | Details |
| --- | --- | --- | --- | --- |
| D1 | Complete rejected-action auditing | Medium | Open | Record safe rejection reasons such as expired, locked, stale, capacity exceeded, duplicate email, and invalid correction. |
| D2 | Centralized audit actor and correlation IDs | Low | Open | Automatically add the actor, such as organizer, guest, or system, and a correlation ID linking audit records to application logs. |
| D3 | Audit-history filtering and detail | Low | Open | Add filters for party, batch, actor, outcome, event type, and date range. |

## Organizer Workflow

| ID | Gap | Priority | Status | Details |
| --- | --- | --- | --- | --- |
| O1 | Email syntax validation | High | Open | Validate email syntax server-side on imports and corrections; presence and browser validation are insufficient. |
| O2 | Confirmation for destructive actions | Medium | Open | Global lock already confirms. Add confirmation for status overrides and consequential corrections, especially seat reductions or email changes. |
| O3 | Effective-status dashboard metrics | High | Open | Exclude effectively expired pending parties from pending-seat metrics so dashboard metrics match capacity calculations. |
| O4 | CSV parser hardening | Medium | Open | Improve malformed-input diagnostics and test multiline fields, Unicode, encoding, and size limits. A mature CSV library is an alternative. |
| O5 | Batch filter and grid refinements | Low | Open | Add a batch filter to QuickGrid. Sorting and pagination are implemented. |

## Batch Management

Treat batch management as a separate follow-up phase.

| ID | Gap | Priority | Status | Details |
| --- | --- | --- | --- | --- |
| B1 | Organizer-supplied batch name and deadline | High | Open | Replace the fixed `Imported batch` name and 14-day deadline with organizer input in the configured event time zone. |
| B2 | Persisted draft-batch workflow | Medium | Open | Allow previewing, saving, reviewing, and committing a draft batch across sessions. |
| B3 | Deadline administration with capacity rechecks | High if multiple batches or extensions are used | Open | An extended deadline can reactivate capacity already reused elsewhere. Transactionally reject an extension that overbooks. |
| B4 | RSVP token lifecycle for sending invitations | High before Phase 4 | Open | Imported parties retain only a token hash. Define secure token generation, storage, and retrieval so email campaigns can send RSVP URLs without leaking tokens. |

## Suggested One-Off Minimum

Implement `S1`, `S4`, `A1`, `R1`, `R2`, `R3`, `O1`, `O3`, `B1`, and `B4`.

Implement `B2` and `B3` only when several invitation waves or deadline changes are expected. Lower-priority audit, grid, and workflow refinements can be deferred.
