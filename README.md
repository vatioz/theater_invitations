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
