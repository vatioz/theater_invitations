using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using TheaterInvitations.Web.Data;

namespace TheaterInvitations.Domain.Tests;

public sealed class DevelopmentSeedGuardTests
{
    [Theory]
    [InlineData("Development", true)]
    [InlineData("Staging", false)]
    [InlineData("Production", false)]
    public void Seed_execution_depends_on_development_environment(string environmentName, bool expected)
    {
        Assert.Equal(expected, DevelopmentSeedGuard.ShouldSeed(new TestEnvironment(environmentName)));
    }

    [Theory]
    [InlineData("Staging")]
    [InlineData("Production")]
    public async Task Known_development_token_is_absent_when_seed_is_not_invoked(string environmentName)
    {
        await using var db = new InvitationDbContext(new DbContextOptionsBuilder<InvitationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

        if (DevelopmentSeedGuard.ShouldSeed(new TestEnvironment(environmentName)))
        {
            await DevelopmentDataSeeder.SeedAsync(db);
        }

        Assert.False(await DevelopmentSeedGuard.KnownTokenExistsAsync(db));
    }

    private sealed class TestEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
