# Theater Invitations

ASP.NET Core RSVP management for theater invitation parties. Requirements are in [`spec/`](spec/README.md).

## Development

```powershell
dotnet restore TheaterInvitations.sln
dotnet build TheaterInvitations.sln --no-restore
dotnet test TheaterInvitations.sln --no-build
dotnet run --project src/TheaterInvitations.Web
```

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
