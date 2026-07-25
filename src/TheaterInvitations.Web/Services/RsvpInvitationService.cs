using Microsoft.EntityFrameworkCore;
using TheaterInvitations.Domain;
using TheaterInvitations.Web.Data;

namespace TheaterInvitations.Web.Services;

public sealed class RsvpInvitationService(InvitationDbContext db, IClock clock)
{
    public async Task<RsvpInvitation?> GetAsync(string token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var tokenHash = RsvpService.HashToken(token);
        var partyInvitation = await (
            from party in db.InvitationParties
            join batch in db.InvitationBatches on party.BatchId equals batch.Id
            where party.TokenHash == tokenHash
            select new
            {
                party.PrimaryGuestName,
                party.AllocatedSeats,
                party.Status,
                party.AccessibilityRequirements,
                batch.DeadlineUtc,
                party.Version
            }).SingleOrDefaultAsync(cancellationToken);

        if (partyInvitation is null)
        {
            return null;
        }

        var configuration = await db.EventConfigurations.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        var isExpired = partyInvitation.Status == InvitationStatus.Expired ||
            (partyInvitation.Status == InvitationStatus.Pending && clock.UtcNow >= partyInvitation.DeadlineUtc);
        return new RsvpInvitation(
            partyInvitation.PrimaryGuestName,
            partyInvitation.AllocatedSeats,
            partyInvitation.DeadlineUtc,
            configuration?.EventName ?? string.Empty,
            configuration?.DoorsAtUtc ?? default,
            configuration?.StartsAtUtc ?? default,
            configuration?.VenueName ?? string.Empty,
            configuration?.VenueAddress ?? string.Empty,
            configuration?.DressCode,
            configuration?.TimeZoneId ?? string.Empty,
            configuration?.SupportEmail ?? string.Empty,
            configuration?.AccessibilityTextLimit ?? 0,
            partyInvitation.Status,
            configuration?.IsRsvpLocked ?? false,
            partyInvitation.AccessibilityRequirements,
            partyInvitation.Version)
        {
            IsExpired = isExpired,
            IsConfigurationAvailable = configuration is not null
        };
    }
}
