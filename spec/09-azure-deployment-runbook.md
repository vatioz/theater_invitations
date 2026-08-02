# Azure Deployment Runbook

This runbook describes a pragmatic production deployment for the one-off theater RSVP event. It uses one Linux custom container on an existing Azure App Service plan, Azure Database for PostgreSQL Flexible Server, application-managed Identity authentication for organizers, and Resend for manually triggered email campaigns.

The functional requirements in [02-functional-requirements.md](02-functional-requirements.md) remain normative. This document is an operational deployment artifact, not an infrastructure-as-code specification.

## Target Architecture

```text
Guests
  -> HTTPS public RSVP hostname
  -> Azure App Service custom container
  -> Azure Database for PostgreSQL Flexible Server

Organizers
  -> Application Identity login
  -> Azure App Service custom container

Manual email campaigns
  -> Resend API
```

Use one App Service application, one PostgreSQL database, and one container image. Do not add Kubernetes, Container Apps, Redis, Service Bus, a worker platform, or multiple active application instances for this event.

## Current Production Gaps

Do not send real invitations until these gaps are resolved and rehearsed:

1. Production organizer authentication is not implemented in application configuration. Development registers cookie authentication and local development personas only. Production must provide a principal with `Operator` or `ElevatedOperator` role claims.
2. Organizer routes must require authentication while public RSVP routes at `/rsvp/*` remain anonymous.
3. The public RSVP hostname must be configured in `PublicApp__BaseUrl` before a campaign is sent. `localhost` links are not usable by guests.
4. The Resend sender domain must be verified outside the application and sender settings must use the verified From address.
5. The raw-token one-off delivery design means database credentials and database backups protect usable RSVP bearer links. Restrict database access accordingly.

## Deployment Decisions

| Topic | Selected approach |
| --- | --- |
| Application host | Existing Azure App Service plan, Linux custom container. |
| Image registry | Azure Container Registry, pulled through App Service managed identity. |
| Database | Azure Database for PostgreSQL Flexible Server. |
| Database networking | Public endpoint plus firewall allowlist for fastest one-off setup, unless organizational policy requires private networking. |
| Organizer identity | Application-managed ASP.NET Core Identity. |
| Organizer roles | `Operator` and `ElevatedOperator`. |
| Public RSVP access | Anonymous bearer-token route only. |
| Email | Resend API with manually triggered sequential campaigns. |
| Application scaling | One active production instance. |
| Migrations | Explicit release step, never automatic production startup migration. |

## Required Values

Prepare these values before deployment. Do not commit their real values.

| App setting | Purpose |
| --- | --- |
| `ASPNETCORE_ENVIRONMENT=Production` | Enables production exception/HSTS behavior and disables development seeding. |
| `ConnectionStrings__Postgres` | TLS PostgreSQL connection string. |
| `PublicApp__BaseUrl` | Public HTTPS RSVP hostname, without trailing slash. |
| `Resend__ApiKey` | Restricted Resend API key. |
| `WEBSITES_PORT=8080` | Container listening port. |

Example production connection string:

```text
Host=your-server.postgres.database.azure.com;
Port=5432;
Database=theater_invitations;
Username=theater_app;
Password=REDACTED;
Ssl Mode=Require;
Trust Server Certificate=false
```

## Step 1: Build The Container

Create a multi-stage Dockerfile in the repository root.

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY TheaterInvitations.sln ./
COPY src/TheaterInvitations.Domain/TheaterInvitations.Domain.csproj src/TheaterInvitations.Domain/
COPY src/TheaterInvitations.Web/TheaterInvitations.Web.csproj src/TheaterInvitations.Web/
RUN dotnet restore TheaterInvitations.sln

COPY . .
RUN dotnet publish src/TheaterInvitations.Web/TheaterInvitations.Web.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

COPY --from=build /app/publish .
EXPOSE 8080

ENTRYPOINT ["dotnet", "TheaterInvitations.Web.dll"]
```

Build and run tests before building an image:

```powershell
dotnet restore TheaterInvitations.sln
dotnet build TheaterInvitations.sln --no-restore --configuration Release
dotnet test TheaterInvitations.sln --no-build --configuration Release
```

Build with an immutable image tag based on the Git commit:

```powershell
docker build -t theater-invitations:web-<git-sha> .
```

Do not deploy a mutable `latest` image tag.

## Step 2: Create Azure Container Registry

Create an Azure Container Registry in the same region as the App Service plan where practical.

Push the image using an immutable tag:

```text
<registry>.azurecr.io/theater-invitations:web-<git-sha>
```

Enable the App Service system-assigned managed identity and grant it the `AcrPull` role on the registry. Do not store container-registry passwords in application settings.

## Step 3: Create PostgreSQL Flexible Server

1. Create an Azure Database for PostgreSQL Flexible Server.
2. Create the `theater_invitations` database.
3. Create a dedicated least-privilege application user, for example `theater_app`.
4. Require TLS and use the server FQDN, not an IP address.
5. For public networking, allow:
   - the deployment operator's current public IP for migration access;
   - App Service outbound IP addresses, or the Azure-services firewall option only if organizational policy accepts its broader scope.
6. Configure backups and verify the expected retention window.

Public-access firewall rules can take several minutes to apply. Verify application credentials independently from firewall access.

## Step 4: Apply Database Migrations

Run migrations explicitly against the production database before deploying the production image.

Temporarily provide the production connection string in a controlled shell session:

```powershell
$env:ConnectionStrings__Postgres = "<production connection string>"
dotnet tool restore
dotnet tool run dotnet-ef database update --project src/TheaterInvitations.Web
```

Do not enable automatic migrations in Production. Verify the database has no Development seed party or `development-rsvp-token` hash after migration.

## Step 5: Configure App Service Container

1. Configure the App Service to use the ACR image tag.
2. Set `WEBSITES_PORT=8080`.
3. Set the required application settings listed above.
4. Enable HTTPS-only.
5. Enable container and application logging for the event window.
6. Configure the App Service managed identity to pull from ACR.
7. Keep one application instance active for the one-off manual-send workflow.

App Service injects application settings into the container process as environment variables. The double underscore form maps to nested .NET configuration, for example:

```text
ConnectionStrings__Postgres
PublicApp__BaseUrl
Resend__ApiKey
```

After deploying a new image, App Service continues serving the prior image until the new container starts and responds to health checks. Retain the previous immutable image tag for rollback.

## Step 6: Configure Public Domain And TLS

1. Choose the public RSVP hostname, for example:

```text
rsvp.your-event-domain.example
```

2. Add the App Service custom-domain DNS record as directed by Azure.
3. Bind a managed certificate or organization-approved certificate.
4. Enable HTTPS-only.
5. Set:

```text
PublicApp__BaseUrl=https://rsvp.your-event-domain.example
```

6. Restart the App Service after configuration changes.

The public app hostname and the verified Resend sender domain do not need to be the same domain.

## Step 7: Configure Organizer Authentication

Application-managed ASP.NET Core Identity is used; App Service Authentication is not required for organizer access.

1. Set temporary App Service settings:

```text
BootstrapAdmin__Email=your-admin@example.com
BootstrapAdmin__Password=<generated-strong-password>
```

2. Deploy and open `/account/login`.
3. Sign in once to create the `ElevatedOperator` account.
4. Remove both bootstrap settings and restart the app.
5. Use the elevated user-management page to create additional operators.
6. Ensure anonymous requests remain possible for `/rsvp/*`.
7. Configure application roles:

```text
Operator
ElevatedOperator
```

7. Test that both roles reach organizer routes and that only `ElevatedOperator` can perform elevated actions.

The application performs role authorization in both route policies and service methods. Do not rely only on hidden UI controls.

## Step 8: Configure Resend

1. Verify the chosen sender domain in Resend.
2. Add exactly the DKIM, SPF, MX, and DMARC records Resend requires at the DNS provider.
3. Wait for Resend to report the sender domain as verified.
4. Create a restricted Resend API key and store it as `Resend__ApiKey`.
5. In Event settings, configure:

```text
From address: rsvp@your-verified-sender-domain.example
Reply-To address: monitored mailbox
Daily send ceiling: approved event limit
I have verified this sender domain in Resend: checked
```

The checkbox is an organizer operational attestation. It does not perform DNS verification; Resend is the source of truth for verified-domain status.

6. Create or verify each active named email template with its intended From display name. Templates for different company-facing campaigns may use different display names, but they all use the application-wide From and Reply-To addresses above.

## Step 9: Staging Rehearsal

Use a separate staging App Service app or deployment slot where supported. Use representative but non-guest data.

Verify all of the following:

1. Anonymous public RSVP works for a valid token.
2. Unknown or revoked tokens show only the generic invalid-link state.
3. Organizer authentication works with actual production-shaped role claims.
4. Event details, support address, deadline, and configured time-zone labels are correct.
5. Whole-party confirm, decline, update, expiration, and global lock behave correctly.
6. Capacity and concurrent draft commit tests remain green.
7. Campaign template preview and test send show the selected template's display name and use the application-wide verified Resend address and organizer Reply-To.
8. A one-recipient campaign reaches `Accepted` in the organizer UI and Resend dashboard.
9. The generated RSVP link uses the staging public HTTPS hostname, not localhost.
10. Logs, audit pages, and campaign pages do not reveal raw RSVP tokens, full RSVP URLs, or API keys.
11. Database restore/backup process is understood and documented.

## Step 10: Production Launch

1. Deploy the tested immutable image tag.
2. Confirm the production public hostname, HTTPS certificate, database connection, and organizer authentication.
3. Configure the verified Resend sender settings.
4. Create one controlled production test party using an address you own.
5. Prepare, review, confirm, and send its campaign.
6. Verify the received message, Reply-To, event information, and public RSVP link from another device/network.
7. Only then import/commit the real invitation batch and send the approved campaign.
8. Monitor App Service logs, PostgreSQL availability, Resend dashboard, campaign dispatch states, and organizer support mailbox during the response window.

## Rollback And Incident Steps

| Situation | Action |
| --- | --- |
| New container fails startup | Change App Service back to the previous immutable image tag. Inspect container logs. |
| Database migration fails | Stop deployment, preserve error output, and restore only through an approved database recovery procedure. Do not drop production tables as a troubleshooting shortcut. |
| Sender misconfiguration | Stop before campaign confirmation, correct sender settings, run a new test send. |
| Campaign has failed recipients | Inspect safe failure category and Resend dashboard; use manual retry only after correcting the cause. |
| RSVP link suspected exposed | Elevated organizer regenerates the party token, which revokes the prior link; prepare a new resend campaign. |
| Global RSVP stop required | Elevated organizer enables global lock; no invitation status changes are silently altered. |

## Minimal Production Checklist

- [ ] Immutable container image pushed to ACR.
- [ ] App Service managed identity has `AcrPull`.
- [ ] PostgreSQL database, TLS connection, user, firewall, and backup verified.
- [ ] Production migrations applied explicitly.
- [ ] `ConnectionStrings__Postgres`, `PublicApp__BaseUrl`, `Resend__ApiKey`, and `WEBSITES_PORT` configured.
- [ ] Public HTTPS RSVP domain tested from outside the deployment machine.
- [ ] Application login tested with `Operator` and `ElevatedOperator` accounts.
- [ ] Resend domain verified and test campaign accepted.
- [ ] One-recipient production rehearsal completed.
- [ ] Log/audit token-leak review completed.
- [ ] Named operators and support mailbox available during the RSVP window.
