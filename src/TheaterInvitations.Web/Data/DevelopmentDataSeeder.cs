using TheaterInvitations.Domain;
using TheaterInvitations.Web.Services;

namespace TheaterInvitations.Web.Data;

public static class DevelopmentDataSeeder
{
    public const string TestRsvpToken = "development-rsvp-token";

    public static async Task SeedAsync(InvitationDbContext db, CancellationToken cancellationToken = default)
    {
        if (db.EventConfigurations.Any())
        {
            return;
        }

        var nowUtc = DateTimeOffset.UtcNow;
        var batch = new InvitationBatch
        {
            Name = "Development test batch",
            DeadlineUtc = nowUtc.AddDays(7),
            CreatedAtUtc = nowUtc
        };
        db.EventConfigurations.Add(new EventConfiguration
        {
            Capacity = 340,
            TimeZoneId = "Europe/Prague",
            SupportEmail = "rsvp@example.test",
            AccessibilityTextLimit = 500
        });
        db.InvitationBatches.Add(batch);
        db.InvitationParties.Add(new InvitationParty
        {
            BatchId = batch.Id,
            PrimaryGuestName = "Alex Guest",
            Email = "alex@example.test",
            Company = "Development Theater",
            AllocatedSeats = 2,
            TokenHash = RsvpService.HashToken(TestRsvpToken)
        });
        await db.SaveChangesAsync(cancellationToken);
    }
}
