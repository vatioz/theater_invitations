using Microsoft.EntityFrameworkCore;
using TheaterInvitations.Domain;
using TheaterInvitations.Web.Data;
using TheaterInvitations.Web.Services;

namespace TheaterInvitations.Domain.Tests;

public sealed class RsvpInvitationServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Valid_token_returns_only_its_party_public_details()
    {
        await using var db = CreateDb();
        await SeedAsync(db, deadlineUtc: Now.AddHours(1));
        var service = new RsvpInvitationService(db, new FixedClock(Now));

        var invitation = await service.GetAsync("valid-token");

        Assert.NotNull(invitation);
        Assert.Equal("Alex Guest", invitation.PrimaryGuestName);
        Assert.Equal(2, invitation.AllocatedSeats);
        Assert.Equal("Theater Gala", invitation.EventName);
        Assert.Equal("Main Theater", invitation.VenueName);
        Assert.Equal("Formal", invitation.DressCode);
        Assert.Equal("Europe/Prague", invitation.TimeZoneId);
        Assert.False(invitation.IsExpired);
    }

    [Fact]
    public async Task Unknown_token_returns_no_invitation_data()
    {
        await using var db = CreateDb();
        await SeedAsync(db, deadlineUtc: Now.AddHours(1));
        var service = new RsvpInvitationService(db, new FixedClock(Now));

        var invitation = await service.GetAsync("unknown-token");

        Assert.Null(invitation);
    }

    [Fact]
    public async Task Pending_invitation_past_deadline_is_effectively_expired()
    {
        await using var db = CreateDb();
        await SeedAsync(db, deadlineUtc: Now.AddMinutes(-1));
        var service = new RsvpInvitationService(db, new FixedClock(Now));

        var invitation = await service.GetAsync("valid-token");

        Assert.NotNull(invitation);
        Assert.True(invitation.IsExpired);
    }

    [Fact]
    public async Task Valid_token_without_configuration_remains_available_but_marks_rsvp_unavailable()
    {
        await using var db = CreateDb();
        var batch = new InvitationBatch { Name = "First batch", DeadlineUtc = Now.AddHours(1), CreatedAtUtc = Now };
        db.Add(batch);
        db.Add(new InvitationParty { BatchId = batch.Id, PrimaryGuestName = "Alex Guest", Email = "alex@example.test", AllocatedSeats = 2, TokenHash = RsvpService.HashToken("valid-token") });
        await db.SaveChangesAsync();
        var service = new RsvpInvitationService(db, new FixedClock(Now));

        var invitation = await service.GetAsync("valid-token");

        Assert.NotNull(invitation);
        Assert.False(invitation.IsConfigurationAvailable);
        Assert.False(invitation.HasEventDetails);
    }

    [Fact]
    public async Task Incomplete_event_details_do_not_hide_a_valid_invitation()
    {
        await using var db = CreateDb();
        await SeedAsync(db, deadlineUtc: Now.AddHours(1));
        var configuration = await db.EventConfigurations.SingleAsync();
        configuration.EventName = string.Empty;
        await db.SaveChangesAsync();

        var invitation = await new RsvpInvitationService(db, new FixedClock(Now)).GetAsync("valid-token");

        Assert.NotNull(invitation);
        Assert.True(invitation.IsConfigurationAvailable);
        Assert.False(invitation.HasEventDetails);
    }

    [Fact]
    public async Task Development_seed_creates_one_two_seat_invitation_once()
    {
        await using var db = CreateDb();

        await DevelopmentDataSeeder.SeedAsync(db);
        await DevelopmentDataSeeder.SeedAsync(db);

        var party = await db.InvitationParties.SingleAsync();
        Assert.Equal(2, party.AllocatedSeats);
        Assert.Equal(RsvpService.HashToken(DevelopmentDataSeeder.TestRsvpToken), party.TokenHash);
    }

    [Fact]
    public async Task Development_seed_adds_sample_invitation_when_configuration_already_exists()
    {
        await using var db = CreateDb();
        db.EventConfigurations.Add(CreateConfiguration());
        await db.SaveChangesAsync();

        await DevelopmentDataSeeder.SeedAsync(db);
        await DevelopmentDataSeeder.SeedAsync(db);

        Assert.Single(db.InvitationParties);
        Assert.True(await DevelopmentSeedGuard.KnownTokenExistsAsync(db));
    }

    private static InvitationDbContext CreateDb() => new(new DbContextOptionsBuilder<InvitationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private static async Task SeedAsync(InvitationDbContext db, DateTimeOffset deadlineUtc)
    {
        var batch = new InvitationBatch { Name = "First batch", DeadlineUtc = deadlineUtc, CreatedAtUtc = Now };
        db.Add(CreateConfiguration());
        db.Add(batch);
        db.Add(new InvitationParty
        {
            BatchId = batch.Id,
            PrimaryGuestName = "Alex Guest",
            Email = "alex@example.test",
            AllocatedSeats = 2,
            TokenHash = RsvpService.HashToken("valid-token")
        });
        await db.SaveChangesAsync();
    }

    private static EventConfiguration CreateConfiguration() => new()
    {
        Capacity = 340,
        EventName = "Theater Gala",
        DoorsAtUtc = new DateTimeOffset(2026, 8, 1, 16, 0, 0, TimeSpan.Zero),
        StartsAtUtc = new DateTimeOffset(2026, 8, 1, 17, 0, 0, TimeSpan.Zero),
        VenueName = "Main Theater",
        VenueAddress = "1 Theater Street",
        DressCode = "Formal",
        TimeZoneId = "Europe/Prague",
        SupportEmail = "rsvp@example.test",
        AccessibilityTextLimit = 500
    };

    private sealed class FixedClock(DateTimeOffset nowUtc) : IClock
    {
        public DateTimeOffset UtcNow => nowUtc;
    }
}
