# Ordered Product Change Implementation

This document turns the agreed product decisions in [10-product-workflow-and-seating-changes.md](10-product-workflow-and-seating-changes.md) into an implementation sequence for the current application. The functional requirements in [02-functional-requirements.md](02-functional-requirements.md) remain normative.

For the five changes listed here, this document supersedes the historical phase ordering and superseded deliverable wording in [01-phased-implementation.md](01-phased-implementation.md). It does not redefine unrelated completed foundations or production-readiness work.

## Scope And Delivery Order

Implement the following phases in order:

1. Czech localization.
2. Public RSVP redesign.
3. Extended CSV columns and party data.
4. Simplified batch import flow.
5. Simplified campaign flow.

The seating module and theater export are explicitly deferred. Their agreed requirements remain in spec 10 and must not be removed or weakened by work in these phases.

Each phase should be independently reviewable and releasable. A phase is complete only after its migration, tests, Czech user-facing copy, and removal of superseded behavior are complete. New UI introduced after Phase 1 must be Czech from its first implementation.

## Agreed Sequencing Rationale

The following decisions are part of the agreed implementation plan, not optional implementation suggestions.

### Add Party Fields Before Removing Draft Imports

`priority` and `phone` ultimately belong to the live party record used by RSVP, organizer administration, campaigns, and future seating/export work. Their final database columns and validation must therefore exist before the current import pipeline is removed.

This prevents an intermediate application version from accepting the new CSV fields but dropping their values when a draft is committed. If Phases 3 and 4 are deployed separately, the existing draft rows temporarily carry the fields through to live parties. If both phases are deployed together, the temporary draft columns may be skipped and the new preview can write directly to the final live-party fields during confirmed import.

In short: establish the final destination for the new data before replacing the mechanism that delivers data to it.

### Enforce RSVP Deadlines Before Redesigning The Page

The current implementation can allow an already confirmed or declined party to change its response after the deadline. Merely hiding an Update action would leave the server mutation reachable from a stale tab or direct request.

The server must first reject every guest mutation at or after the authoritative deadline while preserving the previously recorded response. Only after that invariant is tested should the redesigned page show or hide controls. The page communicates the rule; the server enforces it.

### Keep Preview Temporary And Recheck The Original Upload

The import preview is temporary server-owned session data, not a persisted draft. During Preview, the server parses the bounded upload, reports findings, and estimates capacity without creating a batch, party, token, or reservation.

The server retains the original bounded upload bytes temporarily. When the organizer confirms, it reparses that original upload and repeats validation against the current clock and database. It must not trust rows, totals, UTC conversions, or capacity results returned by the browser.

This second check is required because valid preview data can become stale. Another operation may consume capacity, create a duplicate email or batch name, or the deadline may pass between preview and confirmation. The confirmed import is the authoritative operation; the preview is organizer guidance.

For the current Interactive Server application, the temporary preview belongs to the organizer's Blazor server session. It may be lost on navigation, timeout, reconnect failure, restart, or deployment. Requiring another upload in those cases is an accepted tradeoff that avoids persistent invalid drafts and unnecessary personal-data retention.

### Simplify Campaign UI Without Removing Send Safety

The organizer should review a campaign and confirm `Send now` once. Removing the separate queue confirmation is agreed, but it requires the send operation to handle four safety concerns behind that simpler UI:

1. **Stale review:** If recipient data, deadline, token, sender, template, rendered event data, suppression, or eligibility changes after review, the prior review is invalid. The campaign must be prepared and reviewed again rather than sending information the organizer did not approve.
2. **Concurrent daily limits:** Every provider attempt must reserve an available daily allowance slot transactionally. Two campaigns starting together must not both count and consume the same remaining allowance.
3. **Expected quota pause:** Reaching the daily ceiling pauses the campaign with processed and remaining counts plus the earliest continuation time. It is not a campaign failure. Continue resumes only unresolved eligible dispatches and cannot resend successful ones.
4. **Traceable selected resend:** Resending selected prior recipients creates a new auditable campaign using current party data and the current active link. Original dispatch history remains unchanged, and the resend uses the same prepare, review, and single-confirmation send flow.

These backend safeguards are what make the reduced-click organizer workflow safe. They must not be omitted while implementing only the visible button changes.

## Cross-Phase Rules

- Preserve one party as one recipient and one atomic response for all allocated seats.
- Keep UTC timestamps and the configured event time zone authoritative. Czech culture controls presentation, not deadline arithmetic.
- Keep capacity-increasing operations transaction-safe.
- Preserve opaque RSVP token handling and keep tokens, URLs, phone values, and accessibility text out of routine logs and audit metadata.
- Use existing optimistic concurrency and authorization patterns for consequential organizer actions.
- Do not introduce seating entities, assignment behavior, export entities, or a provisional theater file in Phases 1–5.
- Run the repository-prescribed restore, build, and full test sequence at every phase exit. PostgreSQL integration tests require Docker.

## Phase 1: Czech Localization

### Goal

Make all application-owned public and organizer UI Czech-only and establish deterministic Czech presentation rules before redesigning workflows.

### 1.1 Configure Czech Culture

Configure `cs-CZ` as the sole supported and default culture for HTTP rendering and Interactive Server circuits.

Required changes:

- Register localization and request-culture behavior in application startup.
- Set the document language to Czech.
- Use Czech locale conventions and a 24-hour clock.
- Continue converting event instants through the configured event time zone.
- Show the configured time-zone label in organizer deadline administration.
- Do not add a language selector, culture cookie, browser negotiation, or English UI fallback.

Culture and time zone must remain separate concepts: `cs-CZ` formats values, while the configured zone determines the correct local instant.

### 1.2 Centralize User-Facing Formatting

Add one presentation layer for values currently displayed through enum `ToString()`, ambient-culture formatting, or duplicated seat wording.

It must provide:

- Czech date and 24-hour time formatting.
- Public event/deadline formatting without a time-zone label.
- Organizer deadline formatting with an explicit time-zone label.
- Czech allocated-seat plural forms, including 1, 2–4, and other counts.
- Czech display labels for invitation, batch, campaign, template, dispatch, audit, and organizer-role values.
- Neutral whole-party wording that does not imply a separately named guest.

Stored enum values, role names, audit identifiers, provider identifiers, CSV headers, and template placeholder names remain technical values and must not be translated in persistence.

### 1.3 Translate Application Copy

Translate all application-owned visible and accessibility copy, including:

- Application shell, navigation, page titles, login, home, and error pages.
- Public RSVP states and support contact.
- Dashboard, parties, batches, email, campaign detail, settings, users, and audit pages.
- Labels, buttons, table headings, help text, validation, confirmations, empty states, errors, status text, and accessible names.
- CSS-generated error text.
- Application-supplied email preview/test values and any supplied/default template content.

Use formal, neutral `Vy`. Do not derive a Czech vocative from `primary_guest_name`.

Do not rewrite organizer-authored event values or existing email-template content. Existing stored technical/audit values receive Czech display mappings rather than data migrations.

### 1.4 Localize Validation And Errors

Do not rely on framework-default English data-annotation or Identity descriptions.

- Supply Czech validation messages through explicit messages or a shared resource catalog.
- Map stable service/Identity error codes to Czech at the presentation boundary where practical.
- Avoid adding new behavior that depends on matching localized exception text.
- Keep provider failure codes available for diagnosis while surrounding UI labels remain Czech.

### 1.5 Verification

Add tests for:

- Effective `cs-CZ` culture and Czech document language.
- Winter and summer UTC-to-`Europe/Prague` presentation.
- 24-hour date/time output.
- Public formatting without and organizer formatting with a zone label.
- Seat pluralization for representative values such as 1, 2, 4, 5, 11, 14, and 21.
- A Czech label for every user-visible enum value.
- Preservation of Czech diacritics.

Search the repository for unintended English user-facing strings. Technical identifiers and user-authored data are expected exceptions.

### Phase 1 Exit Criteria

1. Public and organizer requests and circuits render under `cs-CZ`.
2. The document language is Czech.
3. Every current application-owned UI state, validation path, and accessible name is Czech.
4. Date, time, deadline, status, and allocation formatting is centralized and tested.
5. Supplied email content is Czech; existing organizer-authored content is unchanged.
6. Current name sorting is tested against representative Czech names. Any database-collation change is made only if the deployed PostgreSQL behavior fails the accepted ordering test.

## Phase 2: Public RSVP Redesign

### Goal

Deliver the focused Czech invitation card and direct response actions while correcting the existing server-side after-deadline update defect.

### Dependency

Phase 1 must be complete so the redesigned page is implemented and tested in its final language and formatting model.

### 2.1 Correct Response-Window Enforcement

Fix server behavior before changing controls.

- Reject every guest mutation when the authoritative time is equal to or later than the party deadline, regardless of current status.
- Preserve an existing `Confirmed` or `Declined` response after the deadline; do not rewrite it to `Expired`.
- Continue treating an unanswered pending invitation as effectively expired.
- Apply global lock and deadline checks inside the server mutation.
- Check eligibility before capacity so an after-deadline request cannot incorrectly report capacity exhaustion.
- Preserve idempotency, stale-version rejection, transaction safety, and sanitized rejection auditing.

Use a distinct response-window predicate rather than broadening pending-only expiration logic in a way that destroys recorded responses.

### 2.2 Expose Explicit Public State

The public invitation projection must distinguish:

- Whether the deadline has passed.
- Whether global lock is active.
- Whether a response has already been recorded.
- Whether the guest may currently respond or update.
- Whether event configuration is sufficient for the flow.

Required presentation behavior:

- Pending after deadline: expired view with no mutation controls.
- Recorded response after deadline: show recorded response and context with no update action.
- Pending under global lock: closed view with no controls.
- Recorded response under global lock: show recorded response with no update action.
- Invalid token: generic view with no invitation or event data.

### 2.3 Implement The Focused Invitation Card

Use the current event visual style with this hierarchy:

1. Event title.
2. Event date and venue.
3. Primary guest name in neutral Czech wording.
4. Whole-party allocated-seat count.
5. Exact response deadline.
6. Optional accessibility requirements.
7. Response actions.

Doors time, start time, address, dress code, support contact, and recorded response remain available where applicable but visually secondary. Do not repeat the event title in a second heading. Public date/time output uses the event zone but omits the zone label.

### 2.4 Replace The Response Form

Remove the preselected response radio group and shared submit button.

- Keep the optional accessibility field visible for an editable invitation.
- A direct Confirm action submits `Confirm` with the current accessibility value.
- A direct Decline action first opens an inline Czech confirmation region.
- Final decline submits no accessibility value and clears any previously stored value.
- Provide a cancel action for the decline confirmation.
- Do not use a browser-native confirmation dialog.
- Disable competing controls during a mutation.
- Refresh authoritative invitation state after accepted, stale, expired, locked, capacity, or database results.

The update flow uses the same direct actions and is available only before deadline and without global lock.

### 2.5 Accessibility And Responsive Behavior

- Preserve one clear page-level heading in every state.
- Use semantic buttons, labels, status/error regions, and visible keyboard focus.
- Move focus appropriately when inline confirmation, success, or error state replaces content.
- Support keyboard-only operation and narrow mobile layouts without horizontal scrolling.
- Preserve CSRF protection and expected-version submission.

### 2.6 Tests

Domain and service tests must cover:

- Confirmed and declined parties cannot mutate at or after the exact deadline.
- Rejection preserves recorded status and accessibility data.
- Decline before deadline clears accessibility data.
- Lock/deadline rejection occurs before capacity evaluation.
- Pending expiration remains distinct from a closed recorded response.
- Invalid tokens reveal no invitation data.
- Rejections are audited without tokens or accessibility text.

Rendered-component or browser tests must cover:

- Czech focused-card hierarchy.
- Two direct response actions and no response radio group.
- Accessibility field visible during editing.
- Confirm submits accessibility data.
- Decline requires inline confirmation and cancel works.
- Controls are disabled while submitting.
- Recorded response is editable before but not at/after deadline or under lock.
- Public output contains no time-zone ID.
- Invalid, expired, locked, stale, success, and unavailable states.
- Keyboard, focus, mobile, and automated accessibility checks.

### Phase 2 Exit Criteria

1. Server-side deadline and lock rules cannot be bypassed by a direct request.
2. Recorded responses remain visible but immutable after deadline or under lock.
3. The focused invitation card and direct Confirm/Decline interactions match spec 10.
4. Decline cannot accidentally retain accessibility text.
5. Public component/browser and service tests pass in Czech.

## Phase 3: Extended CSV Columns And Party Data

### Goal

Add the final recognized CSV schema and persist `priority` and `phone` safely before replacing the batch workflow.

### Dependency

Phase 1 is required for Czech parser, correction, and validation messages. Phase 2 has no data dependency on this phase but remains earlier in delivery order.

### 3.1 Final Field Contract

Recognized columns are name-based and order-independent.

| Header | Required | Storage and validation |
| --- | --- | --- |
| `primary_guest_name` | Yes | Trimmed nonblank full display name, maximum 200 characters. |
| `email` | Yes | Trimmed and normalized through the existing email rules, maximum 320 characters. |
| `allocated_seats` | Yes | Positive integer subject to allocation and capacity rules. |
| `company` | No | Trimmed; blank becomes absent; maximum 200 characters. |
| `priority` | No | Integer 1, 2, or 3; blank or absent defaults to 3. |
| `phone` | No | Trimmed; blank becomes absent; maximum 64 characters; reject control characters and preserve all other formatting. |

Unknown columns are ignored, but every unknown header must be returned for prominent preview display. Duplicate recognized headers are invalid. A misspelled required header is both unknown and missing; it must never be silently accepted.

### 3.2 Extend Live Party Persistence

Add to live party persistence:

- Non-null `Priority` with default 3 and a database check constraint limiting values to 1–3.
- Nullable, length-bounded `Phone`.

The migration must backfill existing parties to priority 3. Phone has no current automated communication behavior.

Before adding case-insensitive unique indexes, inspect existing data for collisions. Enforce committed party email and persisted batch-name uniqueness in PostgreSQL as well as application validation so concurrent operations cannot bypass the invariant.

### 3.3 Extract And Harden CSV Parsing

Move parsing out of the general organizer service into a dedicated testable parser.

The parser must:

- Accept a bounded byte stream and enforce the configured file-size limit.
- Accept UTF-8 with or without BOM and reject invalid UTF-8.
- Preserve standards-compliant quoting, embedded delimiters, escaped quotes, Unicode, and multiline fields.
- Match recognized headers by exact canonical name independent of order.
- Keep unknown columns in structural row-width validation while ignoring their values.
- Return document findings, all row findings, and all ignored headers.
- Retain multiple findings for one row rather than one overwritten issue.
- Detect duplicate normalized emails in the upload case-insensitively.
- Detect overflow when counting seats.

### 3.4 Extend Party Administration

Extend organizer party projections and correction input with priority and phone.

- Reuse the same field validation as import.
- Preserve phone formatting after trimming.
- Keep optimistic concurrency and capacity checks.
- Permit authorized correction of priority and phone.
- Show phone only where organizers need to manage the party; avoid unnecessary exposure in broad summaries.
- Audit accepted and rejected corrections without recording the phone value.

### 3.5 Transitional Draft Compatibility

If Phase 3 is deployed separately from Phase 4, the existing persisted draft path must temporarily carry priority and phone so imported values are not lost. Add matching draft-row fields and map them through commit.

If Phases 3 and 4 ship in one coordinated deployment, skip transitional draft columns and wire the parser directly to the temporary preview introduced in Phase 4.

### 3.6 Tests

Parser tests must cover:

- Required and optional columns in arbitrary order.
- Unknown, missing, duplicate, and misspelled headers.
- Optional priority default and valid/invalid priority values.
- Optional phone trimming, preservation, length, and control-character rejection.
- Quoting, commas, escaped quotes, multiline fields, Czech diacritics, BOM, invalid UTF-8, and size limits.
- Missing, blank, overlong, malformed, overflowing, and duplicate values.
- Multiple findings on one row.

Persistence and correction tests must cover:

- Existing parties backfilled to priority 3.
- Database rejection outside priority 1–3.
- Phone and priority round-trip from import and corrections.
- Case-insensitive email and batch-name race protection.
- Stale correction and seat-increase capacity behavior.
- Absence of phone from audit metadata and logs.

### Phase 3 Exit Criteria

1. Live parties persist validated priority and optional phone.
2. The parser implements the six recognized columns in any order and reports ignored headers.
3. Party corrections use the same validation rules.
4. Database constraints protect priority and case-insensitive uniqueness.
5. No active import path can discard priority or phone.

## Phase 4: Simplified Batch Import Flow

### Goal

Replace persisted draft batches with one temporary server-owned preview and one confirmed transactional import while retaining committed-batch deadline administration.

### Dependency

Phase 3 must be complete. The temporary preview uses its parser and final party schema.

### 4.1 Introduce A Dedicated Import Service

Separate batch import from the general organizer service. It must expose two conceptual operations:

1. Preview a bounded upload with batch name and organizer-entered local deadline.
2. Confirm a valid preview by opaque preview ID.

The client must never submit trusted parsed rows, calculated totals, a calculated UTC deadline, or capacity results for confirmation.

### 4.2 Temporary Server-Owned Preview

Use a circuit-scoped server-owned preview store for the current Interactive Server architecture.

- Bind a preview to its authenticated organizer.
- Keep at most one active preview per import flow.
- Retain the original bounded upload bytes so confirmation can authoritatively reparse them.
- Use an opaque random preview ID.
- Apply a short bounded lifetime and remove state on replacement, confirmation attempt, expiry, or circuit loss.
- Accept that navigation, restart, reconnect failure, or session loss may require another upload.
- Do not persist a batch, party, token, draft row, or raw CSV during preview.

The rendered preview includes only the data needed by the authorized organizer. Preview IDs and source data must not enter logs or audit metadata.

### 4.3 Preview Content

Show:

- Batch name.
- Deadline in configured local time, UTC, and with the organizer time-zone label.
- All valid and invalid rows through paging or virtualization.
- All row/document findings.
- Duplicate emails within upload and against committed parties.
- Prominent ignored-header warning.
- Party, valid-row, and invalid-row counts.
- Total allocated seats.
- Current reserved and remaining capacity.
- Projected remaining capacity.
- Warning that deadline, duplicates, source, and capacity are rechecked at import.

Invalid preview data cannot be edited in the app and cannot be imported. The organizer corrects the source file and previews again.

### 4.4 Confirm And Import

One Czech `Confirm and import` action must:

1. Reauthorize the organizer and load the unexpired actor-owned server preview.
2. Enter the existing bounded serializable transaction retry.
3. Reparse the original upload and revalidate every field.
4. Recheck batch-name and email uniqueness, deadline, event configuration, reserved capacity, and current capacity.
5. Create the batch directly as committed.
6. Create every party with priority and phone.
7. Create active RSVP token records and party token hashes.
8. Persist source digest and accepted audit event without source or personal values.
9. Commit everything together or persist none of the batch, party, or token records.
10. Consume the preview so a repeated click cannot create another batch.

After a rejected confirmation, require a new preview. Record a separate sanitized rejection audit after rollback when required, using stable reason categories rather than source values.

### 4.5 Replace The Organizer UI

Remove:

- Create/save draft wording.
- Saved draft list and reopen behavior.
- Draft and prepared state badges.
- Delete-draft actions.
- Separate commit-draft step.

Keep:

- Committed batch list.
- Elevated committed-deadline administration with reason, confirmation, capacity recheck, concurrency, and audit.

Disable upload replacement and confirmation while an operation is running. Clear the old preview when the source or metadata changes. On success, clear the form and refresh committed batches.

### 4.6 Contract Persisted Draft Storage

Use expand/switch/contract deployment when mixed application versions may run:

1. Deploy code and schema capable of the new preview flow.
2. Stop creating persisted drafts and verify no old instance remains.
3. Delete legacy uncommitted batches and their draft rows after an explicit operational check; they cannot be promoted automatically.
4. Drop the draft-row table, draft validation fields, draft-only mappings, services, DTOs, UI, reset SQL, and obsolete tests.
5. Remove `Draft` and `Prepared` enum values after confirming no persisted rows use them. Persisted import creates only `Committed`.

Historical draft audit event strings may remain as history and receive Czech display labels.

### 4.7 Tests

Preview tests must prove:

- No import-domain records or capacity reservation are written.
- Invalid rows, duplicate categories, ignored headers, counts, and capacity impact are returned.
- Invalid previews cannot be confirmed.
- Replacement, expiry, actor ownership, and size bounds are enforced.

Import tests must prove:

- One committed batch, all parties, and all token records are created atomically.
- Priority defaults and phone values persist correctly.
- Deadline, duplicate, name, and capacity changes after preview are detected.
- A consumed, expired, missing, or foreign preview cannot import.
- Double submission cannot create duplicate records.
- Rejected import leaves no batch, party, or token changes and records only sanitized audit data.

PostgreSQL integration tests must cover concurrent capacity, duplicate email, and case-insensitive batch-name races. Migration verification must prove committed history is preserved while legacy draft storage is removed.

### Phase 4 Exit Criteria

1. Preview writes no batch, party, token, draft row, or capacity reservation.
2. One confirmation performs an authoritative all-or-nothing import.
3. The organizer UI exposes no persisted-draft lifecycle.
4. Legacy uncommitted data is handled explicitly and obsolete draft schema/code is removed.
5. Committed deadline administration continues to work unchanged in authority and safety.

## Phase 5: Simplified Campaign Flow

### Goal

Keep batches and campaigns separate while removing redundant approvals, making review freshness authoritative, handling the daily ceiling as a pause, and adding selected-recipient resend from campaign detail.

### Dependency

Phase 4 must be complete so campaign audiences are created only from the final committed-batch workflow. Phase 1 requires all new campaign UI and messages to be Czech.

### 5.1 Remove Template Approval

- Replace template `Draft`/`Approved` behavior with `Active`/`Retired` behavior.
- Saving a valid immutable template version makes it immediately active and selectable.
- Keep test send optional and isolated from guest campaign dispatches.
- Preserve content digests and verify the selected version has not changed when rendered or sent.
- Remove approval actions, approval-only filters, and approval wording.
- Retain historical approval metadata during an expand deployment if needed; remove obsolete columns only after old application instances are gone.

Existing non-retired draft and approved versions migrate to active. Existing retired versions remain retired. Enum integer migrations must be explicit because these states are persisted numerically.

### 5.2 Add Review Validity And Invalidation

A prepared campaign review must bind safely to the material inputs displayed or used for sending, including:

- Campaign type and selected party scope.
- Current party identity, email, allocation, status, and eligibility.
- Batch and deadline.
- Current active token reference and available delivery material.
- Sender identity and verification state.
- Template ID, version, and digest.
- Event/support values rendered into the message.
- Suppression state when implemented.

Store a safe deterministic review fingerprint or equivalent version set that contains no raw token, token hash, URL, message body, phone, or accessibility text.

Use both protections:

- Proactively invalidate unsent or paused campaigns in known material mutation transactions.
- Authoritatively validate review freshness immediately before send, continue, and each unresolved dispatch.

Natural deadline passage must also invalidate eligibility even without a database mutation. An invalidated campaign cannot send or continue; unresolved recipients require a newly prepared replacement campaign. Accepted dispatch history remains immutable.

Before enabling selected-recipient resend, persist and query normalized suppression state if it is not already available. This phase must honor every suppression known to the application. Provider webhook ingestion may be delivered separately, but no known complaint, permanent bounce, or manual suppression may be bypassed by send, continue, retry, or resend.

### 5.3 Replace Queue And Send With One Approval

Target organizer flow:

1. Select campaign type, committed batch or explicit scope, and active template.
2. Prepare and review content, sender, recipients, and skipped reasons.
3. Confirm one `Send now` action.

The server action must atomically:

- Require `ReadyForReview` and the expected campaign version.
- Revalidate review freshness.
- Record confirmer and confirmation time.
- Transition into server-side execution.
- Commit transition/audit before provider calls.
- Prevent repeated clicks from starting a second execution.

Remove the organizer-visible `Confirm and queue` step and its second confirmation. An internal durable state may coordinate execution, but it is not a separate approval or button.

### 5.4 Implement Safe Sequential Execution

Keep provider calls server-side and sequential for this one-off application. A background scheduler is not required by this phase; `Send now` and `Continue` may invoke the same server executor.

Before each provider call:

- Claim the unresolved dispatch so concurrent requests cannot process it twice.
- Revalidate campaign and recipient eligibility.
- Reserve one configured daily-allowance slot transactionally.
- Commit the claim/reservation before the external request.

After the provider call, persist the normalized result and provider message ID. Use the existing stable campaign-party idempotency key for retries and crash reconciliation. Never hold a database transaction open during the provider HTTP request.

The persistence model must distinguish an in-flight claim/reservation from accepted delivery so concurrent campaigns cannot exceed the ceiling and a crash can be reconciled safely.

### 5.5 Pause At The Daily Ceiling

Add a distinct `PausedDailyLimit` campaign state and persisted pause/continuation timestamps.

- If no daily slot is available, make no provider call.
- Show accepted/sent, failed, and unresolved/remaining counts.
- Show the earliest continuation instant in Czech local presentation.
- An authorized organizer may use `Continue` at or after reset without another confirmation.
- Continue revalidates the unchanged campaign, transitions back into execution, and processes only unresolved eligible dispatches.
- Continue preserves dispatch rows and idempotency keys.
- Continue never retries accepted/delivered or terminal failed/suppressed records; retries remain explicit operations.

Define the provider quota reset boundary from current provider/account documentation or approved configuration. Do not infer it from the event time zone. Provider HTTP rate limiting is a provider result and must not be confused with the configured daily ceiling.

### 5.6 Add Selected-Recipient Resend

Campaign detail must allow selection of one or more prior dispatch recipients and preparation of a new auditable `Resend` campaign.

- Verify every selected dispatch belongs to the source campaign.
- Deduplicate the selected party scope.
- Load current party data and current active token rather than copying stale recipient data.
- Apply current deadline, status, address, token, and suppression eligibility.
- Record skipped recipients and safe reason categories in review.
- Reuse the current active RSVP link for normal resend.
- Create new dispatches and new campaign-party idempotency keys.
- Retain source-campaign lineage.
- Preserve original dispatches and provider IDs as immutable history.
- Send the resend only through the same prepare, review, and single `Send now` flow.

The eligible status policy must match current RSVP behavior: a party may be resent only while its guest response window is open and the selected template/campaign type is valid for its current status. Expired, revoked-token, invalid-address, or suppressed recipients are never eligible.

The existing party-level resend, if retained, must call this same collection-based preparation path rather than remain a second implementation.

### 5.7 State And Migration Rules

Target campaign states are:

```text
ReadyForReview -> Sending -> Completed
                     |----> PausedDailyLimit -> Sending
                     |----> PartiallyFailed
                     |----> Failed
ReadyForReview -> Invalidated
ReadyForReview -> Cancelled
PausedDailyLimit -> Invalidated
```

Preserve explicit numeric values for existing persisted enum states or remap them in SQL. Append new values rather than accidentally reinterpreting completed history.

During rollout:

- Quiesce real sending and inspect nonterminal campaigns.
- Preserve terminal history and accepted dispatches.
- Invalidate legacy `ReadyForReview`, `Queued`, and unreconciled `Sending` campaigns rather than assuming old approval under the new workflow.
- Reprepare unresolved recipients under the new review rules.
- Add new nullable timestamps/fingerprint/state fields first; remove queue/approval-only fields in a later contract migration after old instances are gone.

### 5.8 Tests

Template and flow tests must cover:

- Saved template is immediately active; no approval action exists.
- Retired template cannot prepare a campaign.
- One action moves a valid reviewed campaign into execution.
- Duplicate/stale clicks start execution once.
- New code never creates an organizer-visible queued step.
- Template digest mismatch and each material source change invalidate review.
- Natural deadline passage invalidates eligibility.

Quota and continuation tests must cover:

- Ceiling reached before a provider call pauses the campaign.
- Partial available allowance sends only the available count and pauses.
- Continue before reset is rejected and at/after reset resumes.
- Accepted dispatches are never resent and unresolved idempotency keys remain unchanged.
- Source change while paused invalidates rather than resumes.
- Concurrent PostgreSQL senders cannot reserve beyond the ceiling.
- Crash/retry reconciliation reuses the idempotency key.

Resend tests must cover:

- Empty, duplicate, foreign, eligible, and ineligible selections.
- Current party data and active token are used.
- Original dispatch history remains unchanged.
- Suppressed, expired, invalid-address, and token-unavailable recipients are skipped.
- Source lineage, skipped reasons, audit, and new idempotency keys are correct.
- No token or URL enters organizer DTOs, audit, or logs.

Rendered/browser tests must prove the Czech UI has:

- No template approval action.
- One send confirmation only.
- Paused counts, reset time, and Continue behavior.
- Invalidated-state explanation.
- Accessible recipient selection and resend confirmation.

### Phase 5 Exit Criteria

1. Saving a template makes it immediately usable without approval.
2. A reviewed campaign requires exactly one organizer confirmation to start sending.
3. Material changes and natural eligibility changes prevent stale sends.
4. Daily ceiling enforcement is transaction-safe and pauses rather than fails the campaign.
5. Continue resumes only unresolved approved recipients without another confirmation or duplicate send.
6. Campaign detail can prepare a selected-recipient resend through the same review/send workflow.
7. Legacy nonterminal campaigns are reconciled without changing accepted history.

## Deferred Work

### Seating

The isolated seating module in spec 10 is not part of Phases 1–5. Phase 3 adds `priority` because it is part of the agreed imported party data and is required by future seating. No seating layout, seat assignment, algorithm, locking, or visualization should be implemented yet.

### Export

The theater export in spec 10 is not part of Phases 1–5. Phase 3 adds phone and priority to the canonical party data, but no export format should be guessed. Export implementation remains blocked on the theater contract in OD-19.

## Required Verification Per Phase

Run:

```powershell
dotnet restore TheaterInvitations.sln
dotnet build TheaterInvitations.sln --no-restore
dotnet test TheaterInvitations.sln --no-build
```

The full test command includes PostgreSQL Testcontainers integration tests and requires a running Docker daemon. Apply and verify EF migrations against PostgreSQL before each phase is considered releasable.
