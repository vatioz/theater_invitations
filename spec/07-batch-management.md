# Batch Management

This document expands implementation gaps `B1`-`B4` from [05-implementation-gaps.md](05-implementation-gaps.md). The functional requirements in [02-functional-requirements.md](02-functional-requirements.md) remain normative.

## Scope

Batch management creates, reviews, commits, and administers invitation waves. A batch remains an organizer-facing grouping; a party remains one email recipient and one atomic RSVP for its complete allocation.

This scope does not send email. B4 establishes the token lifecycle and persistence contracts required before Phase 4 email campaigns are built.

## Common Rules

- Store every deadline as a UTC instant. Organizers enter and view it as a local date and time in the configured event time zone, with the zone visibly labeled.
- Reject nonexistent or ambiguous local times at daylight-saving transitions. Do not silently choose an offset.
- Use the canonical reservation formula: confirmed seats plus pending seats whose batch deadline is strictly later than the authoritative current time.
- Temporary previews do not reserve capacity. Capacity shown during preview is an estimate; import and deadline extension must recalculate capacity transactionally.
- Use PostgreSQL serializable transactions and bounded retry for batch commit, deadline extension, and any other operation that can increase reservation.
- Require server-side authorization. `Operator` may preview and import batches. Only `ElevatedOperator` may change a committed batch deadline because this can change guest eligibility and capacity.
- Require an expected batch version for consequential mutations. A stale request must be rejected and show current data.
- Audit accepted and rejected batch mutations with actor, batch reference, timestamp, outcome, reason category, and correlation ID. Never record raw CSV content, raw RSVP tokens, token hashes, full RSVP URLs, or accessibility text in audit metadata.

## Batch Data Contract

Extend `InvitationBatch` with:

| Field | Notes |
| --- | --- |
| Stable ID | Existing non-sequential UUID. |
| Display name | Organizer-supplied, trimmed, maximum 200 characters. Decide whether active names must be unique. |
| Deadline UTC | Required before a batch is committed or sendable. |
| State | A persisted batch is `Committed`; preview state is temporary and not part of the batch lifecycle. |
| Creation/modification metadata | UTC timestamp and authenticated actor for creation and later changes. |
| Commit metadata | UTC timestamp and actor when transition to `Committed` succeeds. |
| Source digest | Digest of the confirmed upload or canonical parsed representation for audit. |
| Concurrency version | Existing PostgreSQL `xmin` mapping. |

Do not persist draft rows. Keep parsed preview rows in temporary server-owned state and discard them after import, replacement, expiry, restart, or session loss. Never create a live party, RSVP token, or capacity reservation during preview, and do not retain the raw CSV after the import operation.

## B1: Organizer-Supplied Batch Name And Deadline

**Priority:** High
**Status:** In Progress
**Requirements:** FR-020, FR-021, FR-060, FR-061, FR-070, FR-081; OD-06, OD-12, OD-31

### Behavior

The import-start flow requires an authorized operator to supply:

- Batch display name.
- Deadline local date and time.
- Read-only configured event time-zone ID.

The server must trim and validate the name, convert the local deadline to UTC using the event configuration time zone, and reject a missing configuration, invalid zone, ambiguous local time, nonexistent local time, or a deadline that is not later than the authoritative current time.

The organizer UI must display the resulting deadline in local event time and UTC during review. Public RSVP continues to use only the stored UTC instant for eligibility and converts it for display using the configured event zone.

### Confirmation And Audit

Before committing a batch, show the name, deadline in local time and UTC, allocation total, remaining capacity estimate, and an explicit warning that capacity is rechecked at commit. Record batch creation and accepted/rejected deadline validation without source content or secrets.

### Acceptance Criteria

1. A valid name and deadline persist with an unambiguous UTC instant.
2. Blank, whitespace-only, or overlong names are rejected.
3. Ambiguous/nonexistent DST times and non-future deadlines are rejected.
4. Organizer deadline displays include the configured time-zone ID.
5. An unauthorized identity cannot create or edit a batch draft.
6. Public RSVP evaluates eligibility against the committed UTC deadline.

## B2: Temporary Preview And Transactional Import

**Priority:** Medium
**Status:** Open replacement of the implemented persisted-draft workflow
**Requirements:** FR-061, FR-062, FR-063, FR-064, FR-070, FR-073, FR-081, FR-122; OD-03, OD-10, OD-11, OD-12, OD-30, OD-35

### Workflow

1. An operator enters B1 metadata and uploads a canonical CSV.
2. The server parses the upload into temporary server-owned preview state and records no batch or row.
3. The review shows valid rows, detailed invalid-row findings, duplicates, ignored headers, party count, seat total, capacity impact, and current remaining capacity.
4. Invalid preview data cannot be edited in the application or committed. The organizer corrects the source file and previews again.
5. A valid preview offers one explicit `Confirm and import` action. In one serializable transaction, the service authoritatively reparses or revalidates the server-held upload, then rechecks email duplicates, deadline, and current capacity.
6. On success, create the committed batch, every live party, B4 token state, and audit event together. On failure, persist none of them.

### Security And Retention

- Never trust an `ImportPreview`, row total, capacity result, or parsed rows supplied by the client at import time.
- Block duplicate email addresses across committed batches by default. Any future exception requires explicit elevated authorization, a reason, and audit.
- Preview visibility must follow organizer PII permissions; do not expose invitee rows to a role not approved to see invitation data.
- Bound temporary preview lifetime and memory use. Expired, replaced, or lost previews are not importable and require another upload.

### Acceptance Criteria

1. Preview creates no persisted batch, party, token, or draft row and reserves no capacity.
2. Invalid, duplicate, oversized, quoted, Unicode, and multiline input remains uncommittable while showing safe detailed findings.
3. Import is all-or-nothing and revalidates duplicate/capacity state at transaction time.
4. Concurrent imports cannot overbook the event.
5. A duplicate introduced after preview causes rejection with no batch or party insert.
6. An expired or missing temporary preview cannot be imported.
7. Accepted and rejected imports are audited without source content or token data.

## B3: Deadline Administration With Capacity Rechecks

**Priority:** High when multiple batches or deadline extensions are used
**Status:** In Progress
**Requirements:** FR-020, FR-021, FR-026, FR-030 through FR-034, FR-064, FR-066, FR-067, FR-070, FR-079, FR-081; OD-03, OD-06, OD-12, OD-32, OD-33

### Behavior

An elevated organizer may request a new deadline for a committed batch with an expected batch version and mandatory reason. The confirmation view shows the old and requested deadlines in local time and UTC, pending seats that would become active or inactive, confirmed seats, remaining capacity, and a stale-data warning.

The service converts input to UTC and, inside a serializable transaction, recalculates the resulting canonical reservation using the requested deadline. An extension that would exceed capacity is rejected without changing the batch or party state. A shortened deadline releases only pending allocation; confirmed seats remain reserved.

The capacity estimate shown before confirmation is advisory. The transaction result is authoritative.

### Expiration And Reopening Policy

Expiration caused solely by a batch deadline is reopenable. A deadline extension returns only eligible system-expired unanswered parties to `Pending` in the same transaction. Explicit organizer overrides to `Expired` remain terminal. Record the expiration source so the system can distinguish these cases.

### Audit

Audit each accepted or rejected request with prior/requested/resulting deadline, affected pending-seat contribution, reason category such as `capacity-exceeded`, `stale`, `invalid-deadline`, or `batch-not-committed`, actor, and correlation ID. Include affected party count only; do not include party tokens or accessibility data.

### Acceptance Criteria

1. Extension reactivates only the parties allowed by the selected expiration policy.
2. Extension after released capacity has been reused is atomically rejected when it would overbook.
3. Concurrent extension, import/commit, and guest confirmation operations cannot overbook.
4. Shortening immediately removes pending allocation from capacity metrics without changing confirmed allocation.
5. Stale deadline edits are rejected and do not silently overwrite newer changes.
6. DST conversion, authorization, reason, confirmation, and audit rules are enforced.

## B4: RSVP Token Lifecycle For Sending Invitations

**Priority:** High before Phase 4
**Status:** In Progress
**Requirements:** FR-040, FR-041, FR-090 through FR-095, FR-110 through FR-115, FR-121; OD-13 through OD-18, OD-32, OD-34

### Current Defect

The current import path stores random bytes directly as `TokenHash`. Those bytes are not the hash of a token that can be given to an invitee, so imported parties cannot receive a usable RSVP URL. Do not attempt to derive a token from these existing values.

### Token Contract

- Generate a raw token with cryptographically secure random bytes, at least 32 bytes, encoded as base64url without padding.
- Store a unique SHA-256 hash for public lookup. Never store the raw token on the party, batch, audit event, or organizer view model.
- Use a token entity or equivalent state carrying hash, issued time, revoked/replaced time, revocation reason category, and version/reference. Public lookup must accept one active token only by default.
- A revoked or replaced token returns the generic invalid-link state and cannot mutate RSVP.
- Do not log raw tokens, hashes, RSVP URLs, query strings, route values, referrers, or token-bearing exception messages. Keep `Referrer-Policy: no-referrer` and verify access-log/telemetry redaction.

### Delivery Preparation

Hash-only storage cannot render a later invitation email. At batch commit, generate the raw token, persist its hash, and create protected delivery material in the same transaction. The protected material must be encrypted with managed key material, accessible only to the send worker, and used in memory only while rendering a message.

Before delivery, Phase 4 creates an immutable campaign recipient snapshot and one dispatch record per campaign-party. The dispatch uniqueness constraint supports idempotent send/retry. Email failure, token preparation failure, or provider failure must not alter RSVP status, eligibility, or capacity.

Token regeneration is an elevated, audited operation. Recommended default: issue one replacement active token, revoke the prior token immediately, and require an explicit resend campaign. Audit token version/reference and reason only.

### Legacy Import Remediation

Before real delivery, identify parties created with unrecoverable random `TokenHash` values. Generate replacement token state and protected delivery material atomically, then write a sanitized system audit event. Never expose or attempt to reverse existing values.

### Acceptance Criteria

1. A generated raw token resolves only to its intended party; an unrelated, malformed, revoked, or replaced token is generic and invalid.
2. Raw tokens are absent from party/batch persistence, organizer UI, audits, normal logs, and telemetry.
3. Token collisions retry safely without creating duplicate active hashes.
4. Regeneration makes the new token valid and the prior token invalid according to the selected policy.
5. Campaign rendering uses the correct party-specific URL and deadline without internal identifiers.
6. Retrying a campaign does not duplicate a successful dispatch for the same campaign-party pair.
7. Delivery failure does not alter RSVP eligibility, state, or capacity.

## Selected Defaults

1. Keep preview rows temporary and server-owned; persist no draft rows or raw CSV. One confirmed transaction creates the committed batch, parties, and tokens.
2. Require case-insensitive batch display-name uniqueness among non-deleted batches.
3. Allow Operators to create and edit drafts. Require `ElevatedOperator`, reason, and confirmation for committed deadline changes and token regeneration.
4. Reopen only unanswered parties expired by the prior system deadline. Organizer-expired parties remain terminal.
5. Store raw RSVP tokens with their hashes in restricted token records for manual email rendering; exclude them from UI, audit, logs, exports, and errors.

## Recommended Implementation Order

1. B1 batch metadata and organizer-zone deadline input.
2. B2 temporary preview, transactional import, and removal of the persisted-draft UI and lifecycle.
3. B3 deadline administration after the expiration/reopening policy is approved.
4. B4 token issuance/outbox preparation before Phase 4 email delivery.
