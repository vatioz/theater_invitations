# Open Decisions

Status values are `Open`, `Proposed`, `Decided`, or `Deferred`. Owners are placeholders until assigned.

## Product and Event Rules

| ID | Decision | Status | Owner | Recommended Default | Blocks |
| --- | --- | --- | --- | --- | --- |
| OD-01 | What is the authoritative theater capacity? | Decided | Requestor | Store as configuration; current planning estimate is 340. | Phase 0 |
| OD-02 | May guests change Confirmed or Declined responses before their deadline? | Decided | Requestor | Allow changes until the deadline, audit each change, and recheck capacity. | Phases 1-2 |
| OD-03 | What happens when an organizer tries to exceed capacity? | Decided | Requestor | Reject the action; do not add an overbooking mode. | Phases 1 and 3 |
| OD-04 | Who may approve late or VIP exceptions? | Decided | Requestor | Restrict to a named elevated organizer role with mandatory reason. | Phases 1 and 3 |
| OD-05 | Does global lock block only guests, or organizers too? | Decided | Requestor | Block guest mutations; permit explicit audited organizer overrides. | Phase 1 |
| OD-06 | What event time zone governs deadlines? | Decided | Requestor | Use the venue's local time zone and display it beside every deadline. | Phases 0 and 2 |
| OD-07 | What event details appear on the RSVP page? | Decided | Requestor | Date, doors time, start time, venue, address, dress code when applicable. | Phase 2 |
| OD-08 | What organizer support address appears to guests? | Decided | Requestor | Use a monitored event-specific mailbox. | Phase 2 |
| OD-09 | What character limit and wording apply to accessibility requests? | Decided | Requestor and theater | Start with 500 characters pending theater feedback. | Phase 2 |
| OD-10 | Is CSV import strictly all-or-nothing? | Decided | Product owner | Yes; correct invalid rows and preview again. | Phase 3 |
| OD-11 | How should duplicate emails across batches be handled? | Decided | Requestor | Block by default and require an explicit correction or documented exception. | Phase 3 |
| OD-12 | Which organizer roles may import, send, override, lock, and export? | Decided | System owner | Separate viewer and operator roles; reserve overrides and lock for elevated operators. | Phase 3 |

## Email

| ID | Decision | Status | Owner | Recommended Default | Blocks |
| --- | --- | --- | --- | --- | --- |
| OD-13 | Which organization owns the Resend account? | Decided | System owner | Organization-owned account, not a developer's personal account. | Phase 4 |
| OD-14 | Which sending domain or subdomain, From name, From address, and Reply-To are approved? | Decided | Communications and IT | Use a dedicated verified subdomain and monitored Reply-To. | Phase 4 |
| OD-15 | Which current Resend plan and provider limits apply during the event window? | Decided | System owner | Verify against expected party count, reminders, tests, and provider documentation. | Phase 4 |
| OD-16 | Who approves invitation and reminder content? | Decided | Communications | Require versioned approval before each campaign. | Phase 4 |
| OD-17 | When are reminders sent and how many are allowed per invitation? | Decided | Requestor | One reminder to active pending parties before each deadline. | Phase 4 |
| OD-18 | How are bounces, complaints, suppressions, corrections, and resend requests handled? | Decided | Communications and organizer | Surface failures, suppress complaints/permanent bounces, and require corrected addresses for resend. | Phase 4 |

## Theater and Handoff

| ID | Decision | Status | Owner | Recommended Default | Blocks |
| --- | --- | --- | --- | --- | --- |
| OD-19 | What exact CSV schema, delimiter, encoding, and row model does the theater require? | Decided | Theater | Test a representative sample through the theater's import process. | Phase 5 |
| OD-20 | How will the theater distribute seat assignments and tickets? | Decided | Theater | Theater sends tickets directly using its established system. | Scope and Phase 5 |
| OD-21 | What accessibility fields and terminology can the theater accept? | Decided | Theater | Prefer structured categories plus optional details if supported; otherwise map the approved text field. | Phases 2 and 5 |
| OD-22 | How and when is the final manifest transferred securely? | Decided | Theater and system owner | Use an approved protected channel, not ordinary unencrypted attachment forwarding. | Phase 5 |
| OD-23 | How are corrections, cancellations, or late exceptions communicated after handoff? | Decided | Theater and requestor | Define a named contact, cutoff, and versioned replacement or delta process. | Phase 5 |
| OD-24 | What event-day operations and staffing does the theater provide? | Decided | Theater | Theater owns check-in, seating, walk-ins, and guest direction. | Operational readiness |

## Security, Privacy, and Operations

| ID | Decision | Status | Owner | Recommended Default | Blocks |
| --- | --- | --- | --- | --- | --- |
| OD-25 | Which identity provider, tenant/domain, group, or allowlist authorizes organizers? | Decided | IT and system owner | App Service authentication plus an application-level approved-group check. | Phases 0 and 6 |
| OD-26 | How long are invitation, audit, email, export, and accessibility records retained? | Decided | Privacy owner | Keep only through the operational and dispute window, then delete or anonymize by policy. | Phase 6 |
| OD-27 | Who may view accessibility requirements? | Decided | Privacy owner and theater | Restrict to organizers preparing handoff and authorized theater recipients. | Phases 3, 5, and 6 |
| OD-28 | What backup, recovery point, and recovery time objectives apply? | Decided | System owner | Daily backup plus point-in-time recovery if available; rehearse restore before invitations launch. | Phase 6 |
| OD-29 | Who owns production monitoring and support during invitation deadlines? | Decided | Requestor and system owner | Name primary and backup operators with provider and database escalation access. | Phase 6 |

## Decision Process

1. Assign an owner and target date.
2. Record the selected outcome and rationale in this file or a linked architecture decision record.
3. Update affected requirements and phase acceptance criteria.
4. Mark the item `Decided` only after the relevant stakeholders approve it.
