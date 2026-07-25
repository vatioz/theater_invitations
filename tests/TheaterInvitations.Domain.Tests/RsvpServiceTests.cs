using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using TheaterInvitations.Domain;
using TheaterInvitations.Web.Data;
using TheaterInvitations.Web.Services;

namespace TheaterInvitations.Domain.Tests;

public sealed class RsvpServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Accepted_response_updates_party_and_writes_audit_event()
    {
        await using var db = CreateDb();
        var party = await SeedAsync(db, capacity: 2, deadlineUtc: Now.AddHours(1));
        var service = CreateService(db);

        var result = await service.SubmitAsync("valid-token", new RsvpSubmission(RsvpResponse.Confirm, "Wheelchair space"), "request-1");

        Assert.Equal(RsvpResult.Applied, result.Result);
        db.ChangeTracker.Clear();
        party = await db.InvitationParties.SingleAsync(x => x.Id == party.Id);
        Assert.Equal(InvitationStatus.Confirmed, party.Status);
        var audit = await db.AuditEvents.SingleAsync();
        Assert.Equal("Accepted", audit.Outcome);
        Assert.Equal(party.Id, audit.PartyId);
        Assert.Equal(InvitationStatus.Pending, audit.PreviousStatus);
        Assert.Equal(InvitationStatus.Confirmed, audit.RequestedStatus);
        Assert.Equal(InvitationStatus.Confirmed, audit.ResultingStatus);
    }

    [Fact]
    public async Task Invalid_token_writes_a_sanitized_audit_event()
    {
        await using var db = CreateDb();
        var service = CreateService(db);

        var result = await service.SubmitAsync("unknown-token", new RsvpSubmission(RsvpResponse.Confirm, null), "request-2");

        Assert.False(result.IsValidToken);
        var audit = await db.AuditEvents.SingleAsync();
        Assert.Equal("invalid-token", audit.ReasonCategory);
        Assert.Null(audit.PartyId);
        Assert.Equal(InvitationStatus.Confirmed, audit.RequestedStatus);
    }

    [Fact]
    public async Task Locked_response_is_rejected_and_audited()
    {
        await using var db = CreateDb();
        var party = await SeedAsync(db, capacity: 2, deadlineUtc: Now.AddHours(1), isLocked: true);
        var service = CreateService(db);

        var result = await service.SubmitAsync("valid-token", new RsvpSubmission(RsvpResponse.Confirm, null), "request-3");

        Assert.Equal(RsvpResult.Locked, result.Result);
        Assert.Equal(InvitationStatus.Pending, party.Status);
        Assert.Equal("locked", (await db.AuditEvents.SingleAsync()).ReasonCategory);
    }

    [Fact]
    public async Task Capacity_exceeded_rejects_confirmation_without_mutation()
    {
        await using var db = CreateDb();
        var party = await SeedAsync(db, capacity: 1, deadlineUtc: Now.AddHours(1), seats: 1);
        db.InvitationParties.Add(new InvitationParty
        {
            BatchId = party.BatchId,
            PrimaryGuestName = "Reserved Party",
            Email = "reserved@example.test",
            AllocatedSeats = 1,
            TokenHash = RsvpService.HashToken("other-token")
        });
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var result = await service.SubmitAsync("valid-token", new RsvpSubmission(RsvpResponse.Confirm, null), "request-4");

        Assert.Equal(RsvpResult.CapacityExceeded, result.Result);
        Assert.Equal(InvitationStatus.Pending, party.Status);
        Assert.Equal("capacity-exceeded", (await db.AuditEvents.SingleAsync()).ReasonCategory);
    }

    [Fact]
    public async Task Accessibility_text_over_the_configured_limit_is_rejected()
    {
        await using var db = CreateDb();
        await SeedAsync(db, capacity: 2, deadlineUtc: Now.AddHours(1), accessibilityTextLimit: 3);
        var service = CreateService(db);

        await Assert.ThrowsAsync<ArgumentException>(() => service.SubmitAsync("valid-token", new RsvpSubmission(RsvpResponse.Confirm, "long"), "request-5"));
        Assert.Equal("accessibility-limit-exceeded", (await db.AuditEvents.SingleAsync()).ReasonCategory);
    }

    private static InvitationDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<InvitationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new InvitationDbContext(options);
    }

    private static RsvpService CreateService(InvitationDbContext db) => new(new TestDbContextFactory(db), new FixedClock(Now), new TransactionRetry());

    private static async Task<InvitationParty> SeedAsync(InvitationDbContext db, int capacity, DateTimeOffset deadlineUtc, bool isLocked = false, int seats = 1, int accessibilityTextLimit = 500)
    {
        var batch = new InvitationBatch { Name = "First batch", DeadlineUtc = deadlineUtc, CreatedAtUtc = Now };
        var party = new InvitationParty
        {
            BatchId = batch.Id,
            PrimaryGuestName = "Alex Guest",
            Email = "alex@example.test",
            AllocatedSeats = seats,
            TokenHash = RsvpService.HashToken("valid-token")
        };
        db.Add(new EventConfiguration
        {
            Capacity = capacity,
            EventName = "Theater Gala",
            DoorsAtUtc = new DateTimeOffset(2026, 8, 1, 16, 0, 0, TimeSpan.Zero),
            StartsAtUtc = new DateTimeOffset(2026, 8, 1, 17, 0, 0, TimeSpan.Zero),
            VenueName = "Main Theater",
            VenueAddress = "1 Theater Street",
            TimeZoneId = "Europe/Prague",
            SupportEmail = "rsvp@example.test",
            AccessibilityTextLimit = accessibilityTextLimit,
            IsRsvpLocked = isLocked
        });
        db.Add(batch);
        db.Add(party);
        await db.SaveChangesAsync();
        return party;
    }

    private sealed class FixedClock(DateTimeOffset nowUtc) : IClock
    {
        public DateTimeOffset UtcNow => nowUtc;
    }

    private sealed class TestDbContextFactory(InvitationDbContext db) : IDbContextFactory<InvitationDbContext>
    {
        private readonly DbContextOptions<InvitationDbContext> options = (DbContextOptions<InvitationDbContext>)db.GetService<IDbContextOptions>();
        public InvitationDbContext CreateDbContext() => new(options);
        public Task<InvitationDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
    }
}
