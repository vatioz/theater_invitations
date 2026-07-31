using Microsoft.EntityFrameworkCore;
using TheaterInvitations.Domain;
using TheaterInvitations.Web.Services;

namespace TheaterInvitations.Web.Data;

public static class DevelopmentDataSeeder
{
    public const string TestRsvpToken = "development-rsvp-token";

    public static async Task SeedAsync(InvitationDbContext db, CancellationToken cancellationToken = default)
    {
        if (!await db.EventConfigurations.AnyAsync(cancellationToken))
        {
            db.EventConfigurations.Add(new EventConfiguration
            {
                Capacity = 340,
                EventName = "Development Theater Gala",
                DoorsAtUtc = new DateTimeOffset(2026, 10, 17, 16, 30, 0, TimeSpan.Zero),
                StartsAtUtc = new DateTimeOffset(2026, 10, 17, 17, 30, 0, TimeSpan.Zero),
                VenueName = "Development Theater",
                VenueAddress = "1 Example Street\nPrague",
                DressCode = "Smart casual",
                TimeZoneId = "Europe/Prague",
                SupportEmail = "rsvp@example.test",
                AccessibilityTextLimit = 500
            });
        }

        var tokenHash = RsvpService.HashToken(TestRsvpToken);
        if (await db.InvitationParties.AnyAsync(x => x.TokenHash == tokenHash, cancellationToken))
        {
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        var nowUtc = DateTimeOffset.UtcNow;
        var batch = new InvitationBatch
        {
            Name = "Development test batch",
            DeadlineUtc = nowUtc.AddDays(7),
            CreatedAtUtc = nowUtc
        };
        db.InvitationBatches.Add(batch);
        var party = new InvitationParty
        {
            BatchId = batch.Id,
            PrimaryGuestName = "Alex Guest",
            Email = "alex@example.test",
            Company = "Development Theater",
            AllocatedSeats = 2,
            TokenHash = tokenHash
        };
        db.InvitationParties.Add(party);
        db.RsvpTokens.Add(new RsvpToken { PartyId = party.Id, Hash = tokenHash, RawToken = TestRsvpToken, IssuedAtUtc = nowUtc });
        await db.SaveChangesAsync(cancellationToken);
    }
}
