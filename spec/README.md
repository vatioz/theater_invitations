# Theater Invitation RSVP Specification

## Purpose

This application collects whole-party RSVP decisions for a theater event, manages invitation capacity across multiple batches, and exports the final confirmed manifest to the theater.

The application is intentionally limited to invitation and response management. The theater remains responsible for ticket creation, seat assignment, check-in, and front-of-house operations.

## Status Labels

- **Agreed**: Confirmed during requirements discussions.
- **Recommended**: Proposed implementation direction, subject to validation.
- **Open**: Requires a decision before the affected phase can be completed.

## Actors

- **Organizer**: Imports invitees, sends invitations, monitors responses, applies approved overrides, and exports the final manifest.
- **Invitee**: Uses a private invitation link to confirm or decline all seats allocated to their party.
- **Theater**: Receives the final manifest and manages tickets, seats, accessibility fulfillment, check-in, and event-day operations.
- **Email provider**: Delivers invitation and reminder emails and reports delivery events where configured.

## Scope

### In Scope

- Importing invitation parties from CSV.
- Assigning one private RSVP link to each party and email address.
- Allocating one or more seats to a party, normally one or two.
- Strict whole-party confirmation or decline.
- Optional self-reporting of accessibility requirements.
- Hard invitation deadlines and a global RSVP lock.
- Multiple invitation batches governed by available capacity.
- Invitation and reminder email delivery.
- Organizer statistics, guest administration, overrides, and audit history.
- Exporting a confirmed manifest using a theater-compatible mapping.

### Out of Scope

- Naming or separately managing a party's `+1`.
- Partial acceptance of a party's allocated seats.
- Assigning sections, rows, or seats.
- Producing or distributing tickets unless later added by an approved change.
- Guest check-in, arrival tracking, fuzzy search, or tablet kiosk workflows.
- Arrival emails or event-day seat reminders.
- Walk-ins, uninvited guests, and front-of-house staffing.

## Current Architecture Direction

- One ASP.NET Core Blazor application.
- Blazor SSR for the public RSVP experience.
- Interactive Blazor components for the organizer interface where useful.
- PostgreSQL persistence through Entity Framework Core.
- Azure App Service hosting with authenticated organizer routes.
- Resend as the current transactional email provider.

These are implementation directions, not permission to hard-code provider assumptions into the domain model.

## Glossary

- **Party**: One invitation record, one email recipient, and all seats allocated to that invitee.
- **Allocated seats**: Number of seats reserved while an invitation is active or confirmed.
- **Invitation batch**: A group of parties imported and invited with a shared operational deadline.
- **Pending**: Invitation may still be answered and temporarily reserves its allocated seats.
- **Confirmed**: The party accepted all allocated seats.
- **Declined**: The party declined all allocated seats.
- **Expired**: The unanswered invitation passed its deadline and no longer reserves capacity.
- **Global lock**: Organizer-controlled state that rejects RSVP changes across all invitations.
- **Manifest**: Export of confirmed parties for handoff to the theater.

## Specification Set

1. [Phased implementation](01-phased-implementation.md)
2. [Functional requirements](02-functional-requirements.md)
3. [Data, email, and integrations](03-data-email-and-integrations.md)
4. [Open decisions](04-open-decisions.md)
5. [Implementation gaps](05-implementation-gaps.md)
6. [Public RSVP experience](06-public-rsvp-experience.md)
7. [Batch management](07-batch-management.md)
8. [Email campaigns](08-email-campaigns.md)
9. [Azure deployment runbook](09-azure-deployment-runbook.md)

## Guiding Principles

1. Capacity must never be oversubscribed by an import, response, or override race.
2. Deadline and lock rules must be enforced server-side.
3. A party is atomic: it receives one link and makes one decision for all allocated seats.
4. Administrative exceptions must be explicit and audited.
5. Provider and theater contracts must remain replaceable at system boundaries.
6. Accessibility information must be collected and exposed only as necessary.
