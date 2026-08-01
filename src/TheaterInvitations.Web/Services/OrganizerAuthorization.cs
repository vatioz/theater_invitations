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
            throw new UnauthorizedAccessException("An active authenticated organizer account is required.");
        }
        var result = await authorizationService.AuthorizeAsync(user, null, policy);
        if (!result.Succeeded)
        {
            throw new UnauthorizedAccessException($"The '{policy}' organizer permission is required.");
        }

        return user.Identity?.Name ?? throw new UnauthorizedAccessException("An authenticated organizer identity is required.");
    }
}
