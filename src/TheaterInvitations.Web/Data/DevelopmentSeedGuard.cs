using Microsoft.EntityFrameworkCore;

namespace TheaterInvitations.Web.Data;

public static class DevelopmentSeedGuard
{
    public static bool ShouldSeed(IHostEnvironment environment) => environment.IsDevelopment();

    public static Task<bool> KnownTokenExistsAsync(InvitationDbContext db, CancellationToken cancellationToken = default)
    {
        var tokenHash = Services.RsvpService.HashToken(DevelopmentDataSeeder.TestRsvpToken);
        return db.InvitationParties.AnyAsync(x => x.TokenHash == tokenHash, cancellationToken);
    }
}
