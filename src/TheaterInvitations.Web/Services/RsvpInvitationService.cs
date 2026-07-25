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
        var invitation = await (
            from party in db.InvitationParties
            join batch in db.InvitationBatches on party.BatchId equals batch.Id
            from configuration in db.EventConfigurations
            where party.TokenHash == tokenHash
            select new
            {
                party.PrimaryGuestName,
                party.AllocatedSeats,
                party.Status,
                party.AccessibilityRequirements,
                batch.DeadlineUtc,
                configuration.TimeZoneId,
                configuration.SupportEmail,
                configuration.AccessibilityTextLimit,
                configuration.IsRsvpLocked,
                party.Version
            }).SingleOrDefaultAsync(cancellationToken);

        if (invitation is null)
        {
            return null;
        }

        var isExpired = invitation.Status == InvitationStatus.Expired ||
            (invitation.Status == InvitationStatus.Pending && clock.UtcNow >= invitation.DeadlineUtc);
        return new RsvpInvitation(
            invitation.PrimaryGuestName,
            invitation.AllocatedSeats,
            invitation.DeadlineUtc,
            invitation.TimeZoneId,
            invitation.SupportEmail,
            invitation.AccessibilityTextLimit,
            invitation.Status,
            invitation.IsRsvpLocked,
            invitation.AccessibilityRequirements,
            invitation.Version)
        {
            IsExpired = isExpired
        };
    }
}
