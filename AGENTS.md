# Repository Guidance

## Development

- Use `dotnet restore TheaterInvitations.sln`, `dotnet build TheaterInvitations.sln --no-restore`, then `dotnet test TheaterInvitations.sln --no-build`.
- The solution test command includes PostgreSQL Testcontainers integration tests and therefore requires a running Docker daemon.
- The web project is `src/TheaterInvitations.Web`; database migrations use the locally pinned tool: `dotnet tool restore` then `dotnet tool run dotnet-ef database update --project src/TheaterInvitations.Web`.
- Set `ConnectionStrings__Postgres` through user secrets or environment variables. `.env.example` is documentation only and is not loaded by the application.

## Sources Of Truth

- `spec/02-functional-requirements.md` is the normative behavior source. The defaults in `spec/04-open-decisions.md` have been selected as project decisions.

## Domain Invariants

- A party is one email recipient and one atomic RSVP for all its allocated seats; never model or collect a separate `+1` identity, partial acceptance, ticketing, or seat assignment.
- Keep capacity configurable. Reserved capacity is confirmed seats plus non-expired pending seats; every capacity-increasing operation must be transaction-safe, including imports, deadline extensions, overrides, and seat increases.
- Enforce deadline and global-lock eligibility inside server-side RSVP mutations. Store deadlines and timestamps as UTC; use the configured event time zone only for organizer input/display.
- Public RSVP links require opaque, unguessable tokens. Invalid links must not disclose invitee data, and tokens, query strings, and unnecessary accessibility text must not enter logs or audit metadata.
- Make RSVP submissions and email dispatches idempotent; audit successful and rejected state-changing attempts without recording secrets.

## Integration Boundaries

- Keep email providers and theater export formats behind replaceable adapters. Resend and Azure App Service are current directions, not domain-model dependencies.
- CSV input fields are `primary_guest_name`, `email`, optional `company`, and `allocated_seats`; preview and validate imports before any persistence, then commit transactionally.
