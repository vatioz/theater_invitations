using Microsoft.AspNetCore.Identity;

namespace TheaterInvitations.Web.Data;

public sealed class ApplicationUser : IdentityUser
{
    public bool IsDisabled { get; set; }
    public DateTimeOffset? DisabledAtUtc { get; set; }
}
