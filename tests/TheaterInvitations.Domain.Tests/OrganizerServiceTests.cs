using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using TheaterInvitations.Domain;
using TheaterInvitations.Web.Data;
using TheaterInvitations.Web.Services;

namespace TheaterInvitations.Domain.Tests;

public sealed class OrganizerServiceTests
{
    [Fact]
    public async Task Preview_supports_quoted_company_names_and_commits_only_after_confirmation()
    {
        await using var db = CreateDb();
        await SeedConfigurationAsync(db, 10);
        var service = CreateService(db);
        const string csv = "primary_guest_name,email,company,allocated_seats\nAlex Guest,alex@example.test,\"Example, Inc.\",2";

        var preview = await service.PreviewImportAsync(csv);

        Assert.True(preview.IsValid);
        Assert.Equal("Example, Inc.", preview.ValidRows.Single().Company);
        Assert.Empty(db.InvitationParties);

        await service.CommitImportAsync(preview, "First import");

        Assert.Single(db.InvitationParties);
        Assert.Single(db.InvitationBatches);
    }

    [Fact]
    public async Task Preview_rejects_duplicate_email_against_existing_party()
    {
        await using var db = CreateDb();
        await SeedConfigurationAsync(db, 10);
        var batch = new InvitationBatch { Name = "Existing", DeadlineUtc = DateTimeOffset.UtcNow.AddDays(1), CreatedAtUtc = DateTimeOffset.UtcNow };
        db.Add(batch);
        db.Add(new InvitationParty { BatchId = batch.Id, PrimaryGuestName = "Existing", Email = "alex@example.test", AllocatedSeats = 1, TokenHash = RsvpService.HashToken("existing") });
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var preview = await service.PreviewImportAsync("primary_guest_name,email,company,allocated_seats\nDuplicate,alex@example.test,,1");

        Assert.False(preview.IsValid);
        Assert.Contains(preview.Errors, x => x.Contains("duplicate", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Preview_identifies_the_invalid_seat_column()
    {
        await using var db = CreateDb();
        await SeedConfigurationAsync(db, 10);
        var service = CreateService(db);

        var preview = await service.PreviewImportAsync("primary_guest_name,email,company,allocated_seats\nAlex Guest,alex@example.test,,0");

        Assert.Equal("Row 2: allocated_seats must be a positive integer.", Assert.Single(preview.Errors));
    }

    [Fact]
    public async Task Preview_rejects_invalid_email_and_preserves_unicode_multiline_fields()
    {
        await using var db = CreateDb();
        await SeedConfigurationAsync(db, 10);
        var service = CreateService(db);

        var invalid = await service.PreviewImportAsync("primary_guest_name,email,company,allocated_seats\nAlex,not-an-email,,1");
        var valid = await service.PreviewImportAsync("\uFEFFprimary_guest_name,email,company,allocated_seats\n Žaneta , zaneta@example.test ,\"Divadlo\nČeský Krumlov\",1");

        Assert.Equal("Row 2: email must be a valid address.", Assert.Single(invalid.Errors));
        Assert.True(valid.IsValid);
        Assert.Equal("Žaneta", valid.ValidRows.Single().Name);
        Assert.Equal("zaneta@example.test", valid.ValidRows.Single().Email);
        Assert.Equal("Divadlo\nČeský Krumlov", valid.ValidRows.Single().Company);
    }

    [Fact]
    public async Task Preview_reports_malformed_csv_and_size_limit()
    {
        await using var db = CreateDb();
        await SeedConfigurationAsync(db, 10);
        var service = CreateService(db);

        var malformed = await service.PreviewImportAsync("primary_guest_name,email,company,allocated_seats\nAlex,alex@example.test,\"Unclosed,1");
        var oversized = await service.PreviewImportAsync(new string('a', 1_000_001));

        Assert.Contains("Malformed CSV", Assert.Single(malformed.Errors));
        Assert.Equal("CSV must be 1 MB or smaller.", Assert.Single(oversized.Errors));
    }

    [Fact]
    public async Task Correction_updates_party_and_records_the_organizer()
    {
        await using var db = CreateDb();
        var party = await AddPartyAsync(db);
        var service = CreateService(db);

        await service.CorrectPartyAsync(party.Id, party.Version, "Updated Guest", "updated@example.test", "Updated Co", 2);

        db.ChangeTracker.Clear();
        party = await db.InvitationParties.SingleAsync(x => x.Id == party.Id);
        Assert.Equal("Updated Guest", party.PrimaryGuestName);
        Assert.Equal("updated@example.test", party.Email);
        Assert.Equal(2, party.AllocatedSeats);
        var audit = await db.AuditEvents.SingleAsync();
        Assert.Equal("PartyCorrected", audit.EventType);
        Assert.Equal("Development Operator", audit.ActorIdentifier);
    }

    [Fact]
    public async Task Correction_rejects_invalid_email_server_side()
    {
        await using var db = CreateDb();
        var party = await AddPartyAsync(db);
        var service = CreateService(db);

        await Assert.ThrowsAsync<ArgumentException>(() => service.CorrectPartyAsync(party.Id, party.Version, party.PrimaryGuestName, "not-an-email", null, 1));

        Assert.Equal(party.Email, (await db.InvitationParties.SingleAsync()).Email);
    }

    [Fact]
    public async Task Status_override_requires_reason_and_records_transition()
    {
        await using var db = CreateDb();
        var party = await AddPartyAsync(db);
        var service = CreateService(db);

        await Assert.ThrowsAsync<ArgumentException>(() => service.OverrideStatusAsync(party.Id, party.Version, InvitationStatus.Confirmed, ""));
        await service.OverrideStatusAsync(party.Id, party.Version, InvitationStatus.Confirmed, "Approved exception");

        db.ChangeTracker.Clear();
        party = await db.InvitationParties.SingleAsync(x => x.Id == party.Id);
        Assert.Equal(InvitationStatus.Confirmed, party.Status);
        var audit = await db.AuditEvents.SingleAsync();
        Assert.Equal(InvitationStatus.Pending, audit.PreviousStatus);
        Assert.Equal(InvitationStatus.Confirmed, audit.ResultingStatus);
        Assert.Equal("Approved exception", audit.ReasonCategory);
    }

    [Fact]
    public async Task Dashboard_searches_and_pages_parties()
    {
        await using var db = CreateDb();
        await AddPartyAsync(db, "Alex Guest", "alex@example.test");
        await AddPartyAsync(db, "Morgan Guest", "morgan@example.test");
        var service = CreateService(db);

        var result = await service.GetDashboardAsync(new PartyQuery(Search: "Morgan", PageSize: 1));

        Assert.Equal(1, result.PartyCount);
        Assert.Single(result.Parties);
        Assert.Equal("Morgan Guest", result.Parties.Single().Name);
    }

    [Fact]
    public async Task Dashboard_active_pending_and_remaining_capacity_exclude_effectively_expired_parties()
    {
        await using var db = CreateDb();
        await SeedConfigurationAsync(db, 10);
        var activeBatch = new InvitationBatch { Name = "Active", DeadlineUtc = new DateTimeOffset(2026, 7, 25, 13, 0, 0, TimeSpan.Zero), CreatedAtUtc = DateTimeOffset.UtcNow };
        var expiredBatch = new InvitationBatch { Name = "Expired", DeadlineUtc = new DateTimeOffset(2026, 7, 25, 12, 0, 0, TimeSpan.Zero), CreatedAtUtc = DateTimeOffset.UtcNow };
        db.AddRange(activeBatch, expiredBatch);
        db.AddRange(
            new InvitationParty { BatchId = activeBatch.Id, PrimaryGuestName = "Confirmed", Email = "confirmed@example.test", AllocatedSeats = 2, TokenHash = RsvpService.HashToken("confirmed") },
            new InvitationParty { BatchId = activeBatch.Id, PrimaryGuestName = "Pending", Email = "pending@example.test", AllocatedSeats = 3, TokenHash = RsvpService.HashToken("pending") },
            new InvitationParty { BatchId = expiredBatch.Id, PrimaryGuestName = "Expired pending", Email = "expired@example.test", AllocatedSeats = 4, TokenHash = RsvpService.HashToken("expired") });
        await db.SaveChangesAsync();
        var confirmed = await db.InvitationParties.SingleAsync(x => x.Email == "confirmed@example.test");
        confirmed.OverrideStatus(InvitationStatus.Confirmed, DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();

        var dashboard = await CreateService(db).GetDashboardAsync();

        Assert.Equal(2, dashboard.ConfirmedSeats);
        Assert.Equal(3, dashboard.ActivePendingSeats);
        Assert.Equal(5, dashboard.RemainingCapacity);
    }

    [Fact]
    public async Task Party_queries_filter_by_stable_batch_id()
    {
        await using var db = CreateDb();
        await SeedConfigurationAsync(db, 10);
        var first = new InvitationBatch { Name = "First", DeadlineUtc = DateTimeOffset.UtcNow.AddDays(1), CreatedAtUtc = DateTimeOffset.UtcNow };
        var second = new InvitationBatch { Name = "Second", DeadlineUtc = DateTimeOffset.UtcNow.AddDays(1), CreatedAtUtc = DateTimeOffset.UtcNow };
        db.AddRange(first, second);
        db.AddRange(
            new InvitationParty { BatchId = first.Id, PrimaryGuestName = "First guest", Email = "first@example.test", AllocatedSeats = 1, TokenHash = RsvpService.HashToken("first") },
            new InvitationParty { BatchId = second.Id, PrimaryGuestName = "Second guest", Email = "second@example.test", AllocatedSeats = 1, TokenHash = RsvpService.HashToken("second") });
        await db.SaveChangesAsync();

        var dashboard = await CreateService(db).GetDashboardAsync(new PartyQuery(BatchId: second.Id));

        Assert.Equal(1, dashboard.PartyCount);
        Assert.Equal("Second guest", Assert.Single(dashboard.Parties).Name);
    }

    [Fact]
    public async Task Mutation_rejects_stale_version()
    {
        await using var db = CreateDb();
        var party = await AddPartyAsync(db);
        var service = CreateService(db);

        await Assert.ThrowsAsync<StaleDataException>(() => service.CorrectPartyAsync(party.Id, party.Version + 1, "Updated", party.Email, null, 1));
    }

    [Fact]
    public async Task Mutation_requires_service_level_authorization()
    {
        await using var db = CreateDb();
        var party = await AddPartyAsync(db);
        var service = CreateService(db, new DeniedAuthorization());

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.CorrectPartyAsync(party.Id, party.Version, party.PrimaryGuestName, party.Email, null, 1));
    }

    [Fact]
    public async Task Elevated_operator_updates_support_email_and_records_sanitized_audit()
    {
        await using var db = CreateDb();
        await SeedConfigurationAsync(db, 10);
        var service = CreateService(db, environmentName: Environments.Production);
        var dashboard = await service.GetDashboardAsync();

        await service.UpdateSupportEmailAsync("help@theater.org", dashboard.Configuration!.Version);

        db.ChangeTracker.Clear();
        Assert.Equal("help@theater.org", (await db.EventConfigurations.SingleAsync()).SupportEmail);
        var audit = await db.AuditEvents.SingleAsync();
        Assert.Equal("SupportEmailChanged", audit.EventType);
        Assert.Equal("Accepted", audit.Outcome);
        Assert.Equal("Development ElevatedOperator", audit.ActorIdentifier);
        Assert.Null(audit.ReasonCategory);
    }

    [Fact]
    public async Task Invalid_production_support_email_is_rejected_and_audited_without_address()
    {
        await using var db = CreateDb();
        await SeedConfigurationAsync(db, 10);
        var service = CreateService(db, environmentName: Environments.Production);
        var dashboard = await service.GetDashboardAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => service.UpdateSupportEmailAsync("not-an-email", dashboard.Configuration!.Version));

        db.ChangeTracker.Clear();
        Assert.Equal("rsvp@example.test", (await db.EventConfigurations.SingleAsync()).SupportEmail);
        var audit = await db.AuditEvents.SingleAsync();
        Assert.Equal("Rejected", audit.Outcome);
        Assert.Equal("invalid-email", audit.ReasonCategory);
        Assert.DoesNotContain("not-an-email", audit.CorrelationId);
    }

    [Fact]
    public async Task Stale_support_email_update_is_rejected_and_current_value_is_preserved()
    {
        await using var db = CreateDb();
        await SeedConfigurationAsync(db, 10);
        var service = CreateService(db);

        await Assert.ThrowsAsync<StaleDataException>(() => service.UpdateSupportEmailAsync("help@theater.org", 42));

        Assert.Equal("rsvp@example.test", (await db.EventConfigurations.AsNoTracking().SingleAsync()).SupportEmail);
        Assert.Equal("stale", (await db.AuditEvents.SingleAsync()).ReasonCategory);
    }

    [Fact]
    public async Task Support_email_update_requires_elevated_authorization()
    {
        await using var db = CreateDb();
        await SeedConfigurationAsync(db, 10);
        var service = CreateService(db, new DeniedAuthorization());

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.UpdateSupportEmailAsync("help@theater.org", 0));
    }

    [Fact]
    public async Task Elevated_operator_can_create_missing_event_configuration()
    {
        await using var db = CreateDb();
        var service = CreateService(db);

        await service.SaveEventConfigurationAsync(new EventConfigurationInput(340, "Theater Gala", new DateTime(2026, 10, 17, 18, 30, 0), new DateTime(2026, 10, 17, 19, 30, 0), "Main Theater", "1 Theater Street", "Smart casual", "Europe/Prague", "help@theater.org", 500), null);

        var configuration = await db.EventConfigurations.SingleAsync();
        Assert.Equal("Theater Gala", configuration.EventName);
        Assert.Equal("help@theater.org", configuration.SupportEmail);
        var audits = await db.AuditEvents.ToListAsync();
        Assert.Contains(audits, x => x.EventType == "EventConfigurationSaved" && x.Outcome == "Accepted");
        Assert.Contains(audits, x => x.EventType == "SupportEmailChanged" && x.Outcome == "Accepted");
    }

    [Fact]
    public async Task Saving_a_valid_draft_persists_rows_without_creating_live_parties()
    {
        await using var db = CreateDb();
        await SeedConfigurationAsync(db, 10);
        var service = CreateService(db);

        var draft = await service.SaveDraftAsync(new BatchDraftInput("First wave", new DateTime(2026, 7, 26, 18, 0, 0)), "primary_guest_name,email,company,allocated_seats\nAlex Guest,alex@example.test,,2");

        Assert.Equal(InvitationBatchState.Prepared, draft.State);
        Assert.Single(draft.Rows);
        Assert.Empty(db.InvitationParties);
        Assert.Single(db.InvitationDraftRows);
        Assert.Equal("BatchDraftSaved", (await db.AuditEvents.SingleAsync()).EventType);
    }

    [Fact]
    public async Task Operator_can_delete_an_uncommitted_draft_and_its_rows()
    {
        await using var db = CreateDb();
        await SeedConfigurationAsync(db, 10);
        var service = CreateService(db);
        var draft = await service.SaveDraftAsync(new BatchDraftInput("Disposable wave", new DateTime(2026, 7, 26, 18, 0, 0)), "primary_guest_name,email,company,allocated_seats\nAlex Guest,alex@example.test,,1");

        await service.DeleteDraftAsync(draft.Id, draft.Version);

        Assert.Empty(db.InvitationBatches);
        Assert.Empty(db.InvitationDraftRows);
        Assert.Contains(await db.AuditEvents.ToListAsync(), x => x.EventType == "BatchDraftDeleted" && x.Outcome == "Accepted");
    }

    [Fact]
    public async Task Committing_a_prepared_draft_creates_party_token_and_delivery_envelope_together()
    {
        await using var db = CreateDb();
        await SeedConfigurationAsync(db, 10);
        var service = CreateService(db);
        var draft = await service.SaveDraftAsync(new BatchDraftInput("First wave", new DateTime(2026, 7, 26, 18, 0, 0)), "primary_guest_name,email,company,allocated_seats\nAlex Guest,alex@example.test,,2");

        await service.CommitDraftAsync(draft.Id, draft.Version);

        var batch = await db.InvitationBatches.SingleAsync();
        Assert.Equal(InvitationBatchState.Committed, batch.State);
        var party = await db.InvitationParties.SingleAsync();
        var token = await db.RsvpTokens.SingleAsync();
        var envelope = await db.ProtectedDeliveryEnvelopes.SingleAsync();
        Assert.Equal(party.Id, token.PartyId);
        Assert.Equal(party.TokenHash, token.Hash);
        Assert.Equal(token.Id, envelope.TokenId);
        Assert.Contains(await db.AuditEvents.ToListAsync(), x => x.EventType == "BatchCommitted" && x.Outcome == "Accepted");
    }

    [Fact]
    public async Task Extending_a_batch_deadline_reopens_only_system_expired_parties()
    {
        await using var db = CreateDb();
        await SeedConfigurationAsync(db, 10);
        var batch = new InvitationBatch { Name = "Expired wave", DeadlineUtc = new DateTimeOffset(2026, 7, 25, 11, 0, 0, TimeSpan.Zero), CreatedAtUtc = DateTimeOffset.UtcNow };
        var systemExpired = new InvitationParty { BatchId = batch.Id, PrimaryGuestName = "System", Email = "system@example.test", AllocatedSeats = 1, TokenHash = RsvpService.HashToken("system") };
        var overridden = new InvitationParty { BatchId = batch.Id, PrimaryGuestName = "Override", Email = "override@example.test", AllocatedSeats = 1, TokenHash = RsvpService.HashToken("override") };
        db.AddRange(batch, systemExpired, overridden);
        await db.SaveChangesAsync();
        systemExpired.Respond(RsvpResponse.Confirm, null, batch.DeadlineUtc, false, new DateTimeOffset(2026, 7, 25, 12, 0, 0, TimeSpan.Zero));
        overridden.OverrideStatus(InvitationStatus.Expired, DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();

        await CreateService(db).ChangeBatchDeadlineAsync(batch.Id, batch.Version, new DateTime(2026, 7, 26, 18, 0, 0), "Venue extension");

        db.ChangeTracker.Clear();
        var reloadedParties = await db.InvitationParties.OrderBy(x => x.Email).ToListAsync();
        Assert.Equal(InvitationStatus.Expired, reloadedParties[0].Status);
        Assert.Equal(ExpirationSource.OrganizerOverride, reloadedParties[0].ExpirationSource);
        Assert.Equal(InvitationStatus.Pending, reloadedParties[1].Status);
    }

    [Fact]
    public async Task Regenerating_an_rsvp_token_revokes_the_prior_token_and_prepares_a_replacement()
    {
        await using var db = CreateDb();
        var party = await AddPartyAsync(db);
        var original = new RsvpToken { PartyId = party.Id, Hash = party.TokenHash, IssuedAtUtc = DateTimeOffset.UtcNow };
        db.RsvpTokens.Add(original);
        await db.SaveChangesAsync();

        await CreateService(db).RegenerateRsvpTokenAsync(party.Id, "Address correction");

        db.ChangeTracker.Clear();
        var tokens = await db.RsvpTokens.ToListAsync();
        Assert.Equal(2, tokens.Count);
        var revoked = tokens.Single(x => x.Id == original.Id);
        var replacement = tokens.Single(x => x.Id != original.Id);
        Assert.NotNull(revoked.RevokedAtUtc);
        Assert.Null(replacement.RevokedAtUtc);
        Assert.NotEqual(revoked.Hash, replacement.Hash);
        Assert.Equal(replacement.Hash, (await db.InvitationParties.SingleAsync()).TokenHash);
        Assert.Single(await db.ProtectedDeliveryEnvelopes.ToListAsync());
    }

    private static InvitationDbContext CreateDb() => new(new DbContextOptionsBuilder<InvitationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private static OrganizerService CreateService(InvitationDbContext db, IOrganizerAuthorization? authorization = null, string environmentName = "Development") => new(db, new TestDbContextFactory(db), new FixedClock(), authorization ?? new AllowedAuthorization(), new TransactionRetry(), new TestEnvironment(environmentName), new TestEnvelopeProtector());

    private static async Task SeedConfigurationAsync(InvitationDbContext db, int capacity)
    {
        db.EventConfigurations.Add(new EventConfiguration
        {
            Capacity = capacity,
            EventName = "Theater Gala",
            DoorsAtUtc = new DateTimeOffset(2026, 8, 1, 16, 0, 0, TimeSpan.Zero),
            StartsAtUtc = new DateTimeOffset(2026, 8, 1, 17, 0, 0, TimeSpan.Zero),
            VenueName = "Main Theater",
            VenueAddress = "1 Theater Street",
            TimeZoneId = "Europe/Prague",
            SupportEmail = "rsvp@example.test",
            AccessibilityTextLimit = 500
        });
        await db.SaveChangesAsync();
    }

    private static async Task<InvitationParty> AddPartyAsync(InvitationDbContext db, string name = "Alex Guest", string email = "alex@example.test")
    {
        if (!await db.EventConfigurations.AnyAsync()) await SeedConfigurationAsync(db, 10);
        var batch = new InvitationBatch { Name = "Batch", DeadlineUtc = DateTimeOffset.UtcNow.AddDays(1), CreatedAtUtc = DateTimeOffset.UtcNow };
        var party = new InvitationParty { BatchId = batch.Id, PrimaryGuestName = name, Email = email, AllocatedSeats = 1, TokenHash = RsvpService.HashToken(Guid.NewGuid().ToString()) };
        db.Add(batch); db.Add(party);
        await db.SaveChangesAsync();
        return party;
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 7, 25, 12, 0, 0, TimeSpan.Zero);
    }

    private sealed class TestDbContextFactory(InvitationDbContext db) : IDbContextFactory<InvitationDbContext>
    {
        private readonly DbContextOptions<InvitationDbContext> options = (DbContextOptions<InvitationDbContext>)db.GetService<IDbContextOptions>();
        public InvitationDbContext CreateDbContext() => new(options);
        public Task<InvitationDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
    }

    private sealed class AllowedAuthorization : IOrganizerAuthorization
    {
        public Task<string> RequireAsync(string policy, CancellationToken cancellationToken = default) => Task.FromResult(policy == "ElevatedOperator" ? "Development ElevatedOperator" : "Development Operator");
    }

    private sealed class DeniedAuthorization : IOrganizerAuthorization
    {
        public Task<string> RequireAsync(string policy, CancellationToken cancellationToken = default) => throw new UnauthorizedAccessException();
    }

    private sealed class TestEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class TestEnvelopeProtector : IDeliveryEnvelopeProtector
    {
        public byte[] Protect(string token) => System.Text.Encoding.UTF8.GetBytes(token);
    }
}
