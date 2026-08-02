using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using TheaterInvitations.Domain;
using TheaterInvitations.Web.Data;
using TheaterInvitations.Web.Services;

namespace TheaterInvitations.Domain.Tests;

public sealed class OrganizerServiceTests
{
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
    public async Task Correction_round_trips_priority_and_phone_without_auditing_the_phone()
    {
        await using var db = CreateDb();
        var party = await AddPartyAsync(db);

        await CreateService(db).CorrectPartyAsync(party.Id, party.Version, party.PrimaryGuestName, party.Email, null, 1, " +420 777 123 ", party.AllocatedSeats);

        db.ChangeTracker.Clear();
        var updated = await db.InvitationParties.SingleAsync();
        Assert.Equal(1, updated.Priority);
        Assert.Equal("+420 777 123", updated.Phone);
        Assert.DoesNotContain("777", (await db.AuditEvents.SingleAsync()).CorrelationId);
    }

    [Fact]
    public async Task Correction_rejects_invalid_email_server_side()
    {
        await using var db = CreateDb();
        var party = await AddPartyAsync(db);
        var service = CreateService(db);

        await Assert.ThrowsAsync<ArgumentException>(() => service.CorrectPartyAsync(party.Id, party.Version, party.PrimaryGuestName, "not-an-email", null, 1));

        Assert.Equal(party.Email, (await db.InvitationParties.SingleAsync()).Email);
        Assert.Equal("invalid-email", (await db.AuditEvents.SingleAsync()).ReasonCategory);
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
        var audit = await db.AuditEvents.SingleAsync(x => x.Outcome == "Accepted");
        Assert.Equal(InvitationStatus.Pending, audit.PreviousStatus);
        Assert.Equal(InvitationStatus.Confirmed, audit.ResultingStatus);
        Assert.Equal("Approved exception", audit.ReasonCategory);
        Assert.Contains(await db.AuditEvents.ToListAsync(), x => x.EventType == "PartyStatusOverridden" && x.Outcome == "Rejected" && x.ReasonCategory == "missing-reason");
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
    public async Task Preview_writes_no_import_records_and_confirmation_commits_all_data()
    {
        await using var db = CreateDb();
        await SeedConfigurationAsync(db, 10);
        var service = CreateImportService(db);
        var csv = "primary_guest_name,email,allocated_seats,priority,phone\nAlex Guest,alex@example.test,2,1,+420 777 123";

        var preview = await service.PreviewAsync(new BatchImportInput("First wave", new DateTime(2026, 7, 26, 18, 0, 0)), new MemoryStream(System.Text.Encoding.UTF8.GetBytes(csv)));

        Assert.True(preview.IsValid);
        Assert.Empty(db.InvitationBatches);
        Assert.Empty(db.InvitationParties);
        Assert.Empty(db.AuditEvents);
        await service.ConfirmAsync(preview.PreviewId);

        var party = await db.InvitationParties.SingleAsync();
        Assert.Equal(1, party.Priority);
        Assert.Equal("+420 777 123", party.Phone);
        Assert.Single(await db.RsvpTokens.ToListAsync());
        Assert.Equal(InvitationBatchState.Committed, (await db.InvitationBatches.SingleAsync()).State);
    }

    [Fact]
    public async Task Invalid_preview_cannot_be_confirmed_and_is_not_persisted()
    {
        await using var db = CreateDb();
        await SeedConfigurationAsync(db, 10);
        var service = CreateImportService(db);

        var preview = await service.PreviewAsync(new BatchImportInput("Invalid wave", new DateTime(2026, 7, 26, 18, 0, 0)), new MemoryStream(System.Text.Encoding.UTF8.GetBytes("primary_guest_name,email,allocated_seats\n,not-an-email,0")));

        Assert.False(preview.IsValid);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ConfirmAsync(preview.PreviewId));
        Assert.Empty(db.InvitationBatches);
        Assert.Contains(await db.AuditEvents.ToListAsync(), x => x.EventType == "BatchImported" && x.Outcome == "Rejected");
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
        Assert.NotNull((await db.RsvpTokens.SingleAsync(x => x.Id != original.Id)).RawToken);
    }

    [Fact]
    public async Task Approved_template_and_verified_sender_prepare_one_dispatch_per_eligible_batch_party()
    {
        await using var db = CreateDb();
        await SeedConfigurationAsync(db, 10);
        var batch = new InvitationBatch { Name = "Email wave", DeadlineUtc = new DateTimeOffset(2026, 7, 26, 12, 0, 0, TimeSpan.Zero), CreatedAtUtc = DateTimeOffset.UtcNow };
        var party = new InvitationParty { BatchId = batch.Id, PrimaryGuestName = "Alex Guest", Email = "alex@example.test", AllocatedSeats = 2, TokenHash = RsvpService.HashToken("email-token") };
        var token = new RsvpToken { PartyId = party.Id, Hash = party.TokenHash, RawToken = "email-token", IssuedAtUtc = DateTimeOffset.UtcNow };
        db.AddRange(batch, party, token);
        await db.SaveChangesAsync();
        var service = CreateEmailService(db);

        await service.SaveSenderSettingsAsync(new EmailSenderSettingsInput("Theater", "events@theater.org", "support@theater.org", 500, true), null);
        await service.CreateTemplateAsync(new EmailTemplateInput(EmailTemplateType.InitialInvitation, "Join us", "<p>Hello</p>", "Hello"));
        var template = Assert.Single(await service.GetTemplatesAsync());
        await service.ApproveTemplateAsync(template.Id, template.Version);
        var campaign = await service.PrepareInitialCampaignAsync(batch.Id, template.Id);

        Assert.Equal(1, campaign.RecipientCount);
        var dispatch = Assert.Single(await db.EmailDispatches.ToListAsync());
        Assert.Equal(party.Id, dispatch.PartyId);
        Assert.Equal(token.Id, dispatch.TokenId);
        Assert.Equal(EmailDispatchState.Queued, dispatch.State);
        Assert.Equal(EmailCampaignState.ReadyForReview, campaign.State);
        Assert.Contains(await db.AuditEvents.ToListAsync(), x => x.EventType == "EmailCampaignPrepared" && x.EmailCampaignId == campaign.Id);
    }

    [Fact]
    public async Task Campaign_list_projects_dispatch_counts()
    {
        await using var db = CreateDb();
        await SeedConfigurationAsync(db, 10);
        var batch = new InvitationBatch { Name = "Campaign batch", DeadlineUtc = new DateTimeOffset(2026, 7, 26, 12, 0, 0, TimeSpan.Zero), CreatedAtUtc = DateTimeOffset.UtcNow };
        var template = new EmailTemplate { Type = EmailTemplateType.InitialInvitation, VersionNumber = 1, Subject = "Subject", HtmlBody = "<p>Body</p>", PlainTextBody = "Body", State = EmailTemplateState.Approved, ContentDigest = "digest", CreatedAtUtc = DateTimeOffset.UtcNow, CreatedBy = "Test" };
        var campaign = new EmailCampaign { BatchId = batch.Id, TemplateId = template.Id, TemplateVersionNumber = 1, TemplateDigest = "digest", FromDisplayName = "Theater", FromAddress = "events@theater.org", ReplyToAddress = "support@theater.org", CreatedAtUtc = DateTimeOffset.UtcNow, CreatedBy = "Test", QueuedAtUtc = DateTimeOffset.UtcNow, State = EmailCampaignState.Queued };
        db.AddRange(batch, template, campaign);
        await db.SaveChangesAsync();
        var service = CreateEmailService(db);

        var campaigns = await service.GetCampaignsAsync();

        var result = Assert.Single(campaigns);
        Assert.Equal("Campaign batch", result.BatchName);
        Assert.Equal(0, result.RecipientCount);
    }

    [Fact]
    public async Task Campaign_review_renders_preview_and_confirming_it_queues_dispatches()
    {
        await using var db = CreateDb();
        await SeedConfigurationAsync(db, 10);
        var batch = new InvitationBatch { Name = "Review batch", DeadlineUtc = new DateTimeOffset(2026, 7, 26, 12, 0, 0, TimeSpan.Zero), CreatedAtUtc = DateTimeOffset.UtcNow };
        var party = new InvitationParty { BatchId = batch.Id, PrimaryGuestName = "Alex Guest", Email = "alex@example.test", AllocatedSeats = 1, TokenHash = RsvpService.HashToken("review-token") };
        var token = new RsvpToken { PartyId = party.Id, Hash = party.TokenHash, RawToken = "review-token", IssuedAtUtc = DateTimeOffset.UtcNow };
        db.AddRange(batch, party, token);
        await db.SaveChangesAsync();
        var service = CreateEmailService(db);
        await service.SaveSenderSettingsAsync(new EmailSenderSettingsInput("Theater", "events@theater.org", "support@theater.org", 500, true), null);
        await service.CreateTemplateAsync(new EmailTemplateInput(EmailTemplateType.InitialInvitation, "Hello {{guest_name}}", "<p>{{guest_name}} {{rsvp_url}}</p>", "{{guest_name}} {{rsvp_url}}"));
        var template = Assert.Single(await service.GetTemplatesAsync());
        await service.ApproveTemplateAsync(template.Id, template.Version);
        var campaign = await service.PrepareInitialCampaignAsync(batch.Id, template.Id);

        var detail = await service.GetCampaignAsync(campaign.Id);
        Assert.NotNull(detail);
        Assert.Equal(EmailCampaignState.ReadyForReview, detail.State);
        Assert.Contains("Alex Guest", detail.Preview.Subject);
        Assert.Contains("[private RSVP link]", detail.Preview.PlainTextBody);
        await service.ConfirmCampaignAsync(campaign.Id, detail.Version);

        var queued = await service.GetCampaignAsync(campaign.Id);
        Assert.Equal(EmailCampaignState.Queued, queued!.State);
        await service.SendCampaignAsync(campaign.Id, queued.Version);

        var sent = await service.GetCampaignAsync(campaign.Id);
        Assert.Equal(EmailCampaignState.Completed, sent!.State);
        Assert.Equal(EmailDispatchState.Accepted, Assert.Single(sent.Dispatches).State);
    }

    [Fact]
    public async Task Template_rejects_unknown_placeholder()
    {
        await using var db = CreateDb();
        var service = CreateEmailService(db);

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateTemplateAsync(new EmailTemplateInput(EmailTemplateType.InitialInvitation, "{{unknown}}", "Hello", "Hello")));
    }

    [Fact]
    public async Task Party_email_history_returns_safe_dispatch_details()
    {
        await using var db = CreateDb();
        await SeedConfigurationAsync(db, 10);
        var batch = new InvitationBatch { Name = "Email history batch", DeadlineUtc = new DateTimeOffset(2026, 7, 26, 12, 0, 0, TimeSpan.Zero), CreatedAtUtc = DateTimeOffset.UtcNow };
        var party = new InvitationParty { BatchId = batch.Id, PrimaryGuestName = "Alex Guest", Email = "alex@example.test", AllocatedSeats = 1, TokenHash = RsvpService.HashToken("history-token") };
        var token = new RsvpToken { PartyId = party.Id, Hash = party.TokenHash, RawToken = "history-token", IssuedAtUtc = DateTimeOffset.UtcNow };
        var template = new EmailTemplate { Type = EmailTemplateType.InitialInvitation, VersionNumber = 1, Subject = "Subject", HtmlBody = "<p>Body</p>", PlainTextBody = "Body", State = EmailTemplateState.Approved, ContentDigest = "digest", CreatedAtUtc = DateTimeOffset.UtcNow, CreatedBy = "Test" };
        var campaign = new EmailCampaign { BatchId = batch.Id, TemplateId = template.Id, TemplateVersionNumber = 1, TemplateDigest = "digest", FromDisplayName = "Theater", FromAddress = "events@theater.org", ReplyToAddress = "support@theater.org", CreatedAtUtc = DateTimeOffset.UtcNow, CreatedBy = "Test", QueuedAtUtc = DateTimeOffset.UtcNow, State = EmailCampaignState.Completed };
        var dispatch = new EmailDispatch { CampaignId = campaign.Id, PartyId = party.Id, TokenId = token.Id, RecipientEmail = party.Email, RecipientName = party.PrimaryGuestName, AllocatedSeats = 1, DeadlineUtc = batch.DeadlineUtc, IdempotencyKey = "history/dispatch", State = EmailDispatchState.Accepted, AttemptCount = 1, AcceptedAtUtc = DateTimeOffset.UtcNow, ProviderMessageId = "provider-id" };
        db.AddRange(batch, party, token, template, campaign, dispatch);
        await db.SaveChangesAsync();

        var history = await CreateService(db).GetPartyEmailDispatchesAsync(party.Id);

        var result = Assert.Single(history);
        Assert.Equal("Email history batch", result.BatchName);
        Assert.Equal(EmailDispatchState.Accepted, result.State);
        Assert.Null(result.FailureCategory);
    }

    [Fact]
    public async Task Reminder_campaign_allows_each_active_pending_party_once()
    {
        await using var db = CreateDb();
        await SeedConfigurationAsync(db, 10);
        var batch = new InvitationBatch { Name = "Reminder batch", DeadlineUtc = new DateTimeOffset(2026, 7, 26, 12, 0, 0, TimeSpan.Zero), CreatedAtUtc = DateTimeOffset.UtcNow };
        var party = new InvitationParty { BatchId = batch.Id, PrimaryGuestName = "Alex Guest", Email = "alex@example.test", AllocatedSeats = 1, TokenHash = RsvpService.HashToken("reminder-token") };
        var token = new RsvpToken { PartyId = party.Id, Hash = party.TokenHash, RawToken = "reminder-token", IssuedAtUtc = DateTimeOffset.UtcNow };
        var template = new EmailTemplate { Type = EmailTemplateType.Reminder, VersionNumber = 1, Subject = "Reminder", HtmlBody = "<p>Reminder</p>", PlainTextBody = "Reminder", State = EmailTemplateState.Approved, ContentDigest = "reminder-digest", CreatedAtUtc = DateTimeOffset.UtcNow, CreatedBy = "Test" };
        db.AddRange(batch, party, token, template);
        await db.SaveChangesAsync();
        var service = CreateEmailService(db);
        await service.SaveSenderSettingsAsync(new EmailSenderSettingsInput("Theater", "events@theater.org", "support@theater.org", 500, true), null);

        var campaign = await service.PrepareReminderCampaignAsync(batch.Id, template.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.PrepareReminderCampaignAsync(batch.Id, template.Id));

        Assert.Equal(1, campaign.RecipientCount);
    }

    private static InvitationDbContext CreateDb() => new(new DbContextOptionsBuilder<InvitationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private static OrganizerService CreateService(InvitationDbContext db, IOrganizerAuthorization? authorization = null, string environmentName = "Development") => new(db, new TestDbContextFactory(db), new FixedClock(), authorization ?? new AllowedAuthorization(), new TransactionRetry(), new TestEnvironment(environmentName));
    private static BatchImportService CreateImportService(InvitationDbContext db, IOrganizerAuthorization? authorization = null) => new(db, new TestDbContextFactory(db), new FixedClock(), authorization ?? new AllowedAuthorization(), new TransactionRetry(), new BatchImportPreviewStore());
    private static EmailCampaignService CreateEmailService(InvitationDbContext db) => new(db, new TestDbContextFactory(db), new AllowedAuthorization(), new FixedClock(), new TransactionRetry(), new EmailTemplateRenderer(), new AcceptedEmailProvider(), new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["PublicApp:BaseUrl"] = "https://rsvp.example.org" }).Build());

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


    private sealed class AcceptedEmailProvider : IEmailProvider
    {
        public Task<EmailProviderResult> SendAsync(EmailProviderMessage message, CancellationToken cancellationToken) => Task.FromResult(new EmailProviderResult(true, false, "provider-message", null));
    }
}
