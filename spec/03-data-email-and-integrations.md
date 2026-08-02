# Data, Email, and Integrations

This document defines conceptual contracts. Exact table names, framework types, and provider SDK calls are implementation details.

## Conceptual Data Model

### EventConfiguration

- Event identifier and display details.
- Configured capacity.
- IANA or Windows time-zone identifier selected consistently for the deployment platform.
- Global RSVP lock and lock metadata.
- Organizer support contact.
- Accessibility text limit.
- Version or concurrency token.

### InvitationBatch

- Stable identifier and organizer-facing number or name.
- Deadline stored as UTC.
- Draft, prepared, sending, completed, or otherwise approved operational state.
- Created and modified metadata.
- Optional import source metadata and content digest.

### InvitationParty

- Stable internal identifier.
- Opaque public RSVP token represented by a securely stored hash where practical.
- Batch reference.
- Primary guest name, email, and optional company.
- Positive allocated-seat count.
- `Pending`, `Confirmed`, `Declined`, or `Expired` status.
- Optional accessibility requirements.
- Response and modification timestamps.
- Concurrency token.

One party maps to one email recipient. A `+1` must not be represented as a duplicate party row.

### AuditEvent

- Immutable identifier and timestamp.
- Event type, outcome, actor category, and actor identifier where available.
- Party, batch, event, email dispatch, or export references as applicable.
- Correlation identifier.
- Structured non-secret metadata.

Audit history should be stored as rows rather than a growing JSON array on the party record. This supports querying, retention, concurrency, and append-only behavior.

### EmailCampaign and EmailDispatch

- Campaign type, template version, audience criteria or snapshot, and creator.
- One dispatch per campaign-recipient pair.
- Provider message identifier and normalized delivery state.
- Attempt count, timestamps, and sanitized failure category.
- Unique constraint supporting idempotent send behavior.

### ExportRun

- Actor, timestamp, manifest criteria, and mapping version.
- Row count, allocated-seat total, and accessibility-request count.
- File digest and optional protected storage reference.

## Important Constraints

- `allocated_seats > 0`.
- Party email is required and normalized for comparison without destroying the original display value.
- A batch deadline is required before invitations become sendable.
- Capacity-affecting operations use a database transaction and concurrency strategy.
- Public token lookup is indexed and does not expose sequential identifiers.
- Email dispatch is unique by campaign and party.
- State values use a database constraint or mapped enum.

## Capacity Transaction Strategy

The implementation must serialize or otherwise protect operations that can increase reserved capacity. Candidate approaches include locking the single event configuration row during the calculation or using serializable transactions with bounded retry.

The chosen approach must cover:

- Batch commit.
- Reopening or extending an invitation.
- Manual override to `Pending` or `Confirmed`.
- Increasing allocated seats.
- Concurrent guest responses when response changes can increase reserved capacity.

UI-only validation is insufficient.

## CSV Import Contract

### Canonical Input Fields

| Field | Required | Notes |
| --- | --- | --- |
| `primary_guest_name` | Yes | Human-readable invitee or party contact name. |
| `email` | Yes | One recipient address per party. |
| `company` | No | Preserved as provided after trimming. |
| `allocated_seats` | Yes | Positive integer, normally 1 or 2. |
| `priority` | No | Integer 1, 2, or 3 used by seating; defaults to 3. |
| `phone` | No | Restricted future-purpose contact data; trimmed and preserved without guessed normalization. |

Batch and deadline are supplied by the import workflow, not repeated in every row unless a later contract explicitly requires it.

### Validation

- Require a supported encoding and standards-compliant CSV structure.
- Trim surrounding whitespace while preserving meaningful Unicode and diacritics.
- Validate email syntax without claiming that syntax proves deliverability.
- Reject missing names, emails, or seat counts.
- Reject non-integer, zero, negative, or policy-exceeding seat counts.
- Surface duplicate emails within the upload and against existing invitations.
- Treat duplicate handling as an organizer decision, never silently merge rows.
- Calculate total allocated seats and resulting remaining capacity before commit.
- Keep preview data temporary and discard it according to the approved retention policy.
- Match recognized columns by name independent of order. Prominently warn about and ignore unknown columns; never silently accept a missing required header.

## Canonical Manifest and Theater Export

Internally, the confirmed manifest has stable semantic fields:

- Primary guest name.
- Company.
- Email.
- Allocated seats.
- Accessibility requirements.
- Phone when present.
- Current physical-seat labels when assigned.
- Priority only if required by the approved theater mapping.
- Internal party reference, included only if approved for reconciliation.

A versioned export mapping converts this model to the theater's required headers, column order, encoding, delimiter, and party representation. The adapter may duplicate or transform rows only if the theater contract demands it; such transformation must not alter the application's atomic party model.

## Resend Integration

### Account and Domain

- Use an organization-owned Resend account.
- Send from a verified organization-owned domain or subdomain.
- Configure the DNS records currently required by Resend, including DKIM and SPF-related records as applicable.
- Define and publish a DMARC policy appropriate to the organization's broader mail posture.
- Configure one approved application-wide `From` address and organizer `Reply-To`; configure the visible From display name on each immutable template version.
- Store API credentials in protected application configuration or a managed secret store.

No provider can guarantee inbox placement. Authentication, reputation, relevant content, clean lists, low complaint rates, and gradual testing improve deliverability.

### Rendering

- Version invitation and reminder templates.
- Produce both HTML and plain-text bodies.
- Include event identity, party allocation, deadline, RSVP link, and support contact.
- Escape guest-provided and imported values.
- Do not expose internal identifiers when an opaque token is sufficient.
- Preview representative long names, diacritics, one-seat and multi-seat parties, and common email clients.

### Dispatch

- Persist the campaign and intended recipients before contacting the provider.
- Use a durable background process or recoverable job model for production sends.
- Chunk requests according to current provider API limits.
- Bound concurrency and honor retry-after responses.
- Retry transient failures with backoff; do not retry permanent address failures automatically.
- Store provider message identifiers and sanitized responses.
- Make retry behavior idempotent per campaign and recipient.

Provider limits, SDK behavior, plan names, and pricing must be verified during Phase 4. Historical figures from planning discussions are not requirements.

### Delivery Events

If enabled, a public webhook endpoint must:

- Verify webhook authenticity using the provider's current mechanism.
- Handle duplicate and out-of-order events idempotently.
- Normalize accepted, delivered, delayed, bounced, complained, and related provider states.
- Avoid logging full message bodies or RSVP URLs.
- Apply the approved suppression and organizer-notification policy.

## Azure Hosting Assumptions

- The current target is one Azure App Service-hosted ASP.NET Core application.
- Organizer routes use App Service authentication plus application-level authorization.
- Public RSVP routes permit anonymous access and authorize possession of a valid token only for that party.
- PostgreSQL connections require encryption and least-privilege credentials or supported managed identity.
- Environment-specific configuration separates development, staging, and production.
- Health, structured logging, telemetry, backup, and restore behavior are required before production.

Infrastructure-as-code and deployment topology will be specified separately when hosting decisions are approved.

## Security and Privacy

### RSVP Tokens

- Generate tokens with a cryptographically secure random source and sufficient entropy.
- Never use an email address or sequential UUID alone as proof of authorization.
- Avoid logging tokens, including query strings and referrer data.
- Set an appropriate referrer policy on RSVP pages.
- Support revocation and regeneration after suspected disclosure.

### Organizer Access

- Authentication alone is insufficient; restrict access to an approved group, role, or allowlist.
- Apply least privilege to import, send, override, lock, export, and audit actions.
- Reauthorize or strongly confirm high-impact actions where appropriate.

### Personal and Accessibility Data

- Treat names, emails, RSVP choices, and accessibility requests as personal data.
- Restrict accessibility text to roles that need it for handoff and support.
- Do not include raw accessibility text in routine telemetry or audit metadata.
- Protect downloaded CSV files operationally and technically.
- Define retention, correction, deletion, and incident-response procedures before production.
