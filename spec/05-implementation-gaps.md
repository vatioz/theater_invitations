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
| A1 | Production organizer authentication | High before deployment | Open | Replace Development cookie personas with Azure App Service claims/group mapping to `Operator` and `ElevatedOperator`. ASP.NET Core Identity remains a future option. |

## Public RSVP Experience

| ID | Gap | Priority | Status | Details |
| --- | --- | --- | --- | --- |
| R1 | Event details on RSVP page | High | In Progress | Persisted event details, organizer-managed configuration, and public presentation are implemented; production event values remain to be configured. |
| R2 | Replace placeholder support contact | High before real invitations | In Progress | Role-gated editing, validation, concurrency protection, auditing, and public unavailable-state links are implemented; the approved mailbox remains to be configured. |
| R3 | Browser, mobile, and accessibility verification | High | Deferred | Manual and automated browser and accessibility verification is documented in [06-public-rsvp-experience.md](06-public-rsvp-experience.md) but is not currently planned for implementation. |
| R4 | Guard development seed data | Medium | In Progress | Composition and token-absence tests cover Development, Staging, and Production; the production deployment smoke query remains to be wired into deployment operations. |
| R5 | Public page-view audit events | Low | Deferred | Sanitized public page-view auditing is documented in [06-public-rsvp-experience.md](06-public-rsvp-experience.md) but is not currently planned for implementation. |
| R6 | Czech-only UI and focused RSVP card | High | Open | Translate all public and organizer UI plus default email content; implement direct response actions and Czech locale formatting as specified in [10-product-workflow-and-seating-changes.md](10-product-workflow-and-seating-changes.md). |

## Auditing And Diagnostics

| ID | Gap | Priority | Status | Details |
| --- | --- | --- | --- | --- |
| D1 | Complete rejected-action auditing | Medium | Open | Record safe rejection reasons such as expired, locked, stale, capacity exceeded, duplicate email, and invalid correction. |
| D2 | Centralized audit actor and correlation IDs | Low | Open | Automatically add the actor, such as organizer, guest, or system, and a correlation ID linking audit records to application logs. |
| D3 | Audit-history filtering and detail | Low | Open | Add filters for party, batch, actor, outcome, event type, and date range. |

## Organizer Workflow

| ID | Gap | Priority | Status | Details |
| --- | --- | --- | --- | --- |
| O1 | Email syntax validation | High | Completed | Imports and corrections validate normalized email syntax server-side before duplicate and capacity checks. |
| O2 | Confirmation for destructive actions | Medium | Open | Global lock already confirms. Add confirmation for status overrides and consequential corrections, especially seat reductions or email changes. |
| O3 | Effective-status dashboard metrics | High | Completed | Active pending seats now exclude pending parties at or past their deadline, matching the capacity calculation. |
| O4 | CSV parser hardening | Medium | Completed | The quoted-field parser reports malformed CSV, preserves multiline Unicode fields, accepts UTF-8 BOM input, and enforces a 1 MB limit. |
| O5 | Batch filter and grid refinements | Low | Completed | QuickGrid includes a stable batch-ID filter; sorting and pagination remain available. |

## Batch Management

Treat batch management as a separate follow-up phase.

| ID | Gap | Priority | Status | Details |
| --- | --- | --- | --- | --- |
| B1 | Organizer-supplied batch name and deadline | High | In Progress | Draft metadata and configured-zone deadline input are implemented; detailed behavior is in [07-batch-management.md](07-batch-management.md). |
| B2 | Replace persisted drafts with temporary preview | High | Open | Remove the implemented persisted draft UI and lifecycle. Keep preview in memory and commit only after one confirmation and authoritative transactional revalidation; see [10-product-workflow-and-seating-changes.md](10-product-workflow-and-seating-changes.md). |
| B3 | Deadline administration with capacity rechecks | High if multiple batches or extensions are used | In Progress | Transactional deadline changes and system-expiration reopening are implemented; browser and concurrency coverage remains to be expanded. |
| B4 | RSVP token lifecycle for sending invitations | High before Phase 4 | In Progress | Active token records retain raw tokens only for restricted manual email rendering; hash lookup and regeneration are implemented. |

## Suggested One-Off Minimum

Implement `S1`, `S4`, `A1`, `R1`, `R2`, `O1`, `O3`, `B1`, and `B4`.

Implement `B2` and `B3` only when several invitation waves or deadline changes are expected. Lower-priority audit, grid, and workflow refinements can be deferred.

## Agreed Product Changes

The Czech-only UI, RSVP redesign, workflow simplification, seating module, extended import schema, and theater export boundary are specified in [10-product-workflow-and-seating-changes.md](10-product-workflow-and-seating-changes.md). They are agreed requirements but are not yet implemented unless a more specific status above says otherwise.
