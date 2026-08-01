using TheaterInvitations.Web.Components;
using Microsoft.EntityFrameworkCore;
using TheaterInvitations.Web.Data;
using TheaterInvitations.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddIdentityCore<ApplicationUser>(options =>
{
    options.User.RequireUniqueEmail = true;
    options.Password.RequiredLength = 12;
    options.Password.RequireNonAlphanumeric = true;
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
})
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<InvitationDbContext>()
    .AddSignInManager();
builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme)
    .AddIdentityCookies();
builder.Services.Configure<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme, options => options.LoginPath = "/account/login");
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("OrganizerViewer", policy => policy.RequireRole("Operator", "ElevatedOperator"));
    options.AddPolicy("OrganizerOperator", policy => policy.RequireRole("Operator", "ElevatedOperator"));
    options.AddPolicy("ElevatedOperator", policy => policy.RequireRole("ElevatedOperator"));
});
builder.Services.AddDbContext<InvitationDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));
builder.Services.AddDbContextFactory<InvitationDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")), ServiceLifetime.Scoped);
builder.Services.AddSingleton<IClock, TheaterInvitations.Web.Services.SystemClock>();
builder.Services.AddScoped<RsvpService>();
builder.Services.AddScoped<RsvpInvitationService>();
builder.Services.AddScoped<OrganizerService>();
builder.Services.AddScoped<EmailCampaignService>();
builder.Services.AddScoped<OrganizerUserService>();
builder.Services.AddSingleton<EmailTemplateRenderer>();
builder.Services.AddScoped<IOrganizerAuthorization, OrganizerAuthorization>();
builder.Services.AddSingleton<ITransactionRetry, TransactionRetry>();
builder.Services.AddHttpClient<IEmailProvider, ResendEmailProvider>();

var app = builder.Build();

if (DevelopmentSeedGuard.ShouldSeed(app.Environment))
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<InvitationDbContext>();
    await db.Database.MigrateAsync();
    await DevelopmentDataSeeder.SeedAsync(db);
}

await IdentitySeeder.SeedAsync(app.Services, app.Environment, app.Configuration);

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
app.MapGet("/account/logout", async (SignInManager<ApplicationUser> signInManager) => { await signInManager.SignOutAsync(); return Results.Redirect("/account/login"); });

app.Run();
