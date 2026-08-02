# Phased Implementation

The phases below describe software delivery increments. Invitation Batch 1, reminders, and later invitation batches are operational workflows, not implementation phases.

The current ordered implementation for the agreed Czech localization, RSVP redesign, CSV schema, batch-flow, and campaign-flow changes is [11-ordered-product-change-implementation.md](11-ordered-product-change-implementation.md). For those changes, spec 11 supersedes older deliverable wording in this historical roadmap. Seating and export remain deferred.

## Phase 0: Decisions and Foundations

### Goal

Establish the decisions and technical baseline required to implement behavior consistently.

### Deliverables

- Confirm event capacity, event time zone, organizer support address, and event content ownership.
- Resolve all Phase 0 blockers in the [decision register](04-open-decisions.md).
- Create the Blazor solution, test projects, PostgreSQL integration, and migration workflow.
- Define environment configuration and secret handling.
- Protect organizer routes using the selected Azure App Service authentication policy.
- Establish event configuration, clock abstraction, audit conventions, and UTC timestamp storage.
- Define local development and production deployment configuration.

### Dependencies

- Requestor identifies decision owners.
- Hosting, database, authentication, and email accounts can be provisioned.

### Acceptance Criteria

- The application and tests build in a clean environment.
- A migration can create and update an empty database.
- Public and organizer routes have distinct authorization behavior.
- Secrets are not committed to source control.
- Configured local time is converted unambiguously to UTC.

### Deferred

- Guest workflows, CSV imports, and real email delivery.

## Phase 1: RSVP Domain Core

### Goal

Implement the party, lifecycle, deadline, lock, capacity, and audit rules without depending on the final UI.

### Deliverables

- Party invitation, batch, event configuration, and audit persistence.
- `Pending`, `Confirmed`, `Declined`, and `Expired` lifecycle behavior.
- Atomic whole-party confirmation and decline operations.
- Server-side expiration and global-lock enforcement.
- Capacity calculations and transaction-safe capacity guards.
- Idempotent handling of repeated submissions.
- Unit and database integration tests for transitions and concurrency.

### Dependencies

- Phase 0 foundations.
- Decisions OD-02, OD-03, OD-04, and OD-05.

### Acceptance Criteria

- No operation can partially confirm an allocation.
- Expired or globally locked invitations cannot be mutated through any application path.
- Repeating the same response does not create inconsistent state.
- Concurrent imports, confirmations, and overrides cannot exceed configured capacity.
- Every successful or rejected state-changing attempt creates the required audit event.

### Deferred

- Public styling, organizer grids, and outbound email.

## Phase 2: Guest RSVP Experience

### Goal

Deliver a secure, understandable, responsive RSVP flow for invitees.

### Deliverables

- Opaque tokenized RSVP route.
- Event, party allocation, and deadline presentation.
- Confirm and decline actions for the complete party.
- Accessibility requirements input shown only for confirmation, with explanatory help and a configured length limit.
- Current-response, success, expired, locked, invalid-link, and unavailable states.
- Approved pre-deadline response-change behavior.
- Support contact on states where self-service is unavailable.
- Accessibility and responsive-browser tests.

### Dependencies

- Phase 1 domain operations.
- Decisions OD-02, OD-06, OD-07, OD-08, and OD-09.

### Acceptance Criteria

- A guest can complete a valid RSVP without authentication or client-side application state.
- The page clearly states whether the invitation covers one or multiple seats.
- Double-clicks and browser retries do not duplicate effects.
- Accessibility text is not retained for a declined party.
- Invalid tokens reveal no invitee information.
- Expired and locked pages cannot be bypassed by posting directly.

### Deferred

- Sending links to real invitees and theater export mapping.

## Phase 3: Organizer Administration

### Goal

Allow authorized organizers to manage invitation data safely and inspect current event state.

### Deliverables

- Authenticated organizer dashboard.
- CSV upload, parsing, validation preview, and explicit commit.
- Batch creation and deadline assignment.
- Duplicate email and duplicate source-row handling.
- Capacity validation before import commit.
- Searchable, filterable, sortable, and paged party grid.
- Confirmed, active pending, declined, expired, and remaining-capacity metrics.
- Manual correction and status override with mandatory reason.
- Global lock control with confirmation.
- Audit history view with sensitive-field restrictions.

### Dependencies

- Phases 1 and 2.
- Decisions OD-01, OD-03, OD-04, OD-10, OD-11, and OD-12.

### Acceptance Criteria

- Invalid CSV rows are identified before any row is persisted.
- Import is all-or-nothing unless an explicitly approved partial-import mode is added.
- The preview shows row counts, allocated-seat totals, duplicates, and resulting remaining capacity.
- Unauthorized users cannot access organizer data or actions.
- Overrides and lock changes capture actor, time, reason, previous value, and new value.
- Metrics reconcile with the party grid and canonical capacity formula.

### Deferred

- Production email sending and theater-specific exports.

## Phase 4: Invitation Email

### Goal

Send invitations and reminders reliably without coupling RSVP state to a provider request.

### Deliverables

- Verified sender domain and protected API credentials.
- Approved invitation and reminder templates.
- HTML and plain-text rendering with escaped personalized values.
- Preview and test-send workflow.
- Prepared recipient snapshot and final send confirmation.
- Durable per-recipient dispatch records and idempotency controls.
- Provider-aware chunking, bounded retries, and failure reporting.
- Reminder audience selection restricted to active pending invitations.
- Delivery, bounce, and complaint event handling according to the approved policy.

### Dependencies

- Phase 3 batch administration.
- Decisions OD-13 through OD-18.

### Acceptance Criteria

- One party receives one invitation email per approved send operation.
- Retrying a partially failed send does not resend successful deliveries unintentionally.
- A reminder cannot target declined, confirmed, expired, or globally ineligible invitations.
- Organizer-visible records distinguish queued, accepted, delivered when known, bounced, complained, and failed outcomes.
- No specification or UI promises inbox placement.

### Deferred

- Ticket or seat emails after theater handoff unless separately approved.

## Phase 5: Theater Handoff

### Goal

Produce a reconciled, repeatable manifest that matches the theater's agreed import contract.

### Deliverables

- Canonical confirmed-party manifest.
- Configurable theater-specific column mapping and CSV generation.
- Accessibility mapping agreed with the theater.
- Final lock, preview, reconciliation, and export workflow.
- Export run history containing generation time, actor, mapping version, row count, seat count, and file digest.
- Correction and re-export procedure.

### Dependencies

- Phases 3 and 4.
- Decisions OD-19 through OD-24.

### Acceptance Criteria

- Only confirmed parties are included.
- Export row and seat totals reconcile with dashboard metrics.
- CSV escaping and encoding preserve names, companies, and accessibility text.
- Repeating an unchanged export yields equivalent manifest content.
- Theater acceptance is recorded against a tested sample before final handoff.

### Deferred

- Seat assignment, ticket creation, and event-day operations.

## Phase 6: Hardening and Production Readiness

### Goal

Validate security, reliability, support, and recovery for the event window.

### Deliverables

- End-to-end tests covering import, send, RSVP, expiration, later batches, lock, override, and export.
- Authorization, token leakage, CSRF, injection, privacy, and sensitive-log review.
- Load and concurrency tests around deadlines and organizer actions.
- Database backup and restore rehearsal.
- Monitoring, alerting, structured logs, and operational diagnostics.
- Data retention and deletion implementation.
- Organizer runbook and event support ownership.
- Staging rehearsal using representative data and email accounts.

### Dependencies

- Production-shaped implementation from Phases 0 through 5.
- Decisions OD-25 through OD-29.

### Acceptance Criteria

- Critical workflows pass in the staging environment.
- Restore, lock, failed-send recovery, correction, and re-export procedures are rehearsed.
- Monitoring detects failed requests, email failures, and database availability issues.
- Sensitive accessibility text and RSVP tokens are absent from normal application logs.
- Named operators accept the production runbook.
