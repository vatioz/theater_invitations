using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using TheaterInvitations.Domain;
using TheaterInvitations.Web.Data;

namespace TheaterInvitations.Web.Services;

public sealed class EmailCampaignService(InvitationDbContext db, IDbContextFactory<InvitationDbContext> dbFactory, IOrganizerAuthorization authorization, IClock clock, ITransactionRetry retry, EmailTemplateRenderer renderer, IEmailProvider emailProvider, IConfiguration configuration)
{
    public async Task<EmailSenderSettings?> GetSenderSettingsAsync(CancellationToken cancellationToken = default)
    {
        var settings = await db.EmailSenderConfigurations.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        return settings is null ? null : new EmailSenderSettings(settings.FromDisplayName, settings.FromAddress, settings.ReplyToAddress, settings.DailySendCeiling, settings.IsDomainVerified, settings.Version);
    }

    public async Task SaveSenderSettingsAsync(EmailSenderSettingsInput input, uint? expectedVersion, CancellationToken cancellationToken = default)
    {
        var actor = await authorization.RequireAsync("ElevatedOperator", cancellationToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.FromDisplayName);
        if (input.FromDisplayName.Trim().Length > 200 || input.DailySendCeiling <= 0) throw new ArgumentException("Enter a sender name of 200 characters or fewer and a positive daily send ceiling.");
        var from = PartyEmailValidation.Normalize(input.FromAddress);
        var replyTo = PartyEmailValidation.Normalize(input.ReplyToAddress);
        var settings = await db.EmailSenderConfigurations.SingleOrDefaultAsync(cancellationToken);
        if (settings is null)
        {
            settings = new EmailSenderConfiguration();
            db.EmailSenderConfigurations.Add(settings);
        }
        else if (expectedVersion is not null && settings.Version != expectedVersion)
        {
            throw new StaleDataException("Email sender settings changed after you opened them. The current values have been loaded.");
        }

        settings.FromDisplayName = input.FromDisplayName.Trim();
        settings.FromAddress = from;
        settings.ReplyToAddress = replyTo;
        settings.DailySendCeiling = input.DailySendCeiling;
        settings.IsDomainVerified = input.IsDomainVerified;
        settings.VerifiedAtUtc = input.IsDomainVerified ? clock.UtcNow : null;
        settings.VerifiedBy = input.IsDomainVerified ? actor : null;
        AddAudit(db, "EmailSenderSettingsSaved", "Accepted", actor, null, null, null);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<EmailTemplateSummary>> GetTemplatesAsync(CancellationToken cancellationToken = default) =>
        await db.EmailTemplates.AsNoTracking().OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => new EmailTemplateSummary(x.Id, x.Type, x.VersionNumber, x.Subject, x.State, x.Version)).ToListAsync(cancellationToken);

    public async Task CreateTemplateAsync(EmailTemplateInput input, CancellationToken cancellationToken = default)
    {
        var actor = await authorization.RequireAsync("OrganizerOperator", cancellationToken);
        ValidateTemplate(input);
        var nextVersion = (await db.EmailTemplates.Where(x => x.Type == input.Type).Select(x => (int?)x.VersionNumber).MaxAsync(cancellationToken) ?? 0) + 1;
        var template = new EmailTemplate { Type = input.Type, VersionNumber = nextVersion, Subject = input.Subject.Trim(), HtmlBody = input.HtmlBody, PlainTextBody = input.PlainTextBody, State = EmailTemplateState.Draft, ContentDigest = Digest(input), CreatedAtUtc = clock.UtcNow, CreatedBy = actor };
        db.EmailTemplates.Add(template);
        AddAudit(db, "EmailTemplateCreated", "Accepted", actor, null, null, null);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task ApproveTemplateAsync(Guid templateId, uint expectedVersion, CancellationToken cancellationToken = default)
    {
        var actor = await authorization.RequireAsync("OrganizerOperator", cancellationToken);
        var template = await db.EmailTemplates.SingleAsync(x => x.Id == templateId, cancellationToken);
        if (template.Version != expectedVersion) throw new StaleDataException("This template changed after you opened it. The current values have been loaded.");
        if (template.State != EmailTemplateState.Draft) throw new InvalidOperationException("Only draft templates may be approved.");
        template.State = EmailTemplateState.Approved;
        template.ApprovedAtUtc = clock.UtcNow;
        template.ApprovedBy = actor;
        AddAudit(db, "EmailTemplateApproved", "Accepted", actor, null, null, null);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<EmailCampaignSummary>> GetCampaignsAsync(CancellationToken cancellationToken = default) =>
        await (from campaign in db.EmailCampaigns.AsNoTracking().OrderByDescending(x => x.CreatedAtUtc)
               join batch in db.InvitationBatches.AsNoTracking() on campaign.BatchId equals batch.Id
               select new EmailCampaignSummary(campaign.Id, campaign.Type, campaign.State, batch.Name, campaign.TemplateVersionNumber, campaign.CreatedAtUtc,
                   db.EmailDispatches.Count(x => x.CampaignId == campaign.Id),
                   db.EmailDispatches.Count(x => x.CampaignId == campaign.Id && x.State == EmailDispatchState.Accepted),
                   db.EmailDispatches.Count(x => x.CampaignId == campaign.Id && x.State == EmailDispatchState.Failed), campaign.Version))
            .ToListAsync(cancellationToken);

    public async Task<EmailCampaignSummary> PrepareInitialCampaignAsync(Guid batchId, Guid templateId, CancellationToken cancellationToken = default)
    {
        var actor = await authorization.RequireAsync("OrganizerOperator", cancellationToken);
        return await retry.ExecuteAsync(async token =>
        {
            await using var operationDb = await dbFactory.CreateDbContextAsync(token);
            await using var transaction = operationDb.Database.IsRelational() ? await operationDb.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, token) : null;
            var sender = await operationDb.EmailSenderConfigurations.SingleOrDefaultAsync(token) ?? throw new InvalidOperationException("Configure email sender settings before preparing a campaign.");
            if (!sender.IsDomainVerified) throw new InvalidOperationException("Verify the sender domain before preparing a campaign.");
            var batch = await operationDb.InvitationBatches.SingleAsync(x => x.Id == batchId, token);
            if (batch.State != InvitationBatchState.Committed || batch.DeadlineUtc <= clock.UtcNow) throw new InvalidOperationException("Choose a committed batch with a future deadline.");
            var template = await operationDb.EmailTemplates.SingleAsync(x => x.Id == templateId, token);
            if (template.Type != EmailTemplateType.InitialInvitation || template.State != EmailTemplateState.Approved) throw new InvalidOperationException("Choose an approved initial invitation template.");
            var recipients = await (from party in operationDb.InvitationParties
                                    join rsvpToken in operationDb.RsvpTokens on party.Id equals rsvpToken.PartyId
                                     where party.BatchId == batchId && party.Status == InvitationStatus.Pending && rsvpToken.RevokedAtUtc == null && rsvpToken.RawToken != null
                                    select new { party, rsvpToken }).ToListAsync(token);
            if (recipients.Count == 0) throw new InvalidOperationException("The selected batch has no eligible invitation recipients.");
            var campaign = new EmailCampaign { Type = EmailCampaignType.InitialInvitation, State = EmailCampaignState.ReadyForReview, BatchId = batch.Id, TemplateId = template.Id, TemplateVersionNumber = template.VersionNumber, TemplateDigest = template.ContentDigest, FromDisplayName = sender.FromDisplayName, FromAddress = sender.FromAddress, ReplyToAddress = sender.ReplyToAddress, CreatedAtUtc = clock.UtcNow, CreatedBy = actor, QueuedAtUtc = default };
            operationDb.EmailCampaigns.Add(campaign);
            foreach (var recipient in recipients)
            {
                operationDb.EmailDispatches.Add(new EmailDispatch { CampaignId = campaign.Id, PartyId = recipient.party.Id, TokenId = recipient.rsvpToken.Id, RecipientEmail = recipient.party.Email, RecipientName = recipient.party.PrimaryGuestName, AllocatedSeats = recipient.party.AllocatedSeats, DeadlineUtc = batch.DeadlineUtc, IdempotencyKey = $"initial/{campaign.Id:N}/{recipient.party.Id:N}", State = EmailDispatchState.Queued });
            }
            AddAudit(operationDb, "EmailCampaignPrepared", "Accepted", actor, batch.Id, campaign.Id, null);
            await operationDb.SaveChangesAsync(token);
            if (transaction is not null) await transaction.CommitAsync(token);
            return new EmailCampaignSummary(campaign.Id, campaign.Type, campaign.State, batch.Name, campaign.TemplateVersionNumber, campaign.CreatedAtUtc, recipients.Count, 0, 0, campaign.Version);
        }, cancellationToken);
    }

    public async Task<EmailCampaignSummary> PrepareReminderCampaignAsync(Guid batchId, Guid templateId, CancellationToken cancellationToken = default)
    {
        var actor = await authorization.RequireAsync("OrganizerOperator", cancellationToken);
        return await PrepareCampaignAsync(batchId, templateId, EmailCampaignType.Reminder, actor, cancellationToken);
    }

    public async Task<EmailCampaignSummary> PrepareResendCampaignAsync(Guid partyId, Guid templateId, CancellationToken cancellationToken = default)
    {
        var actor = await authorization.RequireAsync("OrganizerOperator", cancellationToken);
        var party = await db.InvitationParties.SingleAsync(x => x.Id == partyId, cancellationToken);
        return await PrepareCampaignAsync(party.BatchId, templateId, EmailCampaignType.Resend, actor, cancellationToken, new[] { partyId });
    }

    public async Task SendTestAsync(Guid templateId, string recipientEmail, CancellationToken cancellationToken = default)
    {
        await authorization.RequireAsync("OrganizerOperator", cancellationToken);
        var recipient = PartyEmailValidation.Normalize(recipientEmail);
        var sender = await db.EmailSenderConfigurations.SingleAsync(cancellationToken);
        if (!sender.IsDomainVerified) throw new InvalidOperationException("Verify the sender domain before sending a test email.");
        var baseUrl = configuration.GetSection("PublicApp").Get<PublicAppOptions>()?.BaseUrl;
        if (string.IsNullOrWhiteSpace(baseUrl)) throw new InvalidOperationException("Configure the public application base URL before sending a test email.");
        var template = await db.EmailTemplates.SingleAsync(x => x.Id == templateId && x.State == EmailTemplateState.Approved, cancellationToken);
        var eventConfiguration = await db.EventConfigurations.SingleAsync(cancellationToken);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(eventConfiguration.TimeZoneId);
        var rendered = renderer.Render(template.Subject, template.HtmlBody, template.PlainTextBody, new EmailRenderData("Test Guest", "2 seats for you and your guest", eventConfiguration.EventName, TimeZoneInfo.ConvertTime(eventConfiguration.StartsAtUtc, zone).ToString("D"), TimeZoneInfo.ConvertTime(eventConfiguration.DoorsAtUtc, zone).ToString("t"), TimeZoneInfo.ConvertTime(eventConfiguration.StartsAtUtc, zone).ToString("t"), eventConfiguration.VenueName, eventConfiguration.VenueAddress, "Test deadline", $"{baseUrl.TrimEnd('/')}/rsvp/test-link", eventConfiguration.SupportEmail));
        var result = await emailProvider.SendAsync(new EmailProviderMessage($"{sender.FromDisplayName} <{sender.FromAddress}>", sender.ReplyToAddress, recipient, rendered.Subject, rendered.HtmlBody, rendered.PlainTextBody, $"test/{templateId:N}/{Guid.NewGuid():N}"), cancellationToken);
        if (!result.IsAccepted) throw new InvalidOperationException("The test email was not accepted by the provider.");
    }

    private async Task<EmailCampaignSummary> PrepareCampaignAsync(Guid batchId, Guid templateId, EmailCampaignType type, string actor, CancellationToken cancellationToken, IReadOnlyCollection<Guid>? explicitPartyIds = null)
    {
        return await retry.ExecuteAsync(async token =>
        {
            await using var operationDb = await dbFactory.CreateDbContextAsync(token);
            await using var transaction = operationDb.Database.IsRelational() ? await operationDb.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, token) : null;
            var sender = await operationDb.EmailSenderConfigurations.SingleOrDefaultAsync(token) ?? throw new InvalidOperationException("Configure email sender settings before preparing a campaign.");
            if (!sender.IsDomainVerified) throw new InvalidOperationException("Verify the sender domain before preparing a campaign.");
            var batch = await operationDb.InvitationBatches.SingleAsync(x => x.Id == batchId, token);
            if (batch.State != InvitationBatchState.Committed || batch.DeadlineUtc <= clock.UtcNow) throw new InvalidOperationException("Choose a committed batch with a future deadline.");
            var template = await operationDb.EmailTemplates.SingleAsync(x => x.Id == templateId, token);
            var requiredTemplateType = type == EmailCampaignType.Reminder ? EmailTemplateType.Reminder : EmailTemplateType.InitialInvitation;
            if (template.Type != requiredTemplateType || template.State != EmailTemplateState.Approved) throw new InvalidOperationException("Choose an approved template for this campaign type.");
            var recipients = await (from party in operationDb.InvitationParties
                                    join rsvpToken in operationDb.RsvpTokens on party.Id equals rsvpToken.PartyId
                                    where party.BatchId == batchId && party.Status == InvitationStatus.Pending && rsvpToken.RevokedAtUtc == null && rsvpToken.RawToken != null
                                    select new { party, rsvpToken }).ToListAsync(token);
            if (explicitPartyIds is not null) recipients = recipients.Where(x => explicitPartyIds.Contains(x.party.Id)).ToList();
            if (type == EmailCampaignType.Reminder)
            {
                var remindedParties = await (from dispatch in operationDb.EmailDispatches
                                             join previousCampaign in operationDb.EmailCampaigns on dispatch.CampaignId equals previousCampaign.Id
                                             where previousCampaign.Type == EmailCampaignType.Reminder
                                             select dispatch.PartyId).ToListAsync(token);
                recipients = recipients.Where(x => !remindedParties.Contains(x.party.Id)).ToList();
            }
            if (recipients.Count == 0) throw new InvalidOperationException("The selected audience has no eligible recipients with an active RSVP token. Commit a new batch or regenerate the party's RSVP link before preparing email.");
            var campaign = new EmailCampaign { Type = type, State = EmailCampaignState.ReadyForReview, BatchId = batch.Id, TemplateId = template.Id, TemplateVersionNumber = template.VersionNumber, TemplateDigest = template.ContentDigest, FromDisplayName = sender.FromDisplayName, FromAddress = sender.FromAddress, ReplyToAddress = sender.ReplyToAddress, CreatedAtUtc = clock.UtcNow, CreatedBy = actor, QueuedAtUtc = default };
            operationDb.EmailCampaigns.Add(campaign);
            foreach (var recipient in recipients) operationDb.EmailDispatches.Add(new EmailDispatch { CampaignId = campaign.Id, PartyId = recipient.party.Id, TokenId = recipient.rsvpToken.Id, RecipientEmail = recipient.party.Email, RecipientName = recipient.party.PrimaryGuestName, AllocatedSeats = recipient.party.AllocatedSeats, DeadlineUtc = batch.DeadlineUtc, IdempotencyKey = $"{type.ToString().ToLowerInvariant()}/{campaign.Id:N}/{recipient.party.Id:N}", State = EmailDispatchState.Queued });
            AddAudit(operationDb, "EmailCampaignPrepared", "Accepted", actor, batch.Id, campaign.Id, null);
            await operationDb.SaveChangesAsync(token);
            if (transaction is not null) await transaction.CommitAsync(token);
            return new EmailCampaignSummary(campaign.Id, campaign.Type, campaign.State, batch.Name, campaign.TemplateVersionNumber, campaign.CreatedAtUtc, recipients.Count, 0, 0, campaign.Version);
        }, cancellationToken);
    }

    public async Task<EmailCampaignDetail?> GetCampaignAsync(Guid campaignId, CancellationToken cancellationToken = default)
    {
        var campaign = await (from item in db.EmailCampaigns.AsNoTracking()
                              join batch in db.InvitationBatches.AsNoTracking() on item.BatchId equals batch.Id
                              join template in db.EmailTemplates.AsNoTracking() on item.TemplateId equals template.Id
                              join eventConfiguration in db.EventConfigurations.AsNoTracking() on 1 equals 1
                              where item.Id == campaignId
                              select new { item, batch, template, eventConfiguration }).SingleOrDefaultAsync(cancellationToken);
        if (campaign is null) return null;
        var dispatches = await db.EmailDispatches.AsNoTracking().Where(x => x.CampaignId == campaignId).OrderBy(x => x.RecipientName)
            .Select(x => new EmailDispatchSummary(x.Id, x.RecipientName, x.RecipientEmail, x.AllocatedSeats, x.State, x.AttemptCount, x.FailureCategory, x.ProviderMessageId)).ToListAsync(cancellationToken);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(campaign.eventConfiguration.TimeZoneId);
        var sample = renderer.Render(campaign.template.Subject, campaign.template.HtmlBody, campaign.template.PlainTextBody, new EmailRenderData("Žaneta Guest", "2 seats for you and your guest", campaign.eventConfiguration.EventName, TimeZoneInfo.ConvertTime(campaign.eventConfiguration.StartsAtUtc, zone).ToString("D"), TimeZoneInfo.ConvertTime(campaign.eventConfiguration.DoorsAtUtc, zone).ToString("t"), TimeZoneInfo.ConvertTime(campaign.eventConfiguration.StartsAtUtc, zone).ToString("t"), campaign.eventConfiguration.VenueName, campaign.eventConfiguration.VenueAddress, TimeZoneInfo.ConvertTime(campaign.batch.DeadlineUtc, zone).ToString("f") + $" ({campaign.eventConfiguration.TimeZoneId})", "[private RSVP link]", campaign.eventConfiguration.SupportEmail));
        return new EmailCampaignDetail(campaign.item.Id, campaign.item.Type, campaign.item.State, campaign.item.Version, campaign.batch.Name, campaign.template.VersionNumber, campaign.item.FromDisplayName, campaign.item.FromAddress, campaign.item.ReplyToAddress, dispatches, sample);
    }

    public async Task ConfirmCampaignAsync(Guid campaignId, uint expectedVersion, CancellationToken cancellationToken = default)
    {
        var actor = await authorization.RequireAsync("OrganizerOperator", cancellationToken);
        var campaign = await db.EmailCampaigns.SingleAsync(x => x.Id == campaignId, cancellationToken);
        if (campaign.Version != expectedVersion) throw new StaleDataException("This campaign changed after you opened it. The current campaign has been loaded.");
        if (campaign.State != EmailCampaignState.ReadyForReview) throw new InvalidOperationException("Only a review-ready campaign can be queued.");
        campaign.State = EmailCampaignState.Queued;
        campaign.QueuedAtUtc = clock.UtcNow;
        AddAudit(db, "EmailCampaignQueued", "Accepted", actor, campaign.BatchId, campaign.Id, null);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task SendCampaignAsync(Guid campaignId, uint expectedVersion, CancellationToken cancellationToken = default)
    {
        var actor = await authorization.RequireAsync("OrganizerOperator", cancellationToken);
        var campaign = await db.EmailCampaigns.SingleAsync(x => x.Id == campaignId, cancellationToken);
        if (campaign.Version != expectedVersion) throw new StaleDataException("This campaign changed after you opened it. The current campaign has been loaded.");
        if (campaign.State is not (EmailCampaignState.Queued or EmailCampaignState.PartiallyFailed or EmailCampaignState.Failed)) throw new InvalidOperationException("Only queued or failed campaigns can be sent.");
        var baseUrl = configuration.GetSection("PublicApp").Get<PublicAppOptions>()?.BaseUrl;
        if (string.IsNullOrWhiteSpace(baseUrl)) throw new InvalidOperationException("Configure the public application base URL before sending a campaign.");
        var sender = await db.EmailSenderConfigurations.SingleAsync(cancellationToken);
        if (!sender.IsDomainVerified) throw new InvalidOperationException("Verify the sender domain before sending a campaign.");
        var sentToday = await db.EmailDispatches.CountAsync(x => x.AcceptedAtUtc != null && x.AcceptedAtUtc.Value.UtcDateTime.Date == clock.UtcNow.UtcDateTime.Date, cancellationToken);
        if (sentToday >= sender.DailySendCeiling) throw new InvalidOperationException("The configured daily send ceiling has been reached.");
        try
        {
            campaign.State = EmailCampaignState.Sending;
            await db.SaveChangesAsync(cancellationToken);

            var dispatches = await db.EmailDispatches.Where(x => x.CampaignId == campaignId && (x.State == EmailDispatchState.Queued || x.State == EmailDispatchState.Failed)).OrderBy(x => x.RecipientName).ToListAsync(cancellationToken);
            foreach (var dispatch in dispatches)
            {
                if (sentToday >= sender.DailySendCeiling) break;
                var token = await db.RsvpTokens.SingleOrDefaultAsync(x => x.Id == dispatch.TokenId && x.RevokedAtUtc == null, cancellationToken);
                if (token?.RawToken is null)
                {
                    dispatch.State = EmailDispatchState.Failed;
                    dispatch.FailureCategory = "token-unavailable";
                    await db.SaveChangesAsync(cancellationToken);
                    continue;
                }
                var template = await db.EmailTemplates.SingleAsync(x => x.Id == campaign.TemplateId, cancellationToken);
                var eventConfiguration = await db.EventConfigurations.SingleAsync(cancellationToken);
                var zone = TimeZoneInfo.FindSystemTimeZoneById(eventConfiguration.TimeZoneId);
                var rsvpUrl = $"{baseUrl.TrimEnd('/')}/rsvp/{token.RawToken}";
                var rendered = renderer.Render(template.Subject, template.HtmlBody, template.PlainTextBody, new EmailRenderData(dispatch.RecipientName, dispatch.AllocatedSeats == 1 ? "1 seat" : $"{dispatch.AllocatedSeats} seats for you and your guest", eventConfiguration.EventName, TimeZoneInfo.ConvertTime(eventConfiguration.StartsAtUtc, zone).ToString("D"), TimeZoneInfo.ConvertTime(eventConfiguration.DoorsAtUtc, zone).ToString("t"), TimeZoneInfo.ConvertTime(eventConfiguration.StartsAtUtc, zone).ToString("t"), eventConfiguration.VenueName, eventConfiguration.VenueAddress, TimeZoneInfo.ConvertTime(dispatch.DeadlineUtc, zone).ToString("f") + $" ({eventConfiguration.TimeZoneId})", rsvpUrl, eventConfiguration.SupportEmail));
                dispatch.AttemptCount++;
                var result = await emailProvider.SendAsync(new EmailProviderMessage($"{campaign.FromDisplayName} <{campaign.FromAddress}>", campaign.ReplyToAddress, dispatch.RecipientEmail, rendered.Subject, rendered.HtmlBody, rendered.PlainTextBody, dispatch.IdempotencyKey), cancellationToken);
                if (result.IsAccepted)
                {
                    dispatch.State = EmailDispatchState.Accepted;
                    dispatch.AcceptedAtUtc = clock.UtcNow;
                    dispatch.ProviderMessageId = result.ProviderMessageId;
                    dispatch.FailureCategory = null;
                    sentToday++;
                }
                else
                {
                    dispatch.State = EmailDispatchState.Failed;
                    dispatch.FailureCategory = result.FailureCategory ?? (result.IsTransientFailure ? "provider-transient-failure" : "provider-rejected");
                }
                await db.SaveChangesAsync(cancellationToken);
            }
            var states = await db.EmailDispatches.Where(x => x.CampaignId == campaignId).Select(x => x.State).ToListAsync(cancellationToken);
            campaign.State = states.All(x => x == EmailDispatchState.Accepted) ? EmailCampaignState.Completed : states.Any(x => x == EmailDispatchState.Accepted) ? EmailCampaignState.PartiallyFailed : EmailCampaignState.Failed;
            AddAudit(db, "EmailCampaignSent", "Accepted", actor, campaign.BatchId, campaign.Id, null);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            campaign.State = EmailCampaignState.Failed;
            await db.SaveChangesAsync(cancellationToken);
            throw;
        }
    }

    private static void ValidateTemplate(EmailTemplateInput input)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(input.Subject);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.HtmlBody);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.PlainTextBody);
        if (input.Subject.Length > 300) throw new ArgumentException("Email subject must be 300 characters or fewer.");
        EmailTemplateRenderer.Validate(input.Subject, input.HtmlBody, input.PlainTextBody);
    }

    private static string Digest(EmailTemplateInput input) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{input.Type}\n{input.Subject}\n{input.HtmlBody}\n{input.PlainTextBody}")));
    private void AddAudit(InvitationDbContext context, string type, string outcome, string actor, Guid? batchId, Guid? campaignId, Guid? dispatchId) => context.AuditEvents.Add(new AuditEvent { OccurredAtUtc = clock.UtcNow, EventType = type, Outcome = outcome, ActorCategory = "Organizer", ActorIdentifier = actor, BatchId = batchId, EmailCampaignId = campaignId, EmailDispatchId = dispatchId, CorrelationId = Guid.NewGuid().ToString("N") });
}

public sealed record EmailSenderSettings(string FromDisplayName, string FromAddress, string ReplyToAddress, int DailySendCeiling, bool IsDomainVerified, uint Version);
public sealed record EmailSenderSettingsInput(string FromDisplayName, string FromAddress, string ReplyToAddress, int DailySendCeiling, bool IsDomainVerified);
public sealed record EmailTemplateInput(EmailTemplateType Type, string Subject, string HtmlBody, string PlainTextBody);
public sealed record EmailTemplateSummary(Guid Id, EmailTemplateType Type, int VersionNumber, string Subject, EmailTemplateState State, uint Version);
public sealed record EmailCampaignSummary(Guid Id, EmailCampaignType Type, EmailCampaignState State, string BatchName, int TemplateVersionNumber, DateTimeOffset CreatedAtUtc, int RecipientCount, int AcceptedCount, int FailedCount, uint Version);
public sealed record EmailDispatchSummary(Guid Id, string RecipientName, string RecipientEmail, int AllocatedSeats, EmailDispatchState State, int AttemptCount, string? FailureCategory, string? ProviderMessageId);
public sealed record EmailCampaignDetail(Guid Id, EmailCampaignType Type, EmailCampaignState State, uint Version, string BatchName, int TemplateVersionNumber, string FromDisplayName, string FromAddress, string ReplyToAddress, IReadOnlyList<EmailDispatchSummary> Dispatches, EmailRenderResult Preview);
