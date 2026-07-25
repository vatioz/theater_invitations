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

    [Fact]
    public async Task Correction_updates_party_and_records_the_organizer()
    {
        await using var db = CreateDb();
        var party = await AddPartyAsync(db);
        var service = new OrganizerService(db, new FixedClock());

        await service.CorrectPartyAsync(party.Id, "Updated Guest", "updated@example.test", "Updated Co", 2, "Development Operator");

        Assert.Equal("Updated Guest", party.PrimaryGuestName);
        Assert.Equal("updated@example.test", party.Email);
        Assert.Equal(2, party.AllocatedSeats);
        var audit = await db.AuditEvents.SingleAsync();
        Assert.Equal("PartyCorrected", audit.EventType);
        Assert.Equal("Development Operator", audit.ActorIdentifier);
    }

    [Fact]
    public async Task Status_override_requires_reason_and_records_transition()
    {
        await using var db = CreateDb();
        var party = await AddPartyAsync(db);
        var service = new OrganizerService(db, new FixedClock());

        await Assert.ThrowsAsync<ArgumentException>(() => service.OverrideStatusAsync(party.Id, InvitationStatus.Confirmed, "", "Development ElevatedOperator"));
        await service.OverrideStatusAsync(party.Id, InvitationStatus.Confirmed, "Approved exception", "Development ElevatedOperator");

        Assert.Equal(InvitationStatus.Confirmed, party.Status);
        var audit = await db.AuditEvents.SingleAsync();
        Assert.Equal(InvitationStatus.Pending, audit.PreviousStatus);
        Assert.Equal(InvitationStatus.Confirmed, audit.ResultingStatus);
        Assert.Equal("Approved exception", audit.ReasonCategory);
    }

    [Fact]
    public async Task Dashboard_searches_and_pages_parties()
    {
        await using var db = CreateDb();
        await AddPartyAsync(db, "Alex Guest", "alex@example.test");
        await AddPartyAsync(db, "Morgan Guest", "morgan@example.test");
        var service = new OrganizerService(db, new FixedClock());

        var result = await service.GetDashboardAsync(new PartyQuery(Search: "Morgan", PageSize: 1));

        Assert.Equal(1, result.PartyCount);
        Assert.Single(result.Parties);
        Assert.Equal("Morgan Guest", result.Parties.Single().Name);
    }

    private static InvitationDbContext CreateDb() => new(new DbContextOptionsBuilder<InvitationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private static async Task SeedConfigurationAsync(InvitationDbContext db, int capacity)
    {
        db.EventConfigurations.Add(new EventConfiguration { Capacity = capacity, TimeZoneId = "Europe/Prague", SupportEmail = "rsvp@example.test", AccessibilityTextLimit = 500 });
        await db.SaveChangesAsync();
    }

    private static async Task<InvitationParty> AddPartyAsync(InvitationDbContext db, string name = "Alex Guest", string email = "alex@example.test")
    {
        if (!await db.EventConfigurations.AnyAsync()) await SeedConfigurationAsync(db, 10);
        var batch = new InvitationBatch { Name = "Batch", DeadlineUtc = DateTimeOffset.UtcNow.AddDays(1), CreatedAtUtc = DateTimeOffset.UtcNow };
        var party = new InvitationParty { BatchId = batch.Id, PrimaryGuestName = name, Email = email, AllocatedSeats = 1, TokenHash = RsvpService.HashToken(Guid.NewGuid().ToString()) };
        db.Add(batch); db.Add(party);
        await db.SaveChangesAsync();
        return party;
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 7, 25, 12, 0, 0, TimeSpan.Zero);
    }
}
