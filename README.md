# Theater Invitations

ASP.NET Core RSVP management for theater invitation parties. Requirements are in [`spec/`](spec/README.md).

## Development

```powershell
dotnet restore TheaterInvitations.sln
dotnet build TheaterInvitations.sln --no-restore
dotnet test TheaterInvitations.sln --no-build
dotnet run --project src/TheaterInvitations.Web
```

The solution test command includes PostgreSQL concurrency tests that start an isolated container. Docker Desktop must be running.

Set `ConnectionStrings__Postgres` in user secrets or environment variables before applying migrations. `.env.example` shows the expected local value; it is not loaded automatically.

```powershell
dotnet tool restore
dotnet tool run dotnet-ef database update --project src/TheaterInvitations.Web
```

## Manual RSVP Test

PostgreSQL is required for the browser flow. Either install PostgreSQL locally or start it with Docker Desktop:

```powershell
docker run --name theater-invitations-postgres --detach --publish 5432:5432 --env POSTGRES_PASSWORD=postgres --env POSTGRES_DB=theater_invitations postgres:16
```

Set the connection string for the current PowerShell session, apply migrations, and run the app:

```powershell
$env:ConnectionStrings__Postgres = "Host=localhost;Database=theater_invitations;Username=postgres;Password=postgres"
dotnet tool restore
dotnet tool run dotnet-ef database update --project src/TheaterInvitations.Web
dotnet run --project src/TheaterInvitations.Web
```

In Development, the application idempotently seeds one party after migrations. Open `https://localhost:7238/rsvp/development-rsvp-token`, confirm or decline the two-seat party, then reload the page to inspect the recorded response. The test token is for local Development only; do not deploy it or use it for real invitations.

## Run With Docker

The application container expects PostgreSQL to be provided separately. Start a local PostgreSQL container first:

```powershell
docker run --name theater-invitations-postgres --detach --publish 5432:5432 --env POSTGRES_USER=postgres --env POSTGRES_PASSWORD=postgres --env POSTGRES_DB=theater_invitations postgres:16-alpine
```

Build the application image from the repository root:

```powershell
docker build --tag theater-invitations:local .
```

Run the application container in Development. The application applies migrations and creates the local sample RSVP data when it starts in Development:

```powershell
docker run --name theater-invitations-web --rm --publish 8080:8080 `
  --env ASPNETCORE_ENVIRONMENT=Development `
  --env ASPNETCORE_URLS=http://+:8080 `
  --env ConnectionStrings__Postgres="Host=host.docker.internal;Port=5432;Database=theater_invitations;Username=postgres;Password=postgres" `
  theater-invitations:local
```

Open the application at `http://localhost:8080`. The local development RSVP link is:

```text
http://localhost:8080/rsvp/development-rsvp-token
```

The Development login endpoints are available at:

```text
http://localhost:8080/dev/login/Operator
http://localhost:8080/dev/login/ElevatedOperator
```

To stop and remove the PostgreSQL container later:

```powershell
docker stop theater-invitations-postgres
docker rm theater-invitations-postgres
```

The application image does not include PostgreSQL, secrets, or production configuration. For Azure deployment, use the procedure in [`spec/09-azure-deployment-runbook.md`](spec/09-azure-deployment-runbook.md), including `WEBSITES_PORT=8080` and App Service application settings.

## GitHub Actions Container Publishing

`.github/workflows/container.yml` builds and pushes the production container when `main` changes, or when started manually from the Actions tab.

Configure these GitHub repository **secrets** for the ACR admin account:

```text
ACR_USERNAME
ACR_PASSWORD
```

Configure these GitHub repository **variables**:

```text
ACR_LOGIN_SERVER  # Registry login server, for example theaterregistry.azurecr.io
IMAGE_REPOSITORY  # Optional; defaults to theater-invitations
```

The workflow uses the ACR admin username/password only to push images. Prefer GitHub OIDC with an `AcrPush`-scoped identity for a long-lived production setup, and disable the ACR admin account when that migration is complete.

The workflow publishes a commit trace tag on every build:

```text
<registry>.azurecr.io/<repository>:sha-<short-commit-sha>
```

For a human-readable release, create and push a semantic-version Git tag:

```powershell
git tag v1.0.0
git push origin v1.0.0
```

That produces:

```text
<registry>.azurecr.io/<repository>:v1.0.0
<registry>.azurecr.io/<repository>:sha-<short-commit-sha>
```

Pushes to `main` also publish `latest`. Use the semantic version tag when configuring App Service for a release and keep the SHA tag for exact rollback traceability. Do not use `latest` as the production deployment reference.

## Development Organizer Access

Development provides local-only cookie login personas. Open one of these URLs, then go to `/organizer`:

- `https://localhost:7238/dev/login/Operator` for CSV preview and import.
- `https://localhost:7238/dev/login/ElevatedOperator` for global RSVP lock changes.

These endpoints do not exist outside Development. Production authentication will be supplied by the host and must emit the same organizer role claims.
