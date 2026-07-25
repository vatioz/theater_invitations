using Microsoft.EntityFrameworkCore;
using TheaterInvitations.Domain;
using TheaterInvitations.Web.Data;
using TheaterInvitations.Web.Services;

namespace TheaterInvitations.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class CapacityConcurrencyTests(PostgreSqlFixture database)
{
    private static readonly DateTimeOffset Now = new(2026, 7, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Concurrent_guest_confirmations_do_not_exceed_capacity()
    {
        await database.ResetAsync();
        var parties = await SeedDeclinedPartiesAsync(capacity: 1, count: 2);
        var services = new[] { CreateRsvpService(), CreateRsvpService() };

        var results = await Task.WhenAll(parties.Select((party, index) => services[index].SubmitAsync($"token-{index}", new RsvpSubmission(RsvpResponse.Confirm, null, party.Version), $"guest-{index}")));

        Assert.Single(results, result => result.Result == RsvpResult.Applied);
        Assert.Equal(1, await ReservedSeatsAsync());
    }

    [Fact]
    public async Task Concurrent_imports_do_not_exceed_capacity()
    {
        await database.ResetAsync();
        await SeedConfigurationAsync(capacity: 3);
        var preview1 = new ImportPreview(new[] { new ImportRow("First", "first@example.test", null, 2) }, Array.Empty<string>());
        var preview2 = new ImportPreview(new[] { new ImportRow("Second", "second@example.test", null, 2) }, Array.Empty<string>());

        var results = await RunCapturingAsync(
            () => CreateOrganizerService().CommitImportAsync(preview1, "First"),
            () => CreateOrganizerService().CommitImportAsync(preview2, "Second"));

        Assert.Single(results, result => result is null);
        Assert.Equal(2, await ReservedSeatsAsync());
    }

    [Fact]
    public async Task Concurrent_seat_increases_do_not_exceed_capacity()
    {
        await database.ResetAsync();
        var parties = await SeedPendingPartiesAsync(capacity: 3, count: 2);

        var results = await RunCapturingAsync(
            () => CreateOrganizerService().CorrectPartyAsync(parties[0].Id, parties[0].Version, parties[0].PrimaryGuestName, parties[0].Email, null, 2),
            () => CreateOrganizerService().CorrectPartyAsync(parties[1].Id, parties[1].Version, parties[1].PrimaryGuestName, parties[1].Email, null, 2));

        Assert.Single(results, result => result is null);
        Assert.Equal(3, await ReservedSeatsAsync());
    }

    [Fact]
    public async Task Concurrent_overrides_do_not_exceed_capacity()
    {
        await database.ResetAsync();
        var parties = await SeedDeclinedPartiesAsync(capacity: 1, count: 2);

        var results = await RunCapturingAsync(
            () => CreateOrganizerService().OverrideStatusAsync(parties[0].Id, parties[0].Version, InvitationStatus.Confirmed, "Approved"),
            () => CreateOrganizerService().OverrideStatusAsync(parties[1].Id, parties[1].Version, InvitationStatus.Confirmed, "Approved"));

        Assert.Single(results, result => result is null);
        Assert.Equal(1, await ReservedSeatsAsync());
    }

    private RsvpService CreateRsvpService() => new(database, new FixedClock(), new TransactionRetry());

    private OrganizerService CreateOrganizerService()
    {
        var db = database.CreateDbContext();
        return new OrganizerService(db, database, new FixedClock(), new AllowedAuthorization(), new TransactionRetry());
    }

    private async Task<List<InvitationParty>> SeedPendingPartiesAsync(int capacity, int count)
    {
        await SeedConfigurationAsync(capacity);
        await using var db = database.CreateDbContext();
        var batch = new InvitationBatch { Name = "Test", CreatedAtUtc = Now, DeadlineUtc = Now.AddDays(1) };
        var parties = Enumerable.Range(0, count).Select(index => new InvitationParty { BatchId = batch.Id, PrimaryGuestName = $"Guest {index}", Email = $"guest-{index}@example.test", AllocatedSeats = 1, TokenHash = RsvpService.HashToken($"token-{index}") }).ToList();
        db.Add(batch); db.AddRange(parties);
        await db.SaveChangesAsync();
        return parties;
    }

    private async Task<List<InvitationParty>> SeedDeclinedPartiesAsync(int capacity, int count)
    {
        var parties = await SeedPendingPartiesAsync(capacity, count);
        await using var db = database.CreateDbContext();
        foreach (var persisted in await db.InvitationParties.ToListAsync()) persisted.OverrideStatus(InvitationStatus.Declined, Now);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return await db.InvitationParties.AsNoTracking().OrderBy(x => x.Email).ToListAsync();
    }

    private async Task SeedConfigurationAsync(int capacity)
    {
        await using var db = database.CreateDbContext();
        db.Add(new EventConfiguration { Capacity = capacity, TimeZoneId = "Europe/Prague", SupportEmail = "rsvp@example.test", AccessibilityTextLimit = 500 });
        await db.SaveChangesAsync();
    }

    private async Task<int> ReservedSeatsAsync()
    {
        await using var db = database.CreateDbContext();
        return await (from party in db.InvitationParties join batch in db.InvitationBatches on party.BatchId equals batch.Id where party.Status == InvitationStatus.Confirmed || (party.Status == InvitationStatus.Pending && batch.DeadlineUtc > Now) select party.AllocatedSeats).SumAsync();
    }

    private static async Task<Exception?[]> RunCapturingAsync(params Func<Task>[] operations) => await Task.WhenAll(operations.Select(async operation =>
    {
        try { await operation(); return null; }
        catch (Exception exception) when (exception is InvalidOperationException or DbUpdateException) { return exception; }
    }));

    private sealed class FixedClock : IClock { public DateTimeOffset UtcNow => Now; }
    private sealed class AllowedAuthorization : IOrganizerAuthorization { public Task<string> RequireAsync(string policy, CancellationToken cancellationToken = default) => Task.FromResult("Integration Operator"); }
}
