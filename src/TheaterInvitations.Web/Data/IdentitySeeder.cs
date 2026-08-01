using Microsoft.AspNetCore.Identity;

namespace TheaterInvitations.Web.Data;

public static class IdentitySeeder
{
    public static async Task SeedAsync(IServiceProvider services, IHostEnvironment environment, IConfiguration configuration)
    {
        await using var scope = services.CreateAsyncScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in new[] { "Operator", "ElevatedOperator" })
        {
            if (!await roles.RoleExistsAsync(role)) await roles.CreateAsync(new IdentityRole(role));
        }

        var email = configuration["BootstrapAdmin:Email"];
        var password = configuration["BootstrapAdmin:Password"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password)) return;
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        if (await users.FindByEmailAsync(email) is not null) return;
        var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true };
        var result = await users.CreateAsync(user, password);
        if (!result.Succeeded)
        {
            var errors = string.Join(" ", result.Errors.Select(error => error.Description));
            throw new InvalidOperationException($"Bootstrap organizer account could not be created: {errors}");
        }

        var roleResult = await users.AddToRoleAsync(user, "ElevatedOperator");
        if (!roleResult.Succeeded)
        {
            var errors = string.Join(" ", roleResult.Errors.Select(error => error.Description));
            throw new InvalidOperationException($"Bootstrap organizer role could not be assigned: {errors}");
        }
    }
}
