using TheaterInvitations.Web.Components;
using Microsoft.EntityFrameworkCore;
using TheaterInvitations.Web.Data;
using TheaterInvitations.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents();
builder.Services.AddAuthorization();
builder.Services.AddDbContext<InvitationDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddScoped<RsvpService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAntiforgery();
app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorComponents<App>();
app.MapGet("/organizer", () => Results.Ok("Organizer access is configured at the host and application layers."))
    .RequireAuthorization();
app.MapGet("/rsvp/{token}", () => Results.NotFound());

app.Run();
