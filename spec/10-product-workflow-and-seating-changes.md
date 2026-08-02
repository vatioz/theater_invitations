# Product Workflow And Seating Changes

This document records the decisions from the August 2026 product review covering the public RSVP page, batch and campaign workflows, Czech localization, seating, CSV data, and theater export. The functional requirements in [02-functional-requirements.md](02-functional-requirements.md) remain normative.

The ordered implementation for Czech localization, public RSVP, CSV data, batch import, and campaign workflow is defined in [11-ordered-product-change-implementation.md](11-ordered-product-change-implementation.md). Seating and export remain deferred.

## Status And Boundaries

- The decisions in this document are **Agreed** unless marked **Open**.
- These are target requirements, not statements that the current implementation already behaves this way.
- One party remains one recipient and one atomic RSVP for all allocated seats. No separate `+1` identity or partial acceptance is introduced.
- Seating is deliberately isolated from operational RSVP and capacity behavior.
- The theater export wire contract is **Open** pending an actual theater-provided sample or specification.

## Czech-Only Product Experience

The complete public and organizer UI will be Czech-only. Supplied/default email-template content will also be Czech, although organizers may still create multiple versioned templates.

Presentation rules:

- Use formal, neutral `Vy` wording.
- Do not derive Czech vocative forms from imported names.
- Format dates using Czech locale conventions and times with a 24-hour clock.
- Keep UTC instants and the configured event time zone authoritative internally.
- Organizer deadline input and administration retain an explicit time-zone label.
- The public RSVP page omits the time-zone label because the event audience is in one country.
- Set the document language correctly for Czech content and verify Czech plural forms, diacritics, sorting, date formatting, and accessible names.

The translation scope includes navigation, headings, forms, validation, confirmations, empty states, errors, status labels, audit labels intended for display, event settings, import, campaigns, seating, and export. Provider-originated identifiers and technical values are not translated.

## Public RSVP Redesign

### Visual Hierarchy

Use a focused invitation card within the current event visual style. The primary order is:

1. Event title.
2. Event date and venue.
3. Primary guest name using neutral Czech wording.
4. Whole-party allocated-seat count.
5. Exact RSVP deadline.
6. Optional accessibility requirements.
7. Response actions.

Doors time, start time, address, dress code, support contact, and response state remain available where applicable, but must not overpower the decision.

### Interaction

- Provide separate direct Confirm and Decline actions rather than preselected radio buttons.
- Keep the optional accessibility field visible. Persist it only with confirmation.
- Decline opens a short inline confirmation before mutation; do not use a browser-native dialog.
- Disable competing actions while a mutation is in progress and preserve idempotent server behavior.
- A recorded response may be changed until, but not at or after, the deadline. Global lock also blocks changes.
- At or after the deadline, show the recorded response without an update action.
- Declining clears previously stored accessibility requirements.
- Invalid links remain generic and disclose no invitation data.

## Batch Import Workflow

### Target Flow

Persisted import drafts and their organizer UI are removed. Import becomes:

1. Enter batch name and local deadline and choose a CSV.
2. Parse into a temporary server-owned preview.
3. Show row findings, duplicates, ignored columns, party count, allocated-seat total, and projected capacity impact.
4. If any required value or row is invalid, show detailed errors and do not offer commit. The organizer fixes the source file and previews again.
5. If valid, one `Confirm and import` action transactionally revalidates and creates the committed batch, parties, and RSVP tokens.

Temporary preview state may be lost on navigation, restart, timeout, or session loss. That is acceptable because previews are not editable and the source file remains authoritative. No invalid batch or draft rows are retained.

Deadline administration for committed batches remains available and retains its existing capacity recheck, elevated authorization, reason, confirmation, concurrency, and audit rules.

### CSV Contract

Columns are matched by name and may appear in any order.

| Header | Required | Rule |
| --- | --- | --- |
| `primary_guest_name` | Yes | One full display name. Do not attempt to split given name and surname. |
| `email` | Yes | One syntactically valid recipient address. |
| `allocated_seats` | Yes | Positive integer under the configured policy and capacity rules. |
| `company` | No | Trim; blank becomes absent. |
| `priority` | No | Integer 1, 2, or 3; blank or absent defaults to 3. Lower number means higher seating priority. |
| `phone` | No | Trim, length-bound, reject control characters, and preserve remaining formatting. |

Unknown headers are ignored rather than rejected. Preview must prominently list them so misspellings or unexpected data loss are visible. A missing required header is always an error.

Phone is restricted personal data stored for a possible future communication need. This change does not add calls, SMS, consent handling, templates, providers, or automated phone use. Its purpose and retention must be revisited before any operational use. Authorized organizers may correct phone and priority with audit and concurrency protection.

## Campaign And Template Workflow

### Separate Concepts, Fewer Steps

Batches and campaigns remain separate. A batch defines imported parties, deadline, tokens, and capacity. A campaign is an auditable pairing of a batch or explicit recipient scope with one template version and sender configuration.

Multiple templates per type remain supported for experimentation and test sends. Saving a template makes that version immediately selectable; remove the draft-to-approved gate. Test send remains optional and does not mutate guest dispatch state.

### Sending

The routine campaign flow is:

1. Select campaign type, batch or recipient scope, and saved template.
2. Prepare and review rendered content, sender, recipient counts, skipped reasons, and dispatch list.
3. Use one confirmed `Send now` action to start server-side sequential dispatch.

Do not require both `Confirm and queue` and a separately confirmed `Send campaign now`. Internal queued or sending states may still exist for durability, but they are not separate organizer approvals.

Any change after preparation to party data, batch deadline, token, sender, template, or eligibility invalidates the campaign review. The campaign cannot send or continue until it is prepared and reviewed again. This avoids sending materially different content or addresses under stale approval.

### Daily Ceiling

- Enforce the configured provider ceiling transactionally.
- If the allowance is exhausted, transition to a distinct paused state rather than failure.
- Show accepted/sent and remaining counts plus the earliest continuation date or time.
- An authorized organizer may use `Continue` after reset without another confirmation because the unchanged campaign was already approved.
- Continue only unresolved eligible dispatches and preserve idempotency keys.

### Resend

Campaign detail must allow an organizer to select one or more prior recipients and initiate one confirmed, auditable resend campaign. Revalidate active token, current recipient data, suppression, and other eligibility before review/send. A normal resend reuses the current active RSVP link. Token regeneration remains a separate elevated action.

## Isolated Seating Module

### Isolation Contract

Seating is a projection and planning tool over current confirmed parties. It must never:

- Change RSVP status, party allocation, capacity, deadlines, or tokens.
- Run automatically because a guest responds or an organizer edits a party.
- Block import, RSVP, campaign, deadline, capacity, lock, or organizer override operations.
- Show or assign pending, declined, or expired parties.

Source changes may release or invalidate seating assignments and produce warnings, but the source operation succeeds independently.

### Auditorium

- Start new configuration at 18 ordered rows with 18 seats each.
- Allow each row to have a different positive seat count.
- Store configurable, stable theater-recognized row and seat labels.
- Treat row order as front to back.
- After any assignment exists, structural row-length or seat-label changes require clearing all seating first. The confirmation must explain that all automatic and manual assignments will be lost.

The initial agreed model does not include aisles, sections, balconies, inaccessible cells inside a row, or arbitrary two-dimensional drawing. Those require a later approved extension if the real auditorium cannot be represented as variable-length rows.

### Assignment State

Each complete assignment records the party, exact seat identities, assignment source (`Automatic` or `Manual`), lock state, assignment time, and version needed for concurrency. A party has at most one current complete assignment and a seat belongs to at most one current party.

Manual movement automatically locks the complete party assignment. A later run preserves every valid locked assignment and discards/recomputes prior automatic assignments.

If a confirmed party declines, its allocation changes, or another source change makes its locked assignment invalid, release the assignment and flag the party for review. Do not reject the RSVP or organizer edit. The algorithm still runs only when an organizer explicitly starts it.

### Automatic Assignment

Hard constraints:

- Assign only confirmed, currently unassigned parties after preserving valid manual locks.
- Give every assigned party exactly `allocated_seats` contiguous seats in one row.
- Never split a party or create a partial assignment.
- Never overlap assignments.

Optimization order:

1. Maximize the number of completely seated parties. The solver may reorder lower-priority or later-confirmed parties when needed for a better valid packing.
2. Subject to that maximum, give better seat quality to priority 1, then priority 2, then priority 3.
3. Within one priority, prefer earlier confirmation time, then stable party ID.
4. Rank seat quality by front row first and distance of the contiguous block from row center second.

The implementation must define a deterministic final tie-break so identical input and locked assignments produce identical output. If all parties cannot fit, apply valid assignments and report the unseated parties and reasons. Pending parties are hidden rather than displayed as provisional demand.

### Manual Adjustment

The organizer may move a whole party only to a valid contiguous same-row block of the correct size. The UI must reject overlaps, split placement, and wrong-sized placement before saving. Manual adjustment must be keyboard-accessible in addition to any drag interaction.

## Export And Theater Handoff

### Current Decision

Do not build a guessed theater file. Obtain an exact theater contract covering headers, field order, delimiter, encoding and BOM, line endings, quoting, row model, file naming, seat representation, phone, priority, accessibility, and correction procedure. Validate a representative file with the theater before production use.

The canonical snapshot is independent of that adapter and includes current confirmed parties with party name, company, email, optional phone, allocation, accessibility requirements, and current seat labels when assigned. Priority is available to the adapter only if the theater requests it. A theater mapping may output one row per party or one row per seat only after the theater selects the contract; it must not create guest identities the app does not have.

### Generation

- Allow generation at any time from a consistent current snapshot; global lock is not required.
- Preview counts, mapping version, confirmed parties, allocated seats, accessibility requests, and unassigned confirmed parties.
- Warn but allow generation when confirmed parties are unassigned. Never omit those parties silently.
- Require final confirmation.
- Export failure must not change RSVP, seating, capacity, or lock state.
- Retain the exact generated file in protected storage for the event lifetime, together with actor, UTC time, criteria, mapping version, counts, and digest.
- Remove retained export files when the one-off event data is deleted after the event. No separate timed cleanup is required for this event.

## Open External Decisions

The following remain open because they require theater input:

1. Exact export headers, order, delimiter, encoding, BOM, line endings, MIME type, and filename.
2. One output row per party versus per seat.
3. Required representation of row and seat labels.
4. Whether phone, priority, accessibility text, company, email, or an internal reconciliation ID is accepted or required.
5. Formula-injection handling expected by the receiving system.
6. Secure transfer channel and replacement-versus-delta correction process.
7. Whether the variable-row auditorium model matches the real theater layout.

## Acceptance Criteria

1. All user-facing application UI and supplied email defaults are Czech, with neutral formal wording and correct Czech date/time formatting.
2. Public RSVP uses the focused card and separate actions, and no guest mutation succeeds at or after deadline or under global lock.
3. Invalid imports persist nothing; valid previews commit atomically after one confirmation; ignored headers are prominently disclosed.
4. Saved templates need no approval step, and one confirmed campaign action starts sending.
5. Daily-ceiling exhaustion pauses rather than fails a campaign, and Continue resumes only unresolved approved recipients without duplicate sends.
6. Material source changes invalidate an unsent or paused campaign review.
7. Seating supports variable row lengths and stable labels, never splits a party, preserves valid manual locks, and produces deterministic results.
8. Seating failures and invalidations never block source RSVP or organizer operations.
9. Export cannot ship until the theater mapping is approved; afterward, unassigned confirmed parties remain visible and included.
10. Generated exports are protected, auditable, digest-verifiable, and deleted with the event data.
