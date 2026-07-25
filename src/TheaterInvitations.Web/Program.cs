using TheaterInvitations.Web.Components;
using Microsoft.EntityFrameworkCore;
using TheaterInvitations.Web.Data;
using TheaterInvitations.Web.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
        .AddCookie(options => options.LoginPath = "/dev/login");
}
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("OrganizerViewer", policy => policy.RequireRole("Viewer", "Operator", "ElevatedOperator"));
    options.AddPolicy("OrganizerOperator", policy => policy.RequireRole("Operator", "ElevatedOperator"));
    options.AddPolicy("ElevatedOperator", policy => policy.RequireRole("ElevatedOperator"));
});
builder.Services.AddDbContext<InvitationDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));
builder.Services.AddSingleton<IClock, TheaterInvitations.Web.Services.SystemClock>();
builder.Services.AddScoped<RsvpService>();
builder.Services.AddScoped<RsvpInvitationService>();
builder.Services.AddScoped<OrganizerService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<InvitationDbContext>();
    await db.Database.MigrateAsync();
    await DevelopmentDataSeeder.SeedAsync(db);
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.Use(async (context, next) =>
{
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    await next();
});
app.UseAntiforgery();
app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();
if (app.Environment.IsDevelopment())
{
    app.MapGet("/dev/login/{role}", async (string role, HttpContext context) =>
    {
        var allowedRoles = new[] { "Viewer", "Operator", "ElevatedOperator" };
        if (!allowedRoles.Contains(role, StringComparer.Ordinal))
        {
            return Results.NotFound();
        }

        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, $"development-{role.ToLowerInvariant()}"),
            new Claim(ClaimTypes.Name, $"Development {role}"),
            new Claim(ClaimTypes.Role, role)
        }, CookieAuthenticationDefaults.AuthenticationScheme);
        await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
        return Results.Redirect("/organizer");
    });
    app.MapGet("/dev/logout", async (HttpContext context) =>
    {
        await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Results.Redirect("/");
    });
}

app.Run();
