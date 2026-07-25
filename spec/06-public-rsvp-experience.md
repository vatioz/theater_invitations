# Public RSVP Experience

This document expands implementation gaps `R1`-`R5` from [05-implementation-gaps.md](05-implementation-gaps.md). The functional requirements in [02-functional-requirements.md](02-functional-requirements.md) remain normative.

## Scope

The public experience serves one invitation party identified by an opaque bearer token. It presents event and allocation details, accepts one whole-party response, and provides safe invalid, expired, locked, current-response, success, and unavailable states.

The experience does not collect a separate guest identity, permit partial acceptance, assign seats, issue tickets, or authenticate invitees by account.

## R1: Event Details On RSVP Page

**Priority:** High
**Status:** In Progress
**Requirements:** FR-042, FR-052; OD-06, OD-07

### Configuration

Store these values as event configuration rather than page constants:

| Field | Required | Notes |
| --- | --- | --- |
| Event name | Yes | Public event title. |
| Event date | Yes | Displayed in the configured event time zone. |
| Doors time | Yes | Local event time with a visible time-zone label. |
| Start time | Yes | Local event time with a visible time-zone label. |
| Venue name | Yes | Public venue name. |
| Venue address | Yes | Human-readable postal address. |
| Dress code | No | Omit the section when empty. |
| Event time-zone ID | Yes | Used for event details and RSVP deadline display. Persist absolute instants as UTC. |

The current deployment default is `Europe/Prague`, but it remains configuration.

### Presentation

- Show event name, date, doors time, start time, venue, and address on active and current-response pages.
- Show dress code only when configured.
- Display a clear time-zone label beside local times and the RSVP deadline.
- Keep the RSVP decision and party allocation visually more prominent than secondary event details.
- Show the recorded party allocation on both success and current-response pages.
- Do not expose event configuration on invalid-token pages if doing so could help distinguish valid event links from unrelated requests. A generic application identity is acceptable.

### Validation

- Application startup must remain available when event configuration is missing or incomplete.
- Elevated organizers must be able to create and edit event configuration from the organizer UI. While configuration is missing, valid RSVP links show a generic temporary-unavailable state without mutation controls. While event details are incomplete, the RSVP flow remains available and omits the incomplete event-details section.
- Doors time must be earlier than or equal to start time.
- Event date/time and deadline conversions must handle daylight-saving transitions unambiguously.

### Acceptance Criteria

1. An active one-seat and two-seat invitation display all configured event details and correct party wording.
2. Event and deadline times display in the configured event time zone with a visible label.
3. Empty dress code does not render an empty heading or placeholder.
4. Mobile layout presents details without horizontal scrolling.
5. Current-response pages retain event details and allocation context.

## R2: Production Support Contact

**Priority:** High before real invitations
**Status:** In Progress
**Requirements:** FR-050, FR-070; OD-08, OD-12

### Configuration

- Persist one monitored event-specific support email address as event configuration.
- All authorized organizer roles may view the configured address.
- Only organizers in the `ElevatedOperator` role may change the address.
- Do not use `rsvp@example.test` outside Development.
- Validate email syntax on every attempted change.
- Audit successful and rejected changes without recording unrelated event or invitation data.
- Use concurrency protection so one organizer cannot silently overwrite another organizer's change.
- Do not hard-code a production address in a component or deployment setting.
- Surface incomplete configuration to organizers, but do not block application startup. Operational readiness and real invitation use still require a valid, approved address.

### Presentation

- Show support contact on expired, locked, capacity-error, transaction-error, and other unavailable self-service states.
- Render a readable address and a `mailto:` link.
- Invalid-token pages should direct guests to the event organizer without exposing private invitation data. Showing the public event support address is allowed once the address is approved as public.
- Do not include the invitee email, raw token, or full RSVP URL in the generated support message.

### Acceptance Criteria

1. All authorized organizers can view the current support address, but only an `ElevatedOperator` can change it.
2. Malformed and `.example` addresses are rejected outside Development, and the previous valid value remains unchanged. A missing address leaves public unavailable states without a contact link.
3. Development may use `rsvp@example.test` only in the Development environment.
4. Successful and rejected changes create sanitized audit events attributed to the organizer.
5. Concurrent stale updates are rejected and show the current configured address.
6. Expired and locked views contain the approved support address.
7. The support link contains no RSVP token or guest personal data.

## R3: Browser, Mobile, And Accessibility Verification

**Priority:** High
**Status:** Deferred
**Requirements:** FR-051, FR-052

Implementation is intentionally deferred. The requirements below are retained for future scope and do not form part of the current implementation plan.

### Supported Browser Baseline

Verify the current stable versions of:

- Chrome or Edge on desktop.
- Firefox on desktop.
- Safari on iOS.
- Chrome on Android.

The page must remain usable when client-side interactivity reconnects after a temporary network interruption. Server-side mutation rules remain authoritative.

### Accessibility Target

Target WCAG 2.2 AA for the public RSVP flow.

Verify:

- Semantic heading order and one clear page-level heading.
- Visible keyboard focus for all interactive controls.
- Complete keyboard operation without pointer input.
- Programmatic labels for radio buttons, text area, and submit/update controls.
- Validation and server errors announced through an alert or live region.
- Color contrast of text, buttons, focus indicators, and validation messages.
- No important meaning conveyed by color alone.
- Zoom to 200% and narrow viewport operation without lost content or horizontal page scrolling.
- Focus moves to the primary status/error heading after navigation or state replacement.

### Functional Browser Matrix

| Scenario | Expected Result |
| --- | --- |
| Valid pending link | Event, allocation, deadline, Confirm, and Decline are available. |
| Confirm with accessibility text | Response and text persist; success state shows allocation. |
| Decline after confirm | Accessibility text is cleared. |
| Existing response | Recorded status appears with Update RSVP before deadline. |
| Rapid double-click | One consistent result and no duplicate side effects. |
| Refresh/back after submit | Current persisted response is displayed. |
| Two stale tabs | Older submission is rejected and latest state is shown. |
| Expired link | No mutation controls; support contact is shown. |
| Global lock | No mutation controls; support contact is shown. |
| Invalid token | Generic state with no invitee data. |
| Temporary database failure | Retry-safe error; no partial response. |

### Automation

- Add browser automation for at least valid confirm, decline, existing-response update, invalid token, expired, locked, stale tab, and rapid double-submit.
- Run automated accessibility checks on active, success/current-response, expired, locked, and invalid states.
- Keep a short manual checklist for mobile devices and screen-reader smoke testing because automated checks are incomplete.

### Acceptance Criteria

1. Automated browser scenarios pass against PostgreSQL.
2. Automated accessibility scans report no serious or critical violations.
3. Keyboard-only confirm, decline, update, and error recovery succeed.
4. Manual iOS and Android smoke tests complete without blocking layout or interaction defects.

## R4: Development Seed Guard

**Priority:** Medium
**Status:** In Progress

### Required Behavior

- Development seed execution must depend on `IHostEnvironment.IsDevelopment()` at the composition root.
- Seed code must never run merely because the database is empty.
- The known token `development-rsvp-token` must not be generated or accepted in Staging or Production unless independently created as real invitation data, which operational procedures must prohibit.
- Production deployment must not include a startup option that silently enables Development seeding.

### Verification

- Add startup/composition tests for Development, Staging, and Production environments.
- Assert Development creates the sample party idempotently.
- Assert Staging and Production create no sample party and no hash matching the known token.
- Add a deployment smoke check that queries for the known token hash without logging the raw token.

### Acceptance Criteria

1. Development seed remains idempotent.
2. Staging and Production startup never invoke the seed service.
3. Automated tests prove the known token hash is absent outside Development.

## R5: Public Page-View Audit Events

**Priority:** Low
**Status:** Deferred
**Requirements:** FR-113, FR-115

Implementation is intentionally deferred. The requirements below are retained for future scope and do not form part of the current implementation plan.

### Audited Outcomes

Record at most one page-view event per initial page request for these normalized outcomes:

| Outcome | Party reference | Allowed metadata |
| --- | --- | --- |
| `active` | Yes | Batch reference and effective state. |
| `current-response` | Yes | Current status; no accessibility text. |
| `expired` | Yes | Effective state only. |
| `locked` | Yes | Effective state only. |
| `invalid-link` | No | Generic reason category only. |
| `unavailable` | When known | Sanitized failure category. |

### Privacy And Logging

- Never store raw RSVP tokens, token hashes, URLs, query strings, referrers, invitee email, or accessibility text in page-view audit metadata.
- Invalid-link events must not create or infer a party reference.
- Use actor category `Invitee`; no authenticated actor identifier is expected.
- Use the HTTP request correlation ID through the centralized audit context when implemented.
- Prevent framework access logs and telemetry from recording route token values or full request paths.

### Volume And Retention

- Avoid auditing interactive re-renders, reconnects, or component refreshes as new page views.
- Define retention with the privacy owner before production.
- Rate-limit or aggregate repeated invalid-link events to prevent audit-table abuse.

### Acceptance Criteria

1. Each initial public page load creates no more than one normalized page-view audit event.
2. Invalid links create no party reference and reveal no data in response or audit records.
3. Automated tests assert token, token hash, email, query string, and accessibility text are absent from audits and captured logs.
4. Blazor reconnects and component re-renders do not duplicate page-view events.

## Recommended Implementation Order

1. R1 event configuration and presentation.
2. R2 production support-contact validation.
3. R4 development seed guard.

R3 browser and accessibility verification and R5 page-view auditing are deferred.
