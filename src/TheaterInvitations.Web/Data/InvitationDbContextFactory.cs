using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TheaterInvitations.Web.Data;

public sealed class InvitationDbContextFactory : IDesignTimeDbContextFactory<InvitationDbContext>
{
    public InvitationDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
            ?? "Host=localhost;Database=theater_invitations;Username=postgres;Password=postgres";
        var options = new DbContextOptionsBuilder<InvitationDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new InvitationDbContext(options);
    }
}
