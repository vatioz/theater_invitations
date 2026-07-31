FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY TheaterInvitations.sln ./
COPY src/TheaterInvitations.Domain/TheaterInvitations.Domain.csproj src/TheaterInvitations.Domain/
COPY src/TheaterInvitations.Web/TheaterInvitations.Web.csproj src/TheaterInvitations.Web/
COPY tests/TheaterInvitations.Domain.Tests/TheaterInvitations.Domain.Tests.csproj tests/TheaterInvitations.Domain.Tests/
COPY tests/TheaterInvitations.IntegrationTests/TheaterInvitations.IntegrationTests.csproj tests/TheaterInvitations.IntegrationTests/
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
