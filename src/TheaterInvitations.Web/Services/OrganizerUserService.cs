using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TheaterInvitations.Web.Data;

namespace TheaterInvitations.Web.Services;

public sealed class OrganizerUserService(UserManager<ApplicationUser> users, RoleManager<IdentityRole> roles, IOrganizerAuthorization authorization)
{
    public async Task<IReadOnlyList<OrganizerUser>> GetUsersAsync(CancellationToken cancellationToken = default)
    {
        await authorization.RequireAsync("ElevatedOperator", cancellationToken);
        return await users.Users.OrderBy(x => x.Email).Select(x => new OrganizerUser(x.Id, x.Email ?? x.UserName ?? string.Empty, x.IsDisabled)).ToListAsync(cancellationToken);
    }

    public async Task CreateUserAsync(string email, string password, string role, CancellationToken cancellationToken = default)
    {
        await authorization.RequireAsync("ElevatedOperator", cancellationToken);
        if (role is not ("Operator" or "ElevatedOperator")) throw new ArgumentException("Choose a valid organizer role.", nameof(role));
        if (!await roles.RoleExistsAsync(role)) throw new InvalidOperationException("The organizer role is not configured.");
        var user = new ApplicationUser { UserName = email.Trim(), Email = email.Trim(), EmailConfirmed = true };
        var result = await users.CreateAsync(user, password);
        if (!result.Succeeded) throw new ArgumentException(string.Join(" ", result.Errors.Select(x => x.Description)));
        await users.AddToRoleAsync(user, role);
    }

    public async Task DisableUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        await authorization.RequireAsync("ElevatedOperator", cancellationToken);
        var user = await users.FindByIdAsync(userId) ?? throw new InvalidOperationException("Organizer account not found.");
        user.IsDisabled = true;
        user.DisabledAtUtc = DateTimeOffset.UtcNow;
        await users.UpdateAsync(user);
    }
}

public sealed record OrganizerUser(string Id, string Email, bool IsDisabled);
