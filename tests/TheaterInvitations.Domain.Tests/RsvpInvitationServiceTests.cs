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
    public async Task Development_seed_creates_one_two_seat_invitation_once()
    {
        await using var db = CreateDb();

        await DevelopmentDataSeeder.SeedAsync(db);
        await DevelopmentDataSeeder.SeedAsync(db);

        var party = await db.InvitationParties.SingleAsync();
        Assert.Equal(2, party.AllocatedSeats);
        Assert.Equal(RsvpService.HashToken(DevelopmentDataSeeder.TestRsvpToken), party.TokenHash);
    }

    private static InvitationDbContext CreateDb() => new(new DbContextOptionsBuilder<InvitationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private static async Task SeedAsync(InvitationDbContext db, DateTimeOffset deadlineUtc)
    {
        var batch = new InvitationBatch { Name = "First batch", DeadlineUtc = deadlineUtc, CreatedAtUtc = Now };
        db.Add(new EventConfiguration { Capacity = 340, TimeZoneId = "Europe/Prague", SupportEmail = "rsvp@example.test", AccessibilityTextLimit = 500 });
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

    private sealed class FixedClock(DateTimeOffset nowUtc) : IClock
    {
        public DateTimeOffset UtcNow => nowUtc;
    }
}
