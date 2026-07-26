using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using TheaterInvitations.Domain;
using TheaterInvitations.Web.Data;

namespace TheaterInvitations.Web.Services;

public sealed class EmailCampaignService(InvitationDbContext db, IDbContextFactory<InvitationDbContext> dbFactory, IOrganizerAuthorization authorization, IClock clock, ITransactionRetry retry)
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
                                    join envelope in operationDb.ProtectedDeliveryEnvelopes on rsvpToken.Id equals envelope.TokenId
                                    where party.BatchId == batchId && party.Status == InvitationStatus.Pending && rsvpToken.RevokedAtUtc == null
                                    select new { party, rsvpToken }).ToListAsync(token);
            if (recipients.Count == 0) throw new InvalidOperationException("The selected batch has no eligible invitation recipients.");
            var campaign = new EmailCampaign { Type = EmailCampaignType.InitialInvitation, State = EmailCampaignState.Queued, BatchId = batch.Id, TemplateId = template.Id, TemplateVersionNumber = template.VersionNumber, TemplateDigest = template.ContentDigest, FromDisplayName = sender.FromDisplayName, FromAddress = sender.FromAddress, ReplyToAddress = sender.ReplyToAddress, CreatedAtUtc = clock.UtcNow, CreatedBy = actor, QueuedAtUtc = clock.UtcNow };
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

    private static void ValidateTemplate(EmailTemplateInput input)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(input.Subject);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.HtmlBody);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.PlainTextBody);
        if (input.Subject.Length > 300) throw new ArgumentException("Email subject must be 300 characters or fewer.");
    }

    private static string Digest(EmailTemplateInput input) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{input.Type}\n{input.Subject}\n{input.HtmlBody}\n{input.PlainTextBody}")));
    private void AddAudit(InvitationDbContext context, string type, string outcome, string actor, Guid? batchId, Guid? campaignId, Guid? dispatchId) => context.AuditEvents.Add(new AuditEvent { OccurredAtUtc = clock.UtcNow, EventType = type, Outcome = outcome, ActorCategory = "Organizer", ActorIdentifier = actor, BatchId = batchId, EmailCampaignId = campaignId, EmailDispatchId = dispatchId, CorrelationId = Guid.NewGuid().ToString("N") });
}

public sealed record EmailSenderSettings(string FromDisplayName, string FromAddress, string ReplyToAddress, int DailySendCeiling, bool IsDomainVerified, uint Version);
public sealed record EmailSenderSettingsInput(string FromDisplayName, string FromAddress, string ReplyToAddress, int DailySendCeiling, bool IsDomainVerified);
public sealed record EmailTemplateInput(EmailTemplateType Type, string Subject, string HtmlBody, string PlainTextBody);
public sealed record EmailTemplateSummary(Guid Id, EmailTemplateType Type, int VersionNumber, string Subject, EmailTemplateState State, uint Version);
public sealed record EmailCampaignSummary(Guid Id, EmailCampaignType Type, EmailCampaignState State, string BatchName, int TemplateVersionNumber, DateTimeOffset CreatedAtUtc, int RecipientCount, int AcceptedCount, int FailedCount, uint Version);
