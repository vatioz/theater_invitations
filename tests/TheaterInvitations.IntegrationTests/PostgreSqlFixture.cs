using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using TheaterInvitations.Web.Data;

namespace TheaterInvitations.IntegrationTests;

public sealed class PostgreSqlFixture : IAsyncLifetime, IDbContextFactory<InvitationDbContext>
{
    private readonly PostgreSqlContainer container = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();
    private DbContextOptions<InvitationDbContext> options = null!;

    public async Task InitializeAsync()
    {
        await container.StartAsync();
        options = new DbContextOptionsBuilder<InvitationDbContext>().UseNpgsql(container.GetConnectionString()).Options;
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
    }

    public async Task ResetAsync()
    {
        await using var db = CreateDbContext();
        await db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE \"AuditEvents\", \"InvitationParties\", \"InvitationBatches\", \"InvitationDraftRows\", \"RsvpTokens\", \"ProtectedDeliveryEnvelopes\", \"EventConfigurations\" CASCADE");
    }

    public InvitationDbContext CreateDbContext() => new(options);
    public Task<InvitationDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
    public Task DisposeAsync() => container.DisposeAsync().AsTask();
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PostgreSqlCollection : ICollectionFixture<PostgreSqlFixture>
{
    public const string Name = "PostgreSQL";
}
