using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using TheaterInvitations.Web.Data;

namespace TheaterInvitations.Web.Services;

public interface IOrganizerAuthorization
{
    Task<string> RequireAsync(string policy, CancellationToken cancellationToken = default);
}

public sealed class OrganizerAuthorization(AuthenticationStateProvider authenticationStateProvider, IAuthorizationService authorizationService, UserManager<ApplicationUser> users) : IOrganizerAuthorization
{
    public async Task<string> RequireAsync(string policy, CancellationToken cancellationToken = default)
    {
        var user = (await authenticationStateProvider.GetAuthenticationStateAsync()).User;
        if (user.Identity?.IsAuthenticated != true || user.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value is not { } userId || await users.FindByIdAsync(userId) is not { IsDisabled: false })
        {
            throw new UnauthorizedAccessException("Je vyžadován aktivní přihlášený účet pořadatele.");
        }
        var result = await authorizationService.AuthorizeAsync(user, null, policy);
        if (!result.Succeeded)
        {
            throw new UnauthorizedAccessException($"Je vyžadováno oprávnění pořadatele „{policy}“.");
        }

        return user.Identity?.Name ?? throw new UnauthorizedAccessException("Je vyžadována přihlášená identita pořadatele.");
    }
}
