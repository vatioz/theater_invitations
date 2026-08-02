# Template-Specific Sender Display Name

This document defines the implementation change that moves the email `From` display name from application-wide sender settings to each immutable email-template version. The functional requirements in [02-functional-requirements.md](02-functional-requirements.md) remain normative.

## Goal

Allow organizers to send different company-facing campaigns with different visible sender names while retaining one application-wide, Resend-bound sender address and one application-wide organizer Reply-To address.

Company segmentation itself remains an organizer workflow: organizers use separate batches or explicit recipient scopes and select the matching template. This change does not automatically select a template from `InvitationParty.Company` and does not permit multiple sender addresses or Reply-To addresses.

## Selected Design

| Concern | Owner | Behavior |
| --- | --- | --- |
| From display name | Immutable email-template version | Required literal display text selected with the template. |
| From address | Application-wide sender configuration | Required verified Resend-bound address used by every live and test send. |
| Reply-To address | Application-wide sender configuration | Required monitored organizer mailbox used by every live and test send. |
| Daily send ceiling | Application-wide sender configuration | Unchanged. |
| Domain verification marker | Application-wide sender configuration | Unchanged and continues to guard campaign preparation and test sends. |
| Campaign sender identity | Immutable campaign snapshot | Copies the template display name plus the application-wide From and Reply-To addresses at preparation time. |

`EmailCampaign.FromDisplayName`, `EmailCampaign.FromAddress`, and `EmailCampaign.ReplyToAddress` remain historical snapshots. Existing campaign history must not be rewritten when a template version or application-wide setting changes.

## Invariants

1. Every live campaign provider message uses the sender identity snapshotted from `template.FromDisplayName <settings.FromAddress>` and `settings.ReplyToAddress` when the campaign was prepared. Test sends resolve the same combination directly from the selected template and current settings.
2. A template cannot override the sender email address or Reply-To address.
3. A display name is literal template metadata. It does not support placeholders or per-recipient rendering.
4. Changing a display name requires creating a new immutable template version; it must not mutate a template version referenced by campaign history.
5. Test send and live campaign preparation resolve sender identity by the same rules.
6. Campaign review shows the exact display name, From address, and Reply-To address that will be sent.
7. Material sender changes after campaign preparation invalidate the review under the existing review-freshness rules.

## Data Model

### `EmailTemplate`

Add:

| Field | Rules |
| --- | --- |
| `FromDisplayName` | Required for active templates; trimmed; 1-200 characters; Unicode allowed; no CR, LF, or control characters. |

Include `FromDisplayName` in the template content digest. The digest must continue to cover the template type, subject, HTML body, and plain-text body. Template name remains organizer metadata rather than message content.

Template summaries and template input DTOs must expose `FromDisplayName` so list, selection, preview, test-send, and campaign-preparation UI can display it.

### `EmailSenderConfiguration`

The target model removes `FromDisplayName`. It retains:

- `FromAddress`.
- `ReplyToAddress`.
- `DailySendCeiling`.
- Domain-verification metadata.
- Concurrency version.

The old database column may remain temporarily during an expand/contract deployment, but new application code must not use it as a runtime fallback. Keeping a fallback would make message identity depend on deployment timing and weaken review reproducibility.

### `EmailCampaign`

No sender-snapshot columns are removed. On preparation:

```text
EmailCampaign.FromDisplayName = EmailTemplate.FromDisplayName
EmailCampaign.FromAddress = EmailSenderConfiguration.FromAddress
EmailCampaign.ReplyToAddress = EmailSenderConfiguration.ReplyToAddress
```

Provider execution continues to use only the campaign snapshot, not live template or sender-setting values. Existing dispatch and idempotency behavior is unchanged.

## Validation And Safety

- Require a nonblank display name no longer than 200 characters.
- Reject CR, LF, null, and other control characters to prevent mail-header injection.
- Permit Czech diacritics and other normal Unicode display text.
- Treat the value as literal text; reject or do not interpret template placeholders in this field.
- Format the provider mailbox through a safe address formatter or equivalent adapter behavior. Do not rely on unescaped string concatenation for names containing quotes, commas, or other mailbox syntax characters.
- Continue normalizing and validating the application-wide From and Reply-To addresses with the existing email-address policy.
- Never permit a template submission to carry hidden From-address or Reply-To fields.

## Organizer Workflow

### Template Creation

Add a required Czech-labeled sender-display-name input to template creation. Explain that:

- The value is the name recipients see as the sender.
- The sender address and Reply-To are controlled centrally.
- A separate template version should be created for each company-facing identity.

Template list rows and selection options should show the display name sufficiently clearly to prevent choosing the wrong company identity. The existing template type, name, version, state, and subject remain visible.

`Operator` and `ElevatedOperator` already have template authority; therefore both roles may choose the template-specific display name. Only `ElevatedOperator` may change the application-wide From address, Reply-To, daily ceiling, or verification marker.

### Sender Settings

Remove the display-name input and display from application-wide email settings. Update Czech help text to state that the display name is configured on each template and that the address and Reply-To apply to all templates and campaigns.

### Preview And Test Send

Template preview must show the complete resolved sender identity:

```text
Template display name <application-wide From address>
Reply-To: application-wide Reply-To address
```

A test send uses the selected active template's display name and the current application-wide addresses. It must fail before a provider call when sender settings are absent/unverified or the selected template lacks a valid display name.

### Campaign Review And Resend

Initial invitation, reminder, and selected-recipient resend preparation all use the selected template's display name. Selecting a different template for a resend also selects that template's display name.

Campaign detail continues to show the immutable campaign sender snapshot. Historical campaigns require no special rendering path.

## Review Freshness

The review fingerprint or equivalent authoritative version set must bind:

- Template ID, version, state, content digest, and `FromDisplayName`.
- Campaign sender snapshot.
- Current application-wide From address, Reply-To address, and verification state.
- All previously required event, batch, recipient, token, suppression, deadline, and eligibility material.

The removed application-wide display-name field must no longer participate in new fingerprints. A change to the application-wide From address, Reply-To address, or verification state invalidates unsent and paused campaigns. A new template version with a different display name does not alter an already prepared campaign, but organizers must prepare a new campaign to use it.

## Migration And Rollout

Use an expand/contract migration so schema deployment does not reinterpret history.

### Expand

1. Add nullable `EmailTemplates.FromDisplayName` with a 200-character limit.
2. Backfill every existing template from the singleton `EmailSenderConfigurations.FromDisplayName` when that value exists and is valid.
3. Recompute `EmailTemplate.ContentDigest` with the new canonical input, including the backfilled display name. Invalidate existing nonterminal campaign reviews and require them to be prepared again; do not reinterpret them under the new digest.
4. Deploy code that requires `FromDisplayName` for every newly created template and refuses to prepare or test an active template without it.
5. Keep `EmailSenderConfigurations.FromDisplayName` only as legacy migration data; new runtime paths ignore it.
6. Do not modify any `EmailCampaign` sender snapshot or its stored template digest.

If no valid legacy display name exists, do not invent one. Retire or make the affected template unavailable for new preparation until an organizer creates a replacement template version with an explicit display name. Report the affected template count during migration/release verification.

### Contract

After all templates usable for future sending have a display name and no old application instances remain:

1. Enforce that every active template has a valid `FromDisplayName`. Keep the column nullable for retired historical templates that could not be safely backfilled.
2. Remove `EmailSenderConfigurations.FromDisplayName` and obsolete DTO/UI/service fields.
3. Verify no application query, fingerprint, test-send path, or provider adapter reads the removed setting.

The deployment runbook must capture the legacy display name before migration, list templates that could not be backfilled, and require controlled test sends for each intended company-facing display name.

## Service Changes

Update all sender-resolution paths, not only initial campaigns:

- Template creation and summary projection.
- Initial-invitation preparation.
- Reminder preparation.
- Party-level and selected-recipient resend preparation.
- Test send.
- Campaign preview/detail projection.
- Review-fingerprint calculation and revalidation.
- Provider message construction or mailbox formatting.
- Sender settings read/write DTOs and organizer settings UI.

Keep sender resolution in one service-level path where practical so test and campaign workflows cannot drift.

## Audit And Privacy

- Continue auditing template creation and sender-setting changes using safe event types and references.
- Do not place rendered bodies, RSVP URLs, tokens, provider credentials, or query strings in audit metadata or logs.
- The display name is not secret, but audit correctness should rely on the immutable template and campaign records rather than copying free text into audit metadata.
- Existing dispatch history and provider message IDs remain immutable.

## Tests

### Unit And Service Tests

- Template creation requires, trims, persists, and returns `FromDisplayName`.
- Blank, overlength, CR/LF, null/control-character, and placeholder-style display names are rejected according to the validation policy.
- Unicode and Czech diacritics are accepted.
- Template content digest changes when the display name changes.
- Initial, reminder, resend, and test-send paths resolve the display name from the selected template.
- All paths resolve From address and Reply-To only from application-wide settings.
- A template cannot override either address.
- Provider mailbox formatting safely handles punctuation in a valid display name.
- Campaign preparation snapshots all three resolved values.
- Provider execution uses the campaign snapshot and does not reread current values for an accepted campaign.
- Changing global From address, Reply-To, or verification invalidates review.
- Creating a later template version does not mutate a prepared or historical campaign.

### Migration Tests

- Existing templates are backfilled from the legacy app-wide display name.
- Backfilled template digests are recomputed and nonterminal campaign reviews are invalidated for explicit repreparation.
- Existing campaigns retain their original sender snapshots.
- Missing legacy configuration does not produce a fabricated display name.
- Templates without a successful backfill cannot be used for new test sends or campaigns.
- The contract migration succeeds only after all active templates have valid display names.

### Rendered And Browser Tests

- Czech template form requires the display name and explains the global address behavior.
- Template list and selector distinguish templates with different company-facing display names.
- Settings no longer offer an app-wide display-name edit.
- Preview, test-send confirmation, and campaign review show the resolved sender and Reply-To.
- Mobile and desktop layouts remain usable with long display names.

### Integration Tests

- A provider test double receives `Company A <verified@domain>` and the global organizer Reply-To for a Company A template.
- A second template can send as `Company B <verified@domain>` without changing application-wide settings.
- Concurrent campaign preparation cannot mix a template display name with stale global addresses without review invalidation.

## Acceptance Criteria

1. An authorized organizer can create two active templates with different From display names and use them for separate batches or selected scopes.
2. Both templates use the same application-wide verified From address and organizer Reply-To address.
3. Initial, reminder, resend, preview, and test-send flows consistently use the selected template's display name.
4. Campaign review shows and persists the exact resolved sender identity before sending.
5. Historical campaigns and dispatches retain their original sender snapshots after settings or templates change.
6. Existing usable templates migrate to the former app-wide display name without rewriting campaign history.
7. No missing legacy value is replaced by an invented sender identity.
8. Header injection is rejected and provider mailbox syntax is formatted safely.
9. The change adds no automatic company routing, per-template From address, or per-template Reply-To behavior.

## Recommended Implementation Order

1. Add the nullable template field and safe legacy backfill migration.
2. Add validation, digest coverage, DTOs, and template UI.
3. Centralize sender resolution and update test, initial, reminder, and resend paths.
4. Update review fingerprinting, preview, campaign review, and provider formatting.
5. Remove the app-wide display-name UI and runtime DTO usage.
6. Run unit, browser, and PostgreSQL migration/integration tests plus controlled Resend test sends.
7. Apply the contract migration after production data and old-instance checks pass.
