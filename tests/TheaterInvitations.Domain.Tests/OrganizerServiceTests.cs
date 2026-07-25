using Microsoft.EntityFrameworkCore;
using TheaterInvitations.Domain;
using TheaterInvitations.Web.Data;
using TheaterInvitations.Web.Services;

namespace TheaterInvitations.Domain.Tests;

public sealed class OrganizerServiceTests
{
    [Fact]
    public async Task Preview_supports_quoted_company_names_and_commits_only_after_confirmation()
    {
        await using var db = CreateDb();
        await SeedConfigurationAsync(db, 10);
        var service = new OrganizerService(db, new FixedClock());
        const string csv = "primary_guest_name,email,company,allocated_seats\nAlex Guest,alex@example.test,\"Example, Inc.\",2";

        var preview = await service.PreviewImportAsync(csv);

        Assert.True(preview.IsValid);
        Assert.Equal("Example, Inc.", preview.ValidRows.Single().Company);
        Assert.Empty(db.InvitationParties);

        await service.CommitImportAsync(preview, "First import");

        Assert.Single(db.InvitationParties);
        Assert.Single(db.InvitationBatches);
    }

    [Fact]
    public async Task Preview_rejects_duplicate_email_against_existing_party()
    {
        await using var db = CreateDb();
        await SeedConfigurationAsync(db, 10);
        var batch = new InvitationBatch { Name = "Existing", DeadlineUtc = DateTimeOffset.UtcNow.AddDays(1), CreatedAtUtc = DateTimeOffset.UtcNow };
        db.Add(batch);
        db.Add(new InvitationParty { BatchId = batch.Id, PrimaryGuestName = "Existing", Email = "alex@example.test", AllocatedSeats = 1, TokenHash = RsvpService.HashToken("existing") });
        await db.SaveChangesAsync();
        var service = new OrganizerService(db, new FixedClock());

        var preview = await service.PreviewImportAsync("primary_guest_name,email,company,allocated_seats\nDuplicate,alex@example.test,,1");

        Assert.False(preview.IsValid);
        Assert.Contains(preview.Errors, x => x.Contains("duplicate", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Preview_identifies_the_invalid_seat_column()
    {
        await using var db = CreateDb();
        await SeedConfigurationAsync(db, 10);
        var service = new OrganizerService(db, new FixedClock());

        var preview = await service.PreviewImportAsync("primary_guest_name,email,company,allocated_seats\nAlex Guest,alex@example.test,,0");

        Assert.Equal("Row 2: allocated_seats must be a positive integer.", Assert.Single(preview.Errors));
    }

    private static InvitationDbContext CreateDb() => new(new DbContextOptionsBuilder<InvitationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private static async Task SeedConfigurationAsync(InvitationDbContext db, int capacity)
    {
        db.EventConfigurations.Add(new EventConfiguration { Capacity = capacity, TimeZoneId = "Europe/Prague", SupportEmail = "rsvp@example.test", AccessibilityTextLimit = 500 });
        await db.SaveChangesAsync();
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 7, 25, 12, 0, 0, TimeSpan.Zero);
    }
}
