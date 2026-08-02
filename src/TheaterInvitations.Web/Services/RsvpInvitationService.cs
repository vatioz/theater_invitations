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
        var tokenRecord = await db.RsvpTokens.AsNoTracking().SingleOrDefaultAsync(x => x.Hash == tokenHash, cancellationToken);
        if (tokenRecord is not null && tokenRecord.RevokedAtUtc is not null)
        {
            return null;
        }
        var partyInvitation = await (
            from party in db.InvitationParties
            join batch in db.InvitationBatches on party.BatchId equals batch.Id
            where tokenRecord != null ? party.Id == tokenRecord.PartyId : party.TokenHash == tokenHash
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
        var nowUtc = clock.UtcNow;
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
            partyInvitation.Version,
            nowUtc)
        {
            IsConfigurationAvailable = configuration is not null
        };
    }
}
