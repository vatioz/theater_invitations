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
        if (input.FromDisplayName.Trim().Length > 200 || input.DailySendCeiling <= 0) throw new ArgumentException("Zadejte jméno odesílatele dlouhé nejvýše 200 znaků a kladný denní limit odesílání.");
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
            throw new StaleDataException("Nastavení odesílatele e-mailů se po otevření změnilo. Byly načteny aktuální hodnoty.");
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
            .Select(x => new EmailTemplateSummary(x.Id, x.Type, x.VersionNumber, x.Name, x.Subject, x.State, x.Version)).ToListAsync(cancellationToken);

    public async Task CreateTemplateAsync(EmailTemplateInput input, CancellationToken cancellationToken = default)
    {
        var actor = await authorization.RequireAsync("OrganizerOperator", cancellationToken);
        ValidateTemplate(input);
        var nextVersion = (await db.EmailTemplates.Where(x => x.Type == input.Type).Select(x => (int?)x.VersionNumber).MaxAsync(cancellationToken) ?? 0) + 1;
        var template = new EmailTemplate { Type = input.Type, VersionNumber = nextVersion, Name = input.Name.Trim(), Subject = input.Subject.Trim(), HtmlBody = input.HtmlBody, PlainTextBody = input.PlainTextBody, State = EmailTemplateState.Active, ContentDigest = Digest(input), CreatedAtUtc = clock.UtcNow, CreatedBy = actor };
        db.EmailTemplates.Add(template);
        AddAudit(db, "EmailTemplateCreated", "Accepted", actor, null, null, null);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<EmailCampaignSummary>> GetCampaignsAsync(CancellationToken cancellationToken = default) =>
         await (from campaign in db.EmailCampaigns.AsNoTracking().OrderByDescending(x => x.CreatedAtUtc)
                join batch in db.InvitationBatches.AsNoTracking() on campaign.BatchId equals batch.Id
                join template in db.EmailTemplates.AsNoTracking() on campaign.TemplateId equals template.Id
                select new EmailCampaignSummary(campaign.Id, campaign.Type, campaign.State, batch.Name, template.Name, campaign.TemplateVersionNumber, campaign.CreatedAtUtc,
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
            var sender = await operationDb.EmailSenderConfigurations.SingleOrDefaultAsync(token) ?? throw new InvalidOperationException("Před přípravou kampaně nastavte odesílatele e-mailů.");
            if (!sender.IsDomainVerified) throw new InvalidOperationException("Před přípravou kampaně ověřte doménu odesílatele.");
            var batch = await operationDb.InvitationBatches.SingleAsync(x => x.Id == batchId, token);
            if (batch.State != InvitationBatchState.Committed || batch.DeadlineUtc <= clock.UtcNow) throw new InvalidOperationException("Vyberte potvrzenou dávku s termínem v budoucnosti.");
            var template = await operationDb.EmailTemplates.SingleAsync(x => x.Id == templateId, token);
            if (template.Type != EmailTemplateType.InitialInvitation || template.State != EmailTemplateState.Active) throw new InvalidOperationException("Vyberte aktivní šablonu první pozvánky.");
            var recipients = await (from party in operationDb.InvitationParties
                                    join rsvpToken in operationDb.RsvpTokens on party.Id equals rsvpToken.PartyId
                                     where party.BatchId == batchId && party.Status == InvitationStatus.Pending && rsvpToken.RevokedAtUtc == null && rsvpToken.RawToken != null
                                    select new { party, rsvpToken }).ToListAsync(token);
            if (recipients.Count == 0) throw new InvalidOperationException("Vybraná dávka nemá žádné příjemce způsobilé pro pozvánku.");
            var campaign = new EmailCampaign { Type = EmailCampaignType.InitialInvitation, State = EmailCampaignState.ReadyForReview, BatchId = batch.Id, TemplateId = template.Id, TemplateVersionNumber = template.VersionNumber, TemplateDigest = template.ContentDigest, FromDisplayName = sender.FromDisplayName, FromAddress = sender.FromAddress, ReplyToAddress = sender.ReplyToAddress, CreatedAtUtc = clock.UtcNow, CreatedBy = actor, QueuedAtUtc = default };
            operationDb.EmailCampaigns.Add(campaign);
            foreach (var recipient in recipients)
            {
                operationDb.EmailDispatches.Add(new EmailDispatch { CampaignId = campaign.Id, PartyId = recipient.party.Id, TokenId = recipient.rsvpToken.Id, RecipientEmail = recipient.party.Email, RecipientName = recipient.party.PrimaryGuestName, AllocatedSeats = recipient.party.AllocatedSeats, DeadlineUtc = batch.DeadlineUtc, IdempotencyKey = $"initial/{campaign.Id:N}/{recipient.party.Id:N}", State = EmailDispatchState.Queued });
            }
            var eventConfiguration = await operationDb.EventConfigurations.SingleAsync(token);
            campaign.ReviewFingerprint = BuildReviewFingerprint(campaign, batch, eventConfiguration, sender, template,
                recipients.Select(x => new ReviewRecipient(x.party.Id, x.party.PrimaryGuestName, x.party.Email, x.party.AllocatedSeats, x.party.Status, x.rsvpToken.Id, x.rsvpToken.RevokedAtUtc is null && x.rsvpToken.RawToken is not null)));
            AddAudit(operationDb, "EmailCampaignPrepared", "Accepted", actor, batch.Id, campaign.Id, null);
            await operationDb.SaveChangesAsync(token);
            if (transaction is not null) await transaction.CommitAsync(token);
            return new EmailCampaignSummary(campaign.Id, campaign.Type, campaign.State, batch.Name, template.Name, campaign.TemplateVersionNumber, campaign.CreatedAtUtc, recipients.Count, 0, 0, campaign.Version);
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
        var source = await (from dispatch in db.EmailDispatches.AsNoTracking()
                            join campaign in db.EmailCampaigns.AsNoTracking() on dispatch.CampaignId equals campaign.Id
                            where dispatch.PartyId == partyId
                            orderby campaign.CreatedAtUtc descending
                            select new { campaign.Id, DispatchId = dispatch.Id }).FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("Pro tuto skupinu nebyla nalezena předchozí e-mailová kampaň.");
        return await PrepareSelectedResendAsync(source.Id, new[] { source.DispatchId }, actor, templateId, cancellationToken);
    }

    public async Task<EmailCampaignSummary> PrepareSelectedResendCampaignAsync(Guid sourceCampaignId, IReadOnlyCollection<Guid> selectedDispatchIds, Guid? templateId = null, CancellationToken cancellationToken = default)
    {
        var actor = await authorization.RequireAsync("OrganizerOperator", cancellationToken);
        return await PrepareSelectedResendAsync(sourceCampaignId, selectedDispatchIds, actor, templateId, cancellationToken);
    }

    private async Task<EmailCampaignSummary> PrepareSelectedResendAsync(Guid sourceCampaignId, IReadOnlyCollection<Guid> selectedDispatchIds, string actor, Guid? templateId, CancellationToken cancellationToken)
    {
        var selectedIds = selectedDispatchIds.Distinct().ToArray();
        if (selectedIds.Length == 0) throw new InvalidOperationException("Vyberte alespoň jednoho příjemce pro opětovné odeslání.");
        return await retry.ExecuteAsync(async token =>
        {
            await using var operationDb = await dbFactory.CreateDbContextAsync(token);
            await using var transaction = operationDb.Database.IsRelational() ? await operationDb.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, token) : null;
            var source = await operationDb.EmailCampaigns.SingleAsync(x => x.Id == sourceCampaignId, token);
            var batch = await operationDb.InvitationBatches.SingleAsync(x => x.Id == source.BatchId, token);
            var sender = await operationDb.EmailSenderConfigurations.SingleOrDefaultAsync(token) ?? throw new InvalidOperationException("Před přípravou kampaně nastavte odesílatele e-mailů.");
            if (!sender.IsDomainVerified) throw new InvalidOperationException("Před přípravou kampaně ověřte doménu odesílatele.");
            var effectiveTemplateId = templateId ?? source.TemplateId;
            var template = await operationDb.EmailTemplates.SingleAsync(x => x.Id == effectiveTemplateId, token);
            if (template.State != EmailTemplateState.Active) throw new InvalidOperationException("Vyberte aktivní šablonu pro opětovné odeslání.");
            var eventConfiguration = await operationDb.EventConfigurations.SingleAsync(token);

            var sourceDispatches = await operationDb.EmailDispatches.Where(x => x.CampaignId == sourceCampaignId && selectedIds.Contains(x.Id)).ToListAsync(token);
            if (sourceDispatches.Count != selectedIds.Length) throw new InvalidOperationException("Jeden nebo více vybraných příjemců nepatří do této kampaně.");
            var partyIds = sourceDispatches.Select(x => x.PartyId).Distinct().ToArray();
            var parties = await operationDb.InvitationParties.Where(x => partyIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, token);
            var activeTokens = await operationDb.RsvpTokens.Where(x => partyIds.Contains(x.PartyId) && x.RevokedAtUtc == null).ToDictionaryAsync(x => x.PartyId, token);
            var normalizedEmails = parties.Values.Select(x => NormalizeEmailOrNull(x.Email)).Where(x => x is not null).Cast<string>().ToArray();
            var suppressedEmails = (await operationDb.EmailSuppressions.Where(x => normalizedEmails.Contains(x.NormalizedEmail)).Select(x => x.NormalizedEmail).ToListAsync(token)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var historicallySuppressedParties = (await operationDb.EmailDispatches.Where(x => partyIds.Contains(x.PartyId) && (x.State == EmailDispatchState.Bounced || x.State == EmailDispatchState.Complained || x.State == EmailDispatchState.Suppressed)).Select(x => x.PartyId).Distinct().ToListAsync(token)).ToHashSet();

            var recipients = new List<ReviewRecipient>();
            var skips = new List<(Guid PartyId, string Reason)>();
            var seenParties = new HashSet<Guid>();
            foreach (var sourceDispatch in sourceDispatches.OrderBy(x => x.Id))
            {
                if (!seenParties.Add(sourceDispatch.PartyId))
                {
                    skips.Add((sourceDispatch.PartyId, "duplicate-selection"));
                    continue;
                }
                if (!parties.TryGetValue(sourceDispatch.PartyId, out var party))
                {
                    skips.Add((sourceDispatch.PartyId, "party-unavailable"));
                    continue;
                }
                var email = NormalizeEmailOrNull(party.Email);
                var reason = party.BatchId != batch.Id ? "foreign-party"
                    : eventConfiguration.IsRsvpLocked ? "globally-locked"
                    : batch.DeadlineUtc <= clock.UtcNow ? "deadline-expired"
                    : party.Status == InvitationStatus.Expired ? "status-ineligible"
                    : template.Type == EmailTemplateType.Reminder && party.Status != InvitationStatus.Pending ? "status-ineligible"
                    : email is null ? "invalid-address"
                    : suppressedEmails.Contains(email) || historicallySuppressedParties.Contains(party.Id) ? "suppressed"
                    : !activeTokens.TryGetValue(party.Id, out var activeToken) || activeToken.RawToken is null ? "token-unavailable"
                    : null;
                if (reason is not null)
                {
                    skips.Add((party.Id, reason));
                    continue;
                }
                var tokenEntity = activeTokens[party.Id];
                recipients.Add(new ReviewRecipient(party.Id, party.PrimaryGuestName, email!, party.AllocatedSeats, party.Status, tokenEntity.Id, true));
            }

            var campaign = new EmailCampaign { Type = EmailCampaignType.Resend, State = EmailCampaignState.ReadyForReview, BatchId = batch.Id, SourceCampaignId = sourceCampaignId, TemplateId = template.Id, TemplateVersionNumber = template.VersionNumber, TemplateDigest = template.ContentDigest, FromDisplayName = sender.FromDisplayName, FromAddress = sender.FromAddress, ReplyToAddress = sender.ReplyToAddress, CreatedAtUtc = clock.UtcNow, CreatedBy = actor, QueuedAtUtc = default };
            operationDb.EmailCampaigns.Add(campaign);
            foreach (var recipient in recipients)
            {
                operationDb.EmailDispatches.Add(new EmailDispatch { CampaignId = campaign.Id, PartyId = recipient.PartyId, TokenId = recipient.TokenId, RecipientEmail = recipient.Email, RecipientName = recipient.Name, AllocatedSeats = recipient.AllocatedSeats, DeadlineUtc = batch.DeadlineUtc, IdempotencyKey = $"resend/{campaign.Id:N}/{recipient.PartyId:N}", State = EmailDispatchState.Queued });
            }
            foreach (var skip in skips)
            {
                operationDb.EmailCampaignSkips.Add(new EmailCampaignSkip { CampaignId = campaign.Id, PartyId = skip.PartyId, ReasonCategory = skip.Reason, CreatedAtUtc = clock.UtcNow });
            }
            campaign.ReviewFingerprint = BuildReviewFingerprint(campaign, batch, eventConfiguration, sender, template, recipients);
            AddAudit(operationDb, "EmailCampaignResendPrepared", "Accepted", actor, batch.Id, campaign.Id, null);
            await operationDb.SaveChangesAsync(token);
            if (transaction is not null) await transaction.CommitAsync(token);
            return new EmailCampaignSummary(campaign.Id, campaign.Type, campaign.State, batch.Name, template.Name, campaign.TemplateVersionNumber, campaign.CreatedAtUtc, recipients.Count, 0, 0, campaign.Version);
        }, cancellationToken);
    }

    public async Task SendTestAsync(Guid templateId, string recipientEmail, CancellationToken cancellationToken = default)
    {
        await authorization.RequireAsync("OrganizerOperator", cancellationToken);
        var recipient = PartyEmailValidation.Normalize(recipientEmail);
        var sender = await db.EmailSenderConfigurations.SingleAsync(cancellationToken);
        if (!sender.IsDomainVerified) throw new InvalidOperationException("Před odesláním zkušebního e-mailu ověřte doménu odesílatele.");
        var baseUrl = configuration.GetSection("PublicApp").Get<PublicAppOptions>()?.BaseUrl;
        if (string.IsNullOrWhiteSpace(baseUrl)) throw new InvalidOperationException("Před odesláním zkušebního e-mailu nastavte základní URL veřejné aplikace.");
        var template = await db.EmailTemplates.SingleAsync(x => x.Id == templateId && x.State == EmailTemplateState.Active, cancellationToken);
        var eventConfiguration = await db.EventConfigurations.SingleAsync(cancellationToken);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(eventConfiguration.TimeZoneId);
        var rendered = renderer.Render(template.Subject, template.HtmlBody, template.PlainTextBody, new EmailRenderData("Test Guest", "2 seats for you and your guest", eventConfiguration.EventName, TimeZoneInfo.ConvertTime(eventConfiguration.StartsAtUtc, zone).ToString("D"), TimeZoneInfo.ConvertTime(eventConfiguration.DoorsAtUtc, zone).ToString("t"), TimeZoneInfo.ConvertTime(eventConfiguration.StartsAtUtc, zone).ToString("t"), eventConfiguration.VenueName, eventConfiguration.VenueAddress, "Test deadline", $"{baseUrl.TrimEnd('/')}/rsvp/test-link", eventConfiguration.SupportEmail));
        var result = await emailProvider.SendAsync(new EmailProviderMessage($"{sender.FromDisplayName} <{sender.FromAddress}>", sender.ReplyToAddress, recipient, rendered.Subject, rendered.HtmlBody, rendered.PlainTextBody, $"test/{templateId:N}/{Guid.NewGuid():N}"), cancellationToken);
        if (!result.IsAccepted) throw new InvalidOperationException("Zkušební e-mail nebyl poskytovatelem přijat.");
    }

    private async Task<EmailCampaignSummary> PrepareCampaignAsync(Guid batchId, Guid templateId, EmailCampaignType type, string actor, CancellationToken cancellationToken, IReadOnlyCollection<Guid>? explicitPartyIds = null)
    {
        return await retry.ExecuteAsync(async token =>
        {
            await using var operationDb = await dbFactory.CreateDbContextAsync(token);
            await using var transaction = operationDb.Database.IsRelational() ? await operationDb.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, token) : null;
            var sender = await operationDb.EmailSenderConfigurations.SingleOrDefaultAsync(token) ?? throw new InvalidOperationException("Před přípravou kampaně nastavte odesílatele e-mailů.");
            if (!sender.IsDomainVerified) throw new InvalidOperationException("Před přípravou kampaně ověřte doménu odesílatele.");
            var batch = await operationDb.InvitationBatches.SingleAsync(x => x.Id == batchId, token);
            if (batch.State != InvitationBatchState.Committed || batch.DeadlineUtc <= clock.UtcNow) throw new InvalidOperationException("Vyberte potvrzenou dávku s termínem v budoucnosti.");
            var template = await operationDb.EmailTemplates.SingleAsync(x => x.Id == templateId, token);
            var requiredTemplateType = type == EmailCampaignType.Reminder ? EmailTemplateType.Reminder : EmailTemplateType.InitialInvitation;
            if (template.Type != requiredTemplateType || template.State != EmailTemplateState.Active) throw new InvalidOperationException("Vyberte aktivní šablonu pro tento typ kampaně.");
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
            if (recipients.Count == 0) throw new InvalidOperationException("Vybraný okruh nemá žádné způsobilé příjemce s aktivním tokenem RSVP. Před přípravou e-mailu potvrďte novou dávku nebo obnovte odkaz skupiny pro RSVP.");
            var campaign = new EmailCampaign { Type = type, State = EmailCampaignState.ReadyForReview, BatchId = batch.Id, TemplateId = template.Id, TemplateVersionNumber = template.VersionNumber, TemplateDigest = template.ContentDigest, FromDisplayName = sender.FromDisplayName, FromAddress = sender.FromAddress, ReplyToAddress = sender.ReplyToAddress, CreatedAtUtc = clock.UtcNow, CreatedBy = actor, QueuedAtUtc = default };
            operationDb.EmailCampaigns.Add(campaign);
            foreach (var recipient in recipients) operationDb.EmailDispatches.Add(new EmailDispatch { CampaignId = campaign.Id, PartyId = recipient.party.Id, TokenId = recipient.rsvpToken.Id, RecipientEmail = recipient.party.Email, RecipientName = recipient.party.PrimaryGuestName, AllocatedSeats = recipient.party.AllocatedSeats, DeadlineUtc = batch.DeadlineUtc, IdempotencyKey = $"{type.ToString().ToLowerInvariant()}/{campaign.Id:N}/{recipient.party.Id:N}", State = EmailDispatchState.Queued });
            var eventConfiguration = await operationDb.EventConfigurations.SingleAsync(token);
            campaign.ReviewFingerprint = BuildReviewFingerprint(campaign, batch, eventConfiguration, sender, template,
                recipients.Select(x => new ReviewRecipient(x.party.Id, x.party.PrimaryGuestName, x.party.Email, x.party.AllocatedSeats, x.party.Status, x.rsvpToken.Id, x.rsvpToken.RevokedAtUtc is null && x.rsvpToken.RawToken is not null)));
            AddAudit(operationDb, "EmailCampaignPrepared", "Accepted", actor, batch.Id, campaign.Id, null);
            await operationDb.SaveChangesAsync(token);
            if (transaction is not null) await transaction.CommitAsync(token);
            return new EmailCampaignSummary(campaign.Id, campaign.Type, campaign.State, batch.Name, template.Name, campaign.TemplateVersionNumber, campaign.CreatedAtUtc, recipients.Count, 0, 0, campaign.Version);
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
        var skips = await db.EmailCampaignSkips.AsNoTracking().Where(x => x.CampaignId == campaignId).OrderBy(x => x.ReasonCategory)
            .Select(x => new EmailCampaignSkipSummary(x.PartyId, x.ReasonCategory)).ToListAsync(cancellationToken);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(campaign.eventConfiguration.TimeZoneId);
        var previewRecipient = dispatches.FirstOrDefault();
        var previewGuestName = previewRecipient?.RecipientName ?? "Guest";
        var previewAllocation = previewRecipient is null
            ? "2 seats for you and your guest"
            : previewRecipient.AllocatedSeats == 1 ? "1 seat" : $"{previewRecipient.AllocatedSeats} seats for you and your guest";
        var sample = renderer.Render(campaign.template.Subject, campaign.template.HtmlBody, campaign.template.PlainTextBody, new EmailRenderData(previewGuestName, previewAllocation, campaign.eventConfiguration.EventName, TimeZoneInfo.ConvertTime(campaign.eventConfiguration.StartsAtUtc, zone).ToString("D"), TimeZoneInfo.ConvertTime(campaign.eventConfiguration.DoorsAtUtc, zone).ToString("t"), TimeZoneInfo.ConvertTime(campaign.eventConfiguration.StartsAtUtc, zone).ToString("t"), campaign.eventConfiguration.VenueName, campaign.eventConfiguration.VenueAddress, TimeZoneInfo.ConvertTime(campaign.batch.DeadlineUtc, zone).ToString("f") + $" ({campaign.eventConfiguration.TimeZoneId})", "[private RSVP link]", campaign.eventConfiguration.SupportEmail));
         return new EmailCampaignDetail(campaign.item.Id, campaign.item.Type, campaign.item.State, campaign.item.Version, campaign.batch.Name, campaign.template.Name, campaign.template.Type, campaign.template.VersionNumber, campaign.item.FromDisplayName, campaign.item.FromAddress, campaign.item.ReplyToAddress, campaign.item.PausedAtUtc, campaign.item.ContinueAfterUtc, dispatches, skips, sample);
    }

    public async Task SendCampaignAsync(Guid campaignId, uint expectedVersion, CancellationToken cancellationToken = default)
    {
        var actor = await authorization.RequireAsync("OrganizerOperator", cancellationToken);
        var campaign = await db.EmailCampaigns.SingleAsync(x => x.Id == campaignId, cancellationToken);
        if (campaign.Version != expectedVersion) throw new StaleDataException("Tato kampaň se po otevření změnila. Byla načtena aktuální kampaň.");
        if (campaign.State != EmailCampaignState.ReadyForReview) throw new InvalidOperationException("Odeslat lze pouze kampaň připravenou ke kontrole.");
        await EnsureReviewIsFreshAsync(campaign, actor, cancellationToken);
        var baseUrl = configuration.GetSection("PublicApp").Get<PublicAppOptions>()?.BaseUrl;
        if (string.IsNullOrWhiteSpace(baseUrl)) throw new InvalidOperationException("Před odesláním kampaně nastavte základní URL veřejné aplikace.");
        var sender = await db.EmailSenderConfigurations.SingleAsync(cancellationToken);
        if (!sender.IsDomainVerified) throw new InvalidOperationException("Před odesláním kampaně ověřte doménu odesílatele.");
        if (!await db.EmailDispatches.AnyAsync(x => x.CampaignId == campaignId && x.State == EmailDispatchState.Queued, cancellationToken)) throw new InvalidOperationException("Kampaň nemá žádné způsobilé příjemce k odeslání.");
        campaign.State = EmailCampaignState.Sending;
        campaign.QueuedAtUtc = clock.UtcNow;
        AddAudit(db, "EmailCampaignSendStarted", "Accepted", actor, campaign.BatchId, campaign.Id, null);
        await db.SaveChangesAsync(cancellationToken);
        await ExecuteCampaignAsync(campaignId, actor, baseUrl, cancellationToken);
    }

    public async Task ContinueCampaignAsync(Guid campaignId, uint expectedVersion, CancellationToken cancellationToken = default)
    {
        var actor = await authorization.RequireAsync("OrganizerOperator", cancellationToken);
        var campaign = await db.EmailCampaigns.SingleAsync(x => x.Id == campaignId, cancellationToken);
        if (campaign.Version != expectedVersion) throw new StaleDataException("Tato kampaň se po otevření změnila. Byla načtena aktuální kampaň.");
        if (campaign.State != EmailCampaignState.PausedDailyLimit) throw new InvalidOperationException("Pokračovat lze pouze u kampaně pozastavené denním limitem.");
        if (campaign.ContinueAfterUtc is null || clock.UtcNow < campaign.ContinueAfterUtc) throw new InvalidOperationException($"Pokračovat bude možné nejdříve {campaign.ContinueAfterUtc:u}.");
        await EnsureReviewIsFreshAsync(campaign, actor, cancellationToken);
        var baseUrl = configuration.GetSection("PublicApp").Get<PublicAppOptions>()?.BaseUrl;
        if (string.IsNullOrWhiteSpace(baseUrl)) throw new InvalidOperationException("Před pokračováním kampaně nastavte základní URL veřejné aplikace.");
        campaign.State = EmailCampaignState.Sending;
        campaign.PausedAtUtc = null;
        campaign.ContinueAfterUtc = null;
        AddAudit(db, "EmailCampaignContinued", "Accepted", actor, campaign.BatchId, campaign.Id, null);
        await db.SaveChangesAsync(cancellationToken);
        await ExecuteCampaignAsync(campaignId, actor, baseUrl, cancellationToken);
    }

    private static void ValidateTemplate(EmailTemplateInput input)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(input.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.Subject);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.HtmlBody);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.PlainTextBody);
        if (input.Name.Trim().Length > 200) throw new ArgumentException("Název šablony smí mít nejvýše 200 znaků.");
        if (input.Subject.Length > 300) throw new ArgumentException("Předmět e-mailu smí mít nejvýše 300 znaků.");
        EmailTemplateRenderer.Validate(input.Subject, input.HtmlBody, input.PlainTextBody);
    }

    private async Task ExecuteCampaignAsync(Guid campaignId, string actor, string baseUrl, CancellationToken cancellationToken)
    {
        while (true)
        {
            var claim = await ClaimNextDispatchAsync(campaignId, cancellationToken);
            if (claim is null) break;

            var token = await db.RsvpTokens.SingleOrDefaultAsync(x => x.Id == claim.TokenId && x.RevokedAtUtc == null, cancellationToken);
            var template = await db.EmailTemplates.SingleAsync(x => x.Id == claim.TemplateId, cancellationToken);
            var eventConfiguration = await db.EventConfigurations.SingleAsync(cancellationToken);
            if (token?.RawToken is null)
            {
                await CompleteClaimAsync(claim, new EmailProviderResult(false, false, null, "token-unavailable"), true, cancellationToken);
                continue;
            }

            var zone = TimeZoneInfo.FindSystemTimeZoneById(eventConfiguration.TimeZoneId);
            var rsvpUrl = $"{baseUrl.TrimEnd('/')}/rsvp/{token.RawToken}";
            var rendered = renderer.Render(template.Subject, template.HtmlBody, template.PlainTextBody,
                new EmailRenderData(claim.RecipientName, claim.AllocatedSeats == 1 ? "1 seat" : $"{claim.AllocatedSeats} seats for you and your guest",
                    eventConfiguration.EventName, TimeZoneInfo.ConvertTime(eventConfiguration.StartsAtUtc, zone).ToString("D"),
                    TimeZoneInfo.ConvertTime(eventConfiguration.DoorsAtUtc, zone).ToString("t"), TimeZoneInfo.ConvertTime(eventConfiguration.StartsAtUtc, zone).ToString("t"),
                    eventConfiguration.VenueName, eventConfiguration.VenueAddress,
                    TimeZoneInfo.ConvertTime(claim.DeadlineUtc, zone).ToString("f") + $" ({eventConfiguration.TimeZoneId})", rsvpUrl, eventConfiguration.SupportEmail));
            var result = await emailProvider.SendAsync(new EmailProviderMessage($"{claim.FromDisplayName} <{claim.FromAddress}>", claim.ReplyToAddress,
                claim.RecipientEmail, rendered.Subject, rendered.HtmlBody, rendered.PlainTextBody, claim.IdempotencyKey), cancellationToken);
            await CompleteClaimAsync(claim, result, false, cancellationToken);
        }

        var currentState = await db.EmailCampaigns.AsNoTracking().Where(x => x.Id == campaignId).Select(x => x.State).SingleAsync(cancellationToken);
        if (currentState == EmailCampaignState.PausedDailyLimit)
        {
            var trackedCampaign = db.EmailCampaigns.Local.SingleOrDefault(x => x.Id == campaignId);
            if (trackedCampaign is not null) await db.Entry(trackedCampaign).ReloadAsync(cancellationToken);
            return;
        }
        var campaign = await db.EmailCampaigns.SingleAsync(x => x.Id == campaignId, cancellationToken);
        var states = await db.EmailDispatches.Where(x => x.CampaignId == campaignId).Select(x => x.State).ToListAsync(cancellationToken);
        campaign.State = states.All(x => x is EmailDispatchState.Accepted or EmailDispatchState.Delivered)
            ? EmailCampaignState.Completed
            : states.Any(x => x is EmailDispatchState.Accepted or EmailDispatchState.Delivered)
                ? EmailCampaignState.PartiallyFailed
                : EmailCampaignState.Failed;
        AddAudit(db, "EmailCampaignExecutionFinished", "Accepted", actor, campaign.BatchId, campaign.Id, null);
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<DispatchClaim?> ClaimNextDispatchAsync(Guid campaignId, CancellationToken cancellationToken)
    {
        return await retry.ExecuteAsync(async token =>
        {
            await using var operationDb = await dbFactory.CreateDbContextAsync(token);
            await using var transaction = operationDb.Database.IsRelational()
                ? await operationDb.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, token)
                : null;
            var campaign = await operationDb.EmailCampaigns.SingleAsync(x => x.Id == campaignId, token);
            var dispatch = await operationDb.EmailDispatches.Where(x => x.CampaignId == campaignId && x.State == EmailDispatchState.Queued)
                .OrderBy(x => x.RecipientName).ThenBy(x => x.Id).FirstOrDefaultAsync(token);
            if (dispatch is null)
            {
                if (transaction is not null) await transaction.CommitAsync(token);
                return null;
            }

            var party = await operationDb.InvitationParties.SingleAsync(x => x.Id == dispatch.PartyId, token);
            var tokenEntity = await operationDb.RsvpTokens.SingleOrDefaultAsync(x => x.Id == dispatch.TokenId, token);
            var templateType = await operationDb.EmailTemplates.Where(x => x.Id == campaign.TemplateId).Select(x => x.Type).SingleAsync(token);
            var eventConfiguration = await operationDb.EventConfigurations.SingleAsync(token);
            var statusEligible = campaign.Type == EmailCampaignType.Resend
                ? templateType != EmailTemplateType.Reminder || party.Status == InvitationStatus.Pending
                : party.Status == InvitationStatus.Pending;
            if (tokenEntity?.RevokedAtUtc is not null || tokenEntity?.RawToken is null || !statusEligible || eventConfiguration.IsRsvpLocked || dispatch.DeadlineUtc <= clock.UtcNow)
            {
                dispatch.State = EmailDispatchState.Suppressed;
                dispatch.FailureCategory = "no-longer-eligible";
                await operationDb.SaveChangesAsync(token);
                if (transaction is not null) await transaction.CommitAsync(token);
                return null;
            }

            var dayUtc = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
            var allowance = await operationDb.EmailDailyAllowances.SingleOrDefaultAsync(x => x.DayUtc == dayUtc, token);
            if (allowance is null)
            {
                allowance = new EmailDailyAllowance { DayUtc = dayUtc };
                operationDb.EmailDailyAllowances.Add(allowance);
            }
            if (allowance.ReservedCount >= (await operationDb.EmailSenderConfigurations.SingleAsync(token)).DailySendCeiling)
            {
                campaign.State = EmailCampaignState.PausedDailyLimit;
                campaign.PausedAtUtc = clock.UtcNow;
                campaign.ContinueAfterUtc = dayUtc.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).AddDays(1);
                await operationDb.SaveChangesAsync(token);
                if (transaction is not null) await transaction.CommitAsync(token);
                return null;
            }

            allowance.ReservedCount++;
            var claimId = Guid.NewGuid();
            dispatch.State = EmailDispatchState.Sending;
            dispatch.ClaimId = claimId;
            dispatch.ClaimedAtUtc = clock.UtcNow;
            await operationDb.SaveChangesAsync(token);
            if (transaction is not null) await transaction.CommitAsync(token);
            return new DispatchClaim(dispatch.Id, dispatch.TokenId, campaign.TemplateId, dispatch.RecipientEmail, dispatch.RecipientName,
                dispatch.AllocatedSeats, dispatch.DeadlineUtc, dispatch.IdempotencyKey, campaign.FromDisplayName, campaign.FromAddress, campaign.ReplyToAddress, claimId);
        }, cancellationToken);
    }

    private async Task CompleteClaimAsync(DispatchClaim claim, EmailProviderResult result, bool suppressed, CancellationToken cancellationToken)
    {
        var dispatch = await db.EmailDispatches.SingleAsync(x => x.Id == claim.DispatchId && x.ClaimId == claim.ClaimId, cancellationToken);
        dispatch.AttemptCount++;
        dispatch.ClaimId = null;
        dispatch.ClaimedAtUtc = null;
        if (suppressed)
        {
            dispatch.State = EmailDispatchState.Suppressed;
            dispatch.FailureCategory = result.FailureCategory;
        }
        else if (result.IsAccepted)
        {
            dispatch.State = EmailDispatchState.Accepted;
            dispatch.AcceptedAtUtc = clock.UtcNow;
            dispatch.ProviderMessageId = result.ProviderMessageId;
            dispatch.FailureCategory = null;
        }
        else
        {
            dispatch.State = EmailDispatchState.Failed;
            dispatch.FailureCategory = result.FailureCategory ?? (result.IsTransientFailure ? "provider-transient-failure" : "provider-rejected");
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    private sealed record DispatchClaim(Guid DispatchId, Guid TokenId, Guid TemplateId, string RecipientEmail, string RecipientName,
        int AllocatedSeats, DateTimeOffset DeadlineUtc, string IdempotencyKey, string FromDisplayName, string FromAddress, string ReplyToAddress, Guid ClaimId);

    private async Task EnsureReviewIsFreshAsync(EmailCampaign campaign, string actor, CancellationToken cancellationToken)
    {
        var batch = await db.InvitationBatches.SingleAsync(x => x.Id == campaign.BatchId, cancellationToken);
        var template = await db.EmailTemplates.SingleAsync(x => x.Id == campaign.TemplateId, cancellationToken);
        var sender = await db.EmailSenderConfigurations.SingleAsync(cancellationToken);
        var eventConfiguration = await db.EventConfigurations.SingleAsync(cancellationToken);
        var recipients = await (from dispatch in db.EmailDispatches
                                join party in db.InvitationParties on dispatch.PartyId equals party.Id
                                join token in db.RsvpTokens on dispatch.TokenId equals token.Id
                                where dispatch.CampaignId == campaign.Id
                                select new ReviewRecipient(party.Id, party.PrimaryGuestName, party.Email, party.AllocatedSeats, party.Status,
                                    token.Id, token.RevokedAtUtc == null && token.RawToken != null)).ToListAsync(cancellationToken);
        var currentFingerprint = BuildReviewFingerprint(campaign, batch, eventConfiguration, sender, template, recipients);
        if (campaign.ReviewFingerprint == currentFingerprint) return;

        campaign.State = EmailCampaignState.Invalidated;
        campaign.InvalidatedAtUtc = clock.UtcNow;
        campaign.InvalidationReasonCategory = "review-material-changed";
        AddAudit(db, "EmailCampaignReviewInvalidated", "Accepted", actor, campaign.BatchId, campaign.Id, null);
        await db.SaveChangesAsync(cancellationToken);
        throw new InvalidOperationException("Kontrola kampaně již není platná. Připravte kampaň znovu.");
    }

    private static string BuildReviewFingerprint(EmailCampaign campaign, InvitationBatch batch, EventConfiguration eventConfiguration,
        EmailSenderConfiguration sender, EmailTemplate template, IEnumerable<ReviewRecipient> recipients)
    {
        var material = new StringBuilder();
        Append(material, campaign.Type);
        Append(material, campaign.BatchId);
        Append(material, campaign.SourceCampaignId);
        Append(material, batch.Name);
        Append(material, batch.DeadlineUtc);
        Append(material, campaign.TemplateId);
        Append(material, template.VersionNumber);
        Append(material, template.Name);
        Append(material, template.State);
        Append(material, template.ContentDigest);
        Append(material, Digest(new EmailTemplateInput(template.Type, template.Name, template.Subject, template.HtmlBody, template.PlainTextBody)));
        Append(material, sender.FromDisplayName);
        Append(material, sender.FromAddress);
        Append(material, sender.ReplyToAddress);
        Append(material, sender.IsDomainVerified);
        Append(material, eventConfiguration.EventName);
        Append(material, eventConfiguration.DoorsAtUtc);
        Append(material, eventConfiguration.StartsAtUtc);
        Append(material, eventConfiguration.VenueName);
        Append(material, eventConfiguration.VenueAddress);
        Append(material, eventConfiguration.SupportEmail);
        Append(material, eventConfiguration.IsRsvpLocked);
        foreach (var recipient in recipients.OrderBy(x => x.PartyId))
        {
            Append(material, recipient.PartyId);
            Append(material, recipient.Name);
            Append(material, recipient.Email);
            Append(material, recipient.AllocatedSeats);
            Append(material, recipient.Status);
            Append(material, recipient.TokenId);
            Append(material, recipient.HasAvailableDeliveryMaterial);
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material.ToString())));
    }

    private static void Append(StringBuilder builder, object? value)
    {
        var text = value switch
        {
            DateTimeOffset dateTime => dateTime.UtcDateTime.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            Enum enumValue => Convert.ToInt64(enumValue, System.Globalization.CultureInfo.InvariantCulture).ToString(System.Globalization.CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            _ => value?.ToString() ?? "<null>"
        };
        builder.Append(text.Length).Append(':').Append(text).Append('|');
    }

    private sealed record ReviewRecipient(Guid PartyId, string Name, string Email, int AllocatedSeats, InvitationStatus Status, Guid TokenId, bool HasAvailableDeliveryMaterial);

    private static string Digest(EmailTemplateInput input) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{input.Type}\n{input.Subject}\n{input.HtmlBody}\n{input.PlainTextBody}")));
    private static string? NormalizeEmailOrNull(string email)
    {
        try { return PartyEmailValidation.Normalize(email); }
        catch (ArgumentException) { return null; }
    }
    private void AddAudit(InvitationDbContext context, string type, string outcome, string actor, Guid? batchId, Guid? campaignId, Guid? dispatchId) => context.AuditEvents.Add(new AuditEvent { OccurredAtUtc = clock.UtcNow, EventType = type, Outcome = outcome, ActorCategory = "Organizer", ActorIdentifier = actor, BatchId = batchId, EmailCampaignId = campaignId, EmailDispatchId = dispatchId, CorrelationId = Guid.NewGuid().ToString("N") });
}

public sealed record EmailSenderSettings(string FromDisplayName, string FromAddress, string ReplyToAddress, int DailySendCeiling, bool IsDomainVerified, uint Version);
public sealed record EmailSenderSettingsInput(string FromDisplayName, string FromAddress, string ReplyToAddress, int DailySendCeiling, bool IsDomainVerified);
public sealed record EmailTemplateInput(EmailTemplateType Type, string Name, string Subject, string HtmlBody, string PlainTextBody);
public sealed record EmailTemplateSummary(Guid Id, EmailTemplateType Type, int VersionNumber, string Name, string Subject, EmailTemplateState State, uint Version);
public sealed record EmailCampaignSummary(Guid Id, EmailCampaignType Type, EmailCampaignState State, string BatchName, string TemplateName, int TemplateVersionNumber, DateTimeOffset CreatedAtUtc, int RecipientCount, int AcceptedCount, int FailedCount, uint Version);
public sealed record EmailDispatchSummary(Guid Id, string RecipientName, string RecipientEmail, int AllocatedSeats, EmailDispatchState State, int AttemptCount, string? FailureCategory, string? ProviderMessageId);
public sealed record EmailCampaignSkipSummary(Guid? PartyId, string ReasonCategory);
public sealed record EmailCampaignDetail(Guid Id, EmailCampaignType Type, EmailCampaignState State, uint Version, string BatchName, string TemplateName, EmailTemplateType TemplateType, int TemplateVersionNumber, string FromDisplayName, string FromAddress, string ReplyToAddress, DateTimeOffset? PausedAtUtc, DateTimeOffset? ContinueAfterUtc, IReadOnlyList<EmailDispatchSummary> Dispatches, IReadOnlyList<EmailCampaignSkipSummary> Skips, EmailRenderResult Preview);
