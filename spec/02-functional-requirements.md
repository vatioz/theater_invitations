# Functional Requirements

The keywords **must**, **must not**, **should**, and **may** are normative.

## Party and Allocation

- **FR-001** Each imported row must represent one invitation party and one email recipient.
- **FR-002** Each party must have a positive integer `allocated_seats`; the expected values are one or two, but the technical constraint should remain configurable.
- **FR-003** A party must confirm or decline its complete allocation. Partial confirmation is not supported.
- **FR-004** The application must not request or store a `+1` name.
- **FR-005** The sum of capacity-reserving allocations must not exceed configured event capacity.
- **FR-006** Event capacity must be configurable. The current planning value is approximately 340 seats and is not a code constant.

## Invitation Lifecycle

- **FR-010** Every invitation must be in exactly one state: `Pending`, `Confirmed`, `Declined`, or `Expired`.
- **FR-011** A newly committed invitation must start as `Pending`.
- **FR-012** An eligible `Pending` invitation may become `Confirmed` or `Declined` through a guest response or authorized override.
- **FR-013** An unanswered `Pending` invitation becomes `Expired` after its deadline.
- **FR-014** Expiration must be effective according to the authoritative clock even if no background job has yet persisted the `Expired` state.
- **FR-015** Response changes before the deadline must follow OD-02. Until resolved, no implementation may assume they are allowed.
- **FR-016** Late or VIP exceptions must occur only through the approved override policy and must remain subject to capacity enforcement.
- **FR-017** Repeating a response that already produced the requested state must be idempotent.
- **FR-018** A stale request that conflicts with a newer state must be rejected and show the current state.

## Deadlines and Locking

- **FR-020** Each invitation must have an absolute deadline stored as UTC.
- **FR-021** Organizers must enter and view deadlines in the configured event time zone, including an explicit time and time-zone label.
- **FR-022** Deadline eligibility must be checked inside the same server-side operation that applies an RSVP mutation.
- **FR-023** The global lock must override individual deadlines and reject guest mutations.
- **FR-024** The global lock must not silently alter existing invitation statuses.
- **FR-025** Public pages may derive an expired view dynamically, while persistence of `Expired` may occur on access or through a maintenance process.
- **FR-026** Locking, unlocking, and deadline changes must be audited.

## Capacity

- **FR-030** Reserved capacity equals confirmed seats plus seats from pending invitations that are not effectively expired.
- **FR-031** Remaining capacity equals configured capacity minus reserved capacity.
- **FR-032** Declined and effectively expired invitations do not reserve capacity.
- **FR-033** Imports, deadline extensions, status overrides, and other actions that could reserve seats must perform an atomic capacity check.
- **FR-034** Concurrent operations must not cause reserved capacity to exceed configured capacity.
- **FR-035** The organizer UI must explain which invitation states contribute to each capacity metric.

Example:

```text
capacity = 340
confirmed seats = 220
active pending seats = 70
remaining capacity = 50
```

## Guest RSVP Page

- **FR-040** A guest must access the page using an opaque, unguessable bearer token.
- **FR-041** An unknown, malformed, or revoked token must show a generic invalid-link view without exposing guest data.
- **FR-042** An active invitation page must show invitee name, number of allocated seats, event details, and the exact response deadline.
- **FR-043** Multiple-seat wording must make clear that confirmation covers the invitee and their guest as one party.
- **FR-044** The page must provide explicit Confirm and Decline actions.
- **FR-045** Accessibility requirements must be optional and collected only when confirming.
- **FR-046** The accessibility control must include short explanatory help and enforce the approved character limit.
- **FR-047** Declining must clear any previously stored accessibility response unless retention is legally required and approved.
- **FR-048** A successful action must show the recorded response and party allocation.
- **FR-049** Existing-response behavior and any Update RSVP action must follow OD-02.
- **FR-050** Expired and locked views must explain that self-service is unavailable and provide the approved organizer contact.
- **FR-051** The form must protect against CSRF and duplicate submission.
- **FR-052** The public experience must be usable on current mobile and desktop browsers and meet the selected accessibility standard.

## Invitation Batches

- **FR-060** A batch must have a stable identifier, display name or number, deadline, and lifecycle metadata.
- **FR-061** CSV import must create a new draft batch rather than immediately send email.
- **FR-062** A batch preview must show valid rows, invalid rows, duplicate concerns, party count, allocated-seat total, and capacity impact.
- **FR-063** Committing a batch must be transactional.
- **FR-064** A later batch may be committed only when its allocations fit within remaining capacity.
- **FR-065** Reminder selection must include only effectively active `Pending` invitations from the chosen batch or explicitly selected scope.
- **FR-066** Expired capacity may be reused by a later batch.
- **FR-067** Extending an earlier deadline after capacity has been reused must be rejected if it would overbook the event.

## Organizer Administration

- **FR-070** All organizer pages and actions must require an authenticated and authorized identity.
- **FR-071** CSV import must accept the canonical fields defined in the integration specification.
- **FR-072** The parser must support standards-compliant quoting, embedded delimiters, Unicode, and configured file-size limits.
- **FR-073** No live invitation party may be persisted until the organizer confirms a valid preview. Protected draft import data may be persisted for review under the approved retention policy.
- **FR-074** The party list must support search, status and batch filters, sorting, and pagination.
- **FR-075** Search must cover at least invitee name, email, and company.
- **FR-076** The dashboard must show party counts and allocated-seat totals by effective status.
- **FR-077** An authorized organizer may correct party metadata subject to audit and email identity rules.
- **FR-078** An authorized organizer may override status only with a reason and only when capacity remains valid.
- **FR-079** The UI must require confirmation for global lock changes, send operations, destructive corrections, and final export.
- **FR-080** The organizer must be able to inspect audit history without exposing secret tokens.
- **FR-081** The organizer must be warned when displayed data became stale before a consequential action is committed.

## Email Operations

- **FR-090** Sending must use a prepared, reviewable recipient set.
- **FR-091** Each rendered invitation must contain the correct party-specific RSVP URL and deadline.
- **FR-092** Templates must have HTML and plain-text representations.
- **FR-093** Personalized values must be safely encoded for their output context.
- **FR-094** Dispatch attempts and provider identifiers must be persisted per recipient.
- **FR-095** Send and retry operations must be idempotent for each approved campaign and recipient.
- **FR-096** Provider limits must be read from current provider documentation or configuration rather than embedded as historical assumptions.
- **FR-097** A provider API acceptance must not be described as confirmed delivery.
- **FR-098** Bounce, complaint, retry, and suppression behavior must follow the approved email policy.
- **FR-099** The system must not claim or imply guaranteed inbox delivery.

## Export and Handoff

- **FR-100** The canonical manifest must contain confirmed parties only.
- **FR-101** The canonical fields must include party name, company when present, email, allocated seats, and accessibility requirements when present.
- **FR-102** A theater adapter must map canonical fields to the agreed CSV headers, ordering, encoding, and representation.
- **FR-103** Export must use standards-compliant CSV escaping.
- **FR-104** Before download, the organizer must see row count, total allocated seats, accessibility-request count, and mapping version.
- **FR-105** Each export run must be auditable and reproducible from its recorded criteria and mapping version.
- **FR-106** Corrections after handoff must follow the agreed theater reconciliation procedure.

## Audit

- **FR-110** Audit events must be append-only through normal application operations.
- **FR-111** An audit event must record event type, timestamp, party or batch reference, outcome, actor category, actor identifier when authenticated, and correlation identifier.
- **FR-112** State changes must also record previous state, requested state, resulting state, and override reason where applicable.
- **FR-113** Page-view auditing must cover active, confirmed/current-response, expired, locked, and invalid-link outcomes at a rate and retention approved by the privacy owner.
- **FR-114** Rejected mutation attempts must record a reason category such as expired, locked, stale, invalid transition, or capacity exceeded.
- **FR-115** Audit and application logs must not contain raw RSVP tokens or unnecessary accessibility text.

## Failure Behavior

- **FR-120** Database failure during a response must produce no partial state change and show a retry-safe error.
- **FR-121** Email failure must not change RSVP eligibility or status.
- **FR-122** Import failure must leave the committed database unchanged.
- **FR-123** Export failure must not change invitation state or global lock state.
- **FR-124** User-facing errors must not expose stack traces, provider credentials, database details, or personal data belonging to another party.
