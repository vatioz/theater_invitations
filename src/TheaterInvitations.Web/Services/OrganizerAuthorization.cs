using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;

namespace TheaterInvitations.Web.Services;

public interface IOrganizerAuthorization
{
    Task<string> RequireAsync(string policy, CancellationToken cancellationToken = default);
}

public sealed class OrganizerAuthorization(AuthenticationStateProvider authenticationStateProvider, IAuthorizationService authorizationService) : IOrganizerAuthorization
{
    public async Task<string> RequireAsync(string policy, CancellationToken cancellationToken = default)
    {
        var user = (await authenticationStateProvider.GetAuthenticationStateAsync()).User;
        var result = await authorizationService.AuthorizeAsync(user, null, policy);
        if (!result.Succeeded)
        {
            throw new UnauthorizedAccessException($"The '{policy}' organizer permission is required.");
        }

        return user.Identity?.Name ?? throw new UnauthorizedAccessException("An authenticated organizer identity is required.");
    }
}
