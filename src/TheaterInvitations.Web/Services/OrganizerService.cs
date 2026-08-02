using System.Globalization;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.VisualBasic.FileIO;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Components.QuickGrid;
using TheaterInvitations.Domain;
using TheaterInvitations.Web.Data;

namespace TheaterInvitations.Web.Services;

public sealed class OrganizerService(InvitationDbContext db, IDbContextFactory<InvitationDbContext> dbFactory, IClock clock, IOrganizerAuthorization authorization, ITransactionRetry retry, IHostEnvironment environment)
{
    public async ValueTask<GridItemsProviderResult<OrganizerParty>> GetPartiesAsync(GridItemsProviderRequest<OrganizerParty> request, string? search, InvitationStatus? status, Guid? batchId = null)
    {
        await using var gridDb = await dbFactory.CreateDbContextAsync(request.CancellationToken);
        var query = from party in gridDb.InvitationParties.AsNoTracking()
                    join batch in gridDb.InvitationBatches.AsNoTracking() on party.BatchId equals batch.Id
                    select new { party, batch };
        if (!string.IsNullOrWhiteSpace(search)) query = query.Where(x => x.party.PrimaryGuestName.Contains(search) || x.party.Email.Contains(search) || (x.party.Company != null && x.party.Company.Contains(search)));
        if (status is not null) query = query.Where(x => x.party.Status == status);
        if (batchId is not null) query = query.Where(x => x.batch.Id == batchId);

        var projected = query.Select(x => new OrganizerParty
        {
            Id = x.party.Id,
            Name = x.party.PrimaryGuestName,
            Email = x.party.Email,
            Company = x.party.Company,
            Priority = x.party.Priority,
            Phone = x.party.Phone,
            AllocatedSeats = x.party.AllocatedSeats,
            Status = x.party.Status,
            BatchName = x.batch.Name,
            LatestEmailState = gridDb.EmailDispatches.Where(dispatch => dispatch.PartyId == x.party.Id).OrderByDescending(dispatch => dispatch.Id).Select(dispatch => (EmailDispatchState?)dispatch.State).FirstOrDefault(),
            Version = x.party.Version
        });
        var totalCount = await projected.CountAsync(request.CancellationToken);
        var sorted = request.SortByColumn is null ? projected.OrderBy(x => x.Name) : request.ApplySorting(projected);
        var items = await sorted.Skip(request.StartIndex).Take(request.Count ?? 25).ToArrayAsync(request.CancellationToken);
        return GridItemsProviderResult.From(items, totalCount);
    }

    public async ValueTask<GridItemsProviderResult<OrganizerAudit>> GetAuditsAsync(GridItemsProviderRequest<OrganizerAudit> request)
    {
        await using var gridDb = await dbFactory.CreateDbContextAsync(request.CancellationToken);
        var projected = gridDb.AuditEvents.AsNoTracking().Select(x => new OrganizerAudit
        {
            OccurredAtUtc = x.OccurredAtUtc,
            EventType = x.EventType,
            Outcome = x.Outcome,
            Actor = x.ActorIdentifier ?? x.ActorCategory
        });
        var totalCount = await projected.CountAsync(request.CancellationToken);
        var sorted = request.SortByColumn is null
            ? projected.OrderByDescending(x => x.OccurredAtUtc)
            : request.ApplySorting(projected);
        var items = await sorted.Skip(request.StartIndex).Take(request.Count ?? 25).ToArrayAsync(request.CancellationToken);
        return GridItemsProviderResult.From(items, totalCount);
    }

    public async Task<OrganizerDashboard> GetDashboardAsync(PartyQuery? query = null, CancellationToken cancellationToken = default)
    {
        query ??= new PartyQuery();
        var now = clock.UtcNow;
        var configuration = await db.EventConfigurations.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        var partyQuery = from party in db.InvitationParties join batch in db.InvitationBatches on party.BatchId equals batch.Id select new { party, batch };
        if (!string.IsNullOrWhiteSpace(query.Search)) partyQuery = partyQuery.Where(x => x.party.PrimaryGuestName.Contains(query.Search) || x.party.Email.Contains(query.Search) || (x.party.Company != null && x.party.Company.Contains(query.Search)));
        if (query.Status is not null) partyQuery = partyQuery.Where(x => x.party.Status == query.Status);
        if (query.BatchId is not null) partyQuery = partyQuery.Where(x => x.batch.Id == query.BatchId);
        var totalParties = await partyQuery.CountAsync(cancellationToken);
        var parties = await partyQuery.OrderBy(x => x.party.PrimaryGuestName).Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).Select(x => new OrganizerParty
        {
            Id = x.party.Id,
            Name = x.party.PrimaryGuestName,
            Email = x.party.Email,
            Company = x.party.Company,
            Priority = x.party.Priority,
            Phone = x.party.Phone,
            AllocatedSeats = x.party.AllocatedSeats,
            Status = x.party.Status,
            BatchName = x.batch.Name,
            LatestEmailState = db.EmailDispatches.Where(dispatch => dispatch.PartyId == x.party.Id).OrderByDescending(dispatch => dispatch.Id).Select(dispatch => (EmailDispatchState?)dispatch.State).FirstOrDefault(),
            Version = x.party.Version
        }).ToListAsync(cancellationToken);
        var reserved = await ReservedSeatsAsync(now, cancellationToken);
        var audits = await db.AuditEvents.OrderByDescending(x => x.OccurredAtUtc).Take(20).Select(x => new OrganizerAudit
        {
            OccurredAtUtc = x.OccurredAtUtc,
            EventType = x.EventType,
            Outcome = x.Outcome,
            Actor = x.ActorIdentifier ?? x.ActorCategory
        }).ToListAsync(cancellationToken);
        var metrics = await (from party in db.InvitationParties
                             join batch in db.InvitationBatches on party.BatchId equals batch.Id
                             group party.AllocatedSeats by new
                             {
                                 IsConfirmed = party.Status == InvitationStatus.Confirmed,
                                 IsActivePending = party.Status == InvitationStatus.Pending && batch.DeadlineUtc > now
                             }
                             into groupByStatus
                             select new { groupByStatus.Key, Seats = groupByStatus.Sum() }).ToListAsync(cancellationToken);
        var confirmedSeats = metrics.Where(x => x.Key.IsConfirmed).Sum(x => x.Seats);
        var activePendingSeats = metrics.Where(x => x.Key.IsActivePending).Sum(x => x.Seats);
        return new OrganizerDashboard(configuration?.IsRsvpLocked ?? false, confirmedSeats, activePendingSeats, (configuration?.Capacity ?? 0) - reserved, totalParties, parties, audits, query, (int)Math.Ceiling(totalParties / (double)query.PageSize), configuration);
    }

    public async Task<IReadOnlyList<OrganizerBatch>> GetBatchesAsync(CancellationToken cancellationToken = default) =>
        await db.InvitationBatches.AsNoTracking()
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => new OrganizerBatch(x.Id, x.Name, x.DeadlineUtc, x.State, x.Version))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<OrganizerEmailDispatch>> GetPartyEmailDispatchesAsync(Guid partyId, CancellationToken cancellationToken = default) =>
        await (from dispatch in db.EmailDispatches.AsNoTracking()
               join campaign in db.EmailCampaigns.AsNoTracking() on dispatch.CampaignId equals campaign.Id
               join batch in db.InvitationBatches.AsNoTracking() on campaign.BatchId equals batch.Id
               where dispatch.PartyId == partyId
               orderby campaign.CreatedAtUtc descending
               select new OrganizerEmailDispatch(batch.Name, campaign.Type, campaign.CreatedAtUtc, dispatch.State, dispatch.AttemptCount, dispatch.AcceptedAtUtc, dispatch.FailureCategory))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<OrganizerDraft>> GetDraftsAsync(CancellationToken cancellationToken = default) =>
        await db.InvitationBatches.AsNoTracking()
            .Where(x => x.State != InvitationBatchState.Committed)
            .OrderByDescending(x => x.ModifiedAtUtc)
            .Select(x => new OrganizerDraft(x.Id, x.Name, x.DeadlineUtc, x.State, x.Version, x.ValidationIssue))
            .ToListAsync(cancellationToken);

    public async Task<OrganizerDraftDetail?> GetDraftAsync(Guid batchId, CancellationToken cancellationToken = default)
    {
        var batch = await db.InvitationBatches.AsNoTracking().SingleOrDefaultAsync(x => x.Id == batchId, cancellationToken);
        if (batch is null || batch.State == InvitationBatchState.Committed) return null;
        var rows = await db.InvitationDraftRows.AsNoTracking().Where(x => x.BatchId == batchId)
            .OrderBy(x => x.SourceRowNumber)
            .Select(x => new OrganizerDraftRow(x.SourceRowNumber, x.PrimaryGuestName, x.Email, x.Company, x.Priority, x.Phone, x.AllocatedSeats, x.ValidationIssue))
            .ToListAsync(cancellationToken);
        return new OrganizerDraftDetail(batch.Id, batch.Name, batch.DeadlineUtc, batch.State, batch.Version, batch.ValidationIssue, rows);
    }

    public async Task<OrganizerDraftDetail> SaveDraftAsync(BatchDraftInput input, string csv, Guid? batchId = null, uint? expectedVersion = null, CancellationToken cancellationToken = default)
    {
        var actor = await authorization.RequireAsync("OrganizerOperator", cancellationToken);
        var configuration = await db.EventConfigurations.AsNoTracking().SingleAsync(cancellationToken);
        ValidateBatchInput(input, configuration, clock.UtcNow);
        var normalizedName = input.Name.Trim();
        if (await db.InvitationBatches.AnyAsync(x => x.Id != batchId && x.Name.ToUpper() == normalizedName.ToUpper(), cancellationToken)) throw new InvalidOperationException("A batch with this name already exists.");
        var deadlineUtc = EventConfigurationValidation.ToUtc(input.DeadlineLocal, configuration.TimeZoneId);
        var parsed = await BuildDraftRowsAsync(csv, cancellationToken);
        var now = clock.UtcNow;

        await using var operationDb = await dbFactory.CreateDbContextAsync(cancellationToken);
        InvitationBatch batch;
        if (batchId is null)
        {
            batch = new InvitationBatch { Name = normalizedName, DeadlineUtc = deadlineUtc, State = parsed.IsPrepared ? InvitationBatchState.Prepared : InvitationBatchState.Draft, CreatedAtUtc = now, CreatedBy = actor, ModifiedAtUtc = now, ModifiedBy = actor, SourceDigest = HashContent(csv), ValidationIssue = parsed.DocumentIssue };
            operationDb.InvitationBatches.Add(batch);
        }
        else
        {
            batch = await operationDb.InvitationBatches.SingleAsync(x => x.Id == batchId, cancellationToken);
            if (batch.State == InvitationBatchState.Committed) throw new InvalidOperationException("Committed batches cannot be replaced by a draft.");
            if (expectedVersion is not null && batch.Version != expectedVersion) throw new StaleDataException("This draft changed after you opened it. The current draft has been loaded.");
            operationDb.InvitationDraftRows.RemoveRange(operationDb.InvitationDraftRows.Where(x => x.BatchId == batch.Id));
            batch.Name = normalizedName; batch.DeadlineUtc = deadlineUtc; batch.State = parsed.IsPrepared ? InvitationBatchState.Prepared : InvitationBatchState.Draft; batch.ModifiedAtUtc = now; batch.ModifiedBy = actor; batch.SourceDigest = HashContent(csv); batch.ValidationIssue = parsed.DocumentIssue;
        }

        foreach (var row in parsed.Rows) operationDb.InvitationDraftRows.Add(new InvitationDraftRow { BatchId = batch.Id, SourceRowNumber = row.SourceRowNumber, PrimaryGuestName = row.Name, Email = row.Email, Company = row.Company, Priority = row.Priority, Phone = row.Phone, AllocatedSeats = row.AllocatedSeats, ValidationIssue = row.Issue });
        AddAudit(operationDb, "BatchDraftSaved", "Accepted", batch.Id, null, actor, null);
        await operationDb.SaveChangesAsync(cancellationToken);
        return new OrganizerDraftDetail(batch.Id, batch.Name, batch.DeadlineUtc, batch.State, batch.Version, batch.ValidationIssue, parsed.Rows.Select(x => new OrganizerDraftRow(x.SourceRowNumber, x.Name, x.Email, x.Company, x.Priority, x.Phone, x.AllocatedSeats, x.Issue)).ToList());
    }

    public async Task DeleteDraftAsync(Guid batchId, uint expectedVersion, CancellationToken cancellationToken = default)
    {
        var actor = await authorization.RequireAsync("OrganizerOperator", cancellationToken);
        await using var operationDb = await dbFactory.CreateDbContextAsync(cancellationToken);
        var batch = await operationDb.InvitationBatches.SingleAsync(x => x.Id == batchId, cancellationToken);
        if (batch.State == InvitationBatchState.Committed) throw new InvalidOperationException("Committed batches cannot be deleted as drafts.");
        if (batch.Version != expectedVersion) throw new StaleDataException("This draft changed after you opened it. The current draft has been loaded.");
        operationDb.InvitationDraftRows.RemoveRange(operationDb.InvitationDraftRows.Where(x => x.BatchId == batchId));
        operationDb.InvitationBatches.Remove(batch);
        AddAudit(operationDb, "BatchDraftDeleted", "Accepted", batchId, null, actor, null);
        await operationDb.SaveChangesAsync(cancellationToken);
    }

    public async Task CommitDraftAsync(Guid batchId, uint expectedVersion, CancellationToken cancellationToken = default)
    {
        var actor = await authorization.RequireAsync("OrganizerOperator", cancellationToken);
        await retry.ExecuteAsync(async token =>
        {
            await using var operationDb = await dbFactory.CreateDbContextAsync(token);
            await using var transaction = operationDb.Database.IsRelational() ? await operationDb.Database.BeginTransactionAsync(IsolationLevel.Serializable, token) : null;
            var batch = await operationDb.InvitationBatches.SingleAsync(x => x.Id == batchId, token);
            if (batch.Version != expectedVersion) throw new StaleDataException("This draft changed after you opened it. The current draft has been loaded.");
            if (batch.State != InvitationBatchState.Prepared || batch.DeadlineUtc <= clock.UtcNow) throw new InvalidOperationException("Only a valid future-dated prepared draft may be committed.");
            var rows = await operationDb.InvitationDraftRows.Where(x => x.BatchId == batchId).OrderBy(x => x.SourceRowNumber).ToListAsync(token);
            if (rows.Count == 0 || rows.Any(x => x.ValidationIssue is not null || x.PrimaryGuestName is null || x.Email is null || x.AllocatedSeats is null)) throw new InvalidOperationException("Only a fully valid draft may be committed.");
            var draftEmails = rows.Select(x => x.Email!).ToList();
            if (draftEmails.Distinct(StringComparer.OrdinalIgnoreCase).Count() != draftEmails.Count || await operationDb.InvitationParties.AnyAsync(x => draftEmails.Contains(x.Email), token)) throw new InvalidOperationException("A draft email is already invited or duplicated.");
            var configuration = await operationDb.EventConfigurations.SingleAsync(token);
            var totalSeats = rows.Sum(x => x.AllocatedSeats!.Value);
            if (await ReservedSeatsAsync(operationDb, clock.UtcNow, token) + totalSeats > configuration.Capacity) throw new InvalidOperationException("The draft would exceed remaining capacity.");

            foreach (var row in rows)
            {
                var rawToken = CreateRawToken();
                var hash = RsvpService.HashToken(rawToken);
                var party = new InvitationParty { BatchId = batch.Id, PrimaryGuestName = row.PrimaryGuestName!, Email = row.Email!, Company = row.Company, Priority = row.Priority ?? 3, Phone = row.Phone, AllocatedSeats = row.AllocatedSeats!.Value, TokenHash = hash };
                operationDb.InvitationParties.Add(party);
                var rsvpToken = new RsvpToken { PartyId = party.Id, Hash = hash, RawToken = rawToken, IssuedAtUtc = clock.UtcNow };
                operationDb.RsvpTokens.Add(rsvpToken);
            }

            batch.State = InvitationBatchState.Committed; batch.CommittedAtUtc = clock.UtcNow; batch.CommittedBy = actor; batch.ModifiedAtUtc = clock.UtcNow; batch.ModifiedBy = actor;
            AddAudit(operationDb, "BatchCommitted", "Accepted", batch.Id, null, actor, null);
            await operationDb.SaveChangesAsync(token);
            if (transaction is not null) await transaction.CommitAsync(token);
            return true;
        }, cancellationToken);
    }

    public async Task ChangeBatchDeadlineAsync(Guid batchId, uint expectedVersion, DateTime deadlineLocal, string reason, CancellationToken cancellationToken = default)
    {
        var actor = await authorization.RequireAsync("ElevatedOperator", cancellationToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        await retry.ExecuteAsync(async token =>
        {
            await using var operationDb = await dbFactory.CreateDbContextAsync(token);
            await using var transaction = operationDb.Database.IsRelational() ? await operationDb.Database.BeginTransactionAsync(IsolationLevel.Serializable, token) : null;
            var configuration = await operationDb.EventConfigurations.SingleAsync(token);
            var requestedDeadlineUtc = EventConfigurationValidation.ToUtc(deadlineLocal, configuration.TimeZoneId);
            if (requestedDeadlineUtc <= clock.UtcNow) throw new ArgumentException("The deadline must be in the future.", nameof(deadlineLocal));
            var batch = await operationDb.InvitationBatches.SingleAsync(x => x.Id == batchId, token);
            if (batch.Version != expectedVersion) throw new StaleDataException("This batch changed after you opened it. The current batch has been loaded.");
            if (batch.State != InvitationBatchState.Committed) throw new InvalidOperationException("Only committed batches have an operational deadline.");
            var parties = await operationDb.InvitationParties.Where(x => x.BatchId == batchId).ToListAsync(token);
            var reopens = requestedDeadlineUtc > clock.UtcNow ? parties.Count(x => x.Status == InvitationStatus.Expired && x.ExpirationSource == ExpirationSource.SystemDeadline && x.RespondedAtUtc is null) : 0;
            var batchReserved = parties.Where(x => x.Status == InvitationStatus.Confirmed || x.Status == InvitationStatus.Pending || (reopens > 0 && x.Status == InvitationStatus.Expired && x.ExpirationSource == ExpirationSource.SystemDeadline && x.RespondedAtUtc is null)).Sum(x => x.AllocatedSeats);
            var reservedOtherBatches = await (from party in operationDb.InvitationParties join otherBatch in operationDb.InvitationBatches on party.BatchId equals otherBatch.Id where party.BatchId != batchId && (party.Status == InvitationStatus.Confirmed || (party.Status == InvitationStatus.Pending && otherBatch.DeadlineUtc > clock.UtcNow)) select party.AllocatedSeats).SumAsync(token);
            if (reservedOtherBatches + batchReserved > configuration.Capacity) throw new InvalidOperationException("The deadline extension would exceed remaining capacity.");
            batch.DeadlineUtc = requestedDeadlineUtc; batch.ModifiedAtUtc = clock.UtcNow; batch.ModifiedBy = actor;
            foreach (var party in parties) party.ReopenSystemExpiration();
            AddAudit(operationDb, "BatchDeadlineChanged", "Accepted", batch.Id, null, actor, reason);
            await operationDb.SaveChangesAsync(token);
            if (transaction is not null) await transaction.CommitAsync(token);
            return true;
        }, cancellationToken);
    }

    public async Task RegenerateRsvpTokenAsync(Guid partyId, string reason, CancellationToken cancellationToken = default)
    {
        var actor = await authorization.RequireAsync("ElevatedOperator", cancellationToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        await retry.ExecuteAsync(async token =>
        {
            await using var operationDb = await dbFactory.CreateDbContextAsync(token);
            await using var transaction = operationDb.Database.IsRelational() ? await operationDb.Database.BeginTransactionAsync(IsolationLevel.Serializable, token) : null;
            var party = await operationDb.InvitationParties.SingleAsync(x => x.Id == partyId, token);
            var activeToken = await operationDb.RsvpTokens.SingleOrDefaultAsync(x => x.PartyId == partyId && x.RevokedAtUtc == null, token);
            if (activeToken is not null)
            {
                activeToken.RevokedAtUtc = clock.UtcNow;
                activeToken.RevocationReasonCategory = "replaced";
            }
            var rawToken = CreateRawToken();
            var hash = RsvpService.HashToken(rawToken);
            party.TokenHash = hash;
            var replacement = new RsvpToken { PartyId = party.Id, Hash = hash, RawToken = rawToken, IssuedAtUtc = clock.UtcNow };
            operationDb.RsvpTokens.Add(replacement);
            AddAudit(operationDb, "RsvpTokenRegenerated", "Accepted", null, party.Id, actor, reason);
            await operationDb.SaveChangesAsync(token);
            if (transaction is not null) await transaction.CommitAsync(token);
            return true;
        }, cancellationToken);
    }

    public async Task SaveEventConfigurationAsync(EventConfigurationInput input, uint? expectedVersion, CancellationToken cancellationToken = default)
    {
        var actor = await authorization.RequireAsync("ElevatedOperator", cancellationToken);
        EventConfigurationValidation.ValidateTimeZone(input.TimeZoneId);
        string supportEmail;
        try
        {
            supportEmail = string.IsNullOrWhiteSpace(input.SupportEmail)
                ? string.Empty
                : EventConfigurationValidation.NormalizeSupportEmail(input.SupportEmail, environment.IsDevelopment());
        }
        catch (ArgumentException)
        {
            await RecordSupportEmailAuditAsync("Rejected", actor, "invalid-email", cancellationToken);
            throw;
        }
        if (input.Capacity <= 0 || input.AccessibilityTextLimit < 0)
        {
            throw new ArgumentException("Capacity must be positive and the accessibility limit cannot be negative.");
        }

        await using var operationDb = await dbFactory.CreateDbContextAsync(cancellationToken);
        var configuration = await operationDb.EventConfigurations.SingleOrDefaultAsync(cancellationToken);
        var isNewConfiguration = configuration is null;
        var supportEmailChanged = isNewConfiguration || !string.Equals(configuration!.SupportEmail, supportEmail, StringComparison.OrdinalIgnoreCase);
        if (configuration is null)
        {
            configuration = new EventConfiguration();
            operationDb.EventConfigurations.Add(configuration);
        }
        else if (expectedVersion is not null && configuration.Version != expectedVersion)
        {
            throw new StaleDataException("The event configuration changed after you opened it. The current values have been loaded.");
        }

        configuration.Capacity = input.Capacity;
        configuration.EventName = input.EventName.Trim();
        configuration.DoorsAtUtc = EventConfigurationValidation.ToUtc(input.DoorsLocal, input.TimeZoneId);
        configuration.StartsAtUtc = EventConfigurationValidation.ToUtc(input.StartsLocal, input.TimeZoneId);
        configuration.VenueName = input.VenueName.Trim();
        configuration.VenueAddress = input.VenueAddress.Trim();
        configuration.DressCode = string.IsNullOrWhiteSpace(input.DressCode) ? null : input.DressCode.Trim();
        configuration.TimeZoneId = input.TimeZoneId.Trim();
        configuration.SupportEmail = supportEmail;
        configuration.AccessibilityTextLimit = input.AccessibilityTextLimit;
        EventConfigurationValidation.ValidateEventTimes(configuration);
        AddAudit(operationDb, "EventConfigurationSaved", "Accepted", null, null, actor, null);
        if (supportEmailChanged) AddAudit(operationDb, "SupportEmailChanged", "Accepted", null, null, actor, null);
        await operationDb.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateSupportEmailAsync(string email, uint expectedVersion, CancellationToken cancellationToken = default)
    {
        var actor = await authorization.RequireAsync("ElevatedOperator", cancellationToken);
        string normalizedEmail;
        try
        {
            normalizedEmail = EventConfigurationValidation.NormalizeSupportEmail(email, environment.IsDevelopment());
        }
        catch (ArgumentException)
        {
            await RecordSupportEmailAuditAsync("Rejected", actor, "invalid-email", cancellationToken);
            throw;
        }

        await using var operationDb = await dbFactory.CreateDbContextAsync(cancellationToken);
        var configuration = await operationDb.EventConfigurations.SingleAsync(cancellationToken);
        if (configuration.Version != expectedVersion)
        {
            await RecordSupportEmailAuditAsync("Rejected", actor, "stale", cancellationToken);
            throw new StaleDataException("The support address changed after you opened it. The current address has been loaded.");
        }

        configuration.SupportEmail = normalizedEmail;
        AddAudit(operationDb, "SupportEmailChanged", "Accepted", null, null, actor, null);
        try
        {
            await operationDb.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            await RecordSupportEmailAuditAsync("Rejected", actor, "stale", cancellationToken);
            throw new StaleDataException("The support address changed after you opened it. The current address has been loaded.");
        }
    }

    public async Task SetGlobalLockAsync(bool isLocked, CancellationToken cancellationToken = default)
    {
        var actor = await authorization.RequireAsync("ElevatedOperator", cancellationToken);
        await using var operationDb = await dbFactory.CreateDbContextAsync(cancellationToken);
        var configuration = await operationDb.EventConfigurations.SingleAsync(cancellationToken);
        configuration.IsRsvpLocked = isLocked;
        configuration.LockedAtUtc = isLocked ? clock.UtcNow : null;
        AddAudit(operationDb, isLocked ? "RsvpLocked" : "RsvpUnlocked", "Accepted", null, null, actor, null);
        await operationDb.SaveChangesAsync(cancellationToken);
    }

    public async Task CorrectPartyAsync(Guid partyId, uint expectedVersion, string name, string email, string? company, int priority, string? phone, int seats, CancellationToken cancellationToken = default)
    {
        var actor = await authorization.RequireAsync("OrganizerOperator", cancellationToken);
        string normalizedEmail;
        try { normalizedEmail = PartyEmailValidation.Normalize(email); }
        catch (ArgumentException)
        {
            await RecordOrganizerRejectionAsync("PartyCorrected", actor, partyId, "invalid-email", cancellationToken);
            throw;
        }
        await retry.ExecuteAsync(async token =>
        {
            await using var operationDb = await dbFactory.CreateDbContextAsync(token);
            await using var transaction = operationDb.Database.IsRelational() ? await operationDb.Database.BeginTransactionAsync(IsolationLevel.Serializable, token) : null;
            var party = await operationDb.InvitationParties.SingleAsync(x => x.Id == partyId, token);
            if (party.Version != expectedVersion) { await RecordOrganizerRejectionAsync("PartyCorrected", actor, partyId, "stale", token); throw new StaleDataException("This party changed after you opened it. The latest values have been loaded."); }
            if (await operationDb.InvitationParties.AnyAsync(x => x.Id != partyId && x.Email.ToUpper() == normalizedEmail.ToUpper(), token)) { await RecordOrganizerRejectionAsync("PartyCorrected", actor, partyId, "duplicate-email", token); throw new InvalidOperationException("Email is already invited."); }
            if (seats > party.AllocatedSeats && await ReservedSeatsAsync(operationDb, clock.UtcNow, token) - party.AllocatedSeats + seats > (await operationDb.EventConfigurations.SingleAsync(token)).Capacity) { await RecordOrganizerRejectionAsync("PartyCorrected", actor, partyId, "capacity-exceeded", token); throw new InvalidOperationException("The correction would exceed remaining capacity."); }
            party.CorrectDetails(PartyDataValidation.NormalizeName(name), normalizedEmail, PartyDataValidation.NormalizeCompany(company), PartyDataValidation.NormalizePriority(priority.ToString(CultureInfo.InvariantCulture)), PartyDataValidation.NormalizePhone(phone), seats);
            AddAudit(operationDb, "PartyCorrected", "Accepted", null, partyId, actor, null);
            await operationDb.SaveChangesAsync(token);
            if (transaction is not null) await transaction.CommitAsync(token);
            return true;
        }, cancellationToken);
    }

    public Task CorrectPartyAsync(Guid partyId, uint expectedVersion, string name, string email, string? company, int seats, CancellationToken cancellationToken = default) =>
        CorrectPartyAsync(partyId, expectedVersion, name, email, company, 3, null, seats, cancellationToken);

    public async Task OverrideStatusAsync(Guid partyId, uint expectedVersion, InvitationStatus status, string reason, CancellationToken cancellationToken = default)
    {
        var actor = await authorization.RequireAsync("ElevatedOperator", cancellationToken);
        if (string.IsNullOrWhiteSpace(reason))
        {
            await RecordOrganizerRejectionAsync("PartyStatusOverridden", actor, partyId, "missing-reason", cancellationToken);
            throw new ArgumentException("A reason is required.", nameof(reason));
        }
        await retry.ExecuteAsync(async token =>
        {
            await using var operationDb = await dbFactory.CreateDbContextAsync(token);
            await using var transaction = operationDb.Database.IsRelational() ? await operationDb.Database.BeginTransactionAsync(IsolationLevel.Serializable, token) : null;
            var party = await operationDb.InvitationParties.SingleAsync(x => x.Id == partyId, token);
            if (party.Version != expectedVersion) { await RecordOrganizerRejectionAsync("PartyStatusOverridden", actor, partyId, "stale", token); throw new StaleDataException("This party changed after you opened it. The latest values have been loaded."); }
            var becomingReserved = (status is InvitationStatus.Pending or InvitationStatus.Confirmed) && (party.Status is InvitationStatus.Declined or InvitationStatus.Expired);
            if (becomingReserved && await ReservedSeatsAsync(operationDb, clock.UtcNow, token) + party.AllocatedSeats > (await operationDb.EventConfigurations.SingleAsync(token)).Capacity) { await RecordOrganizerRejectionAsync("PartyStatusOverridden", actor, partyId, "capacity-exceeded", token); throw new InvalidOperationException("The override would exceed remaining capacity."); }
            var previous = party.Status;
            party.OverrideStatus(status, clock.UtcNow);
            AddAudit(operationDb, "PartyStatusOverridden", "Accepted", null, partyId, actor, reason, previous, status, party.Status);
            await operationDb.SaveChangesAsync(token);
            if (transaction is not null) await transaction.CommitAsync(token);
            return true;
        }, cancellationToken);
    }

    private async Task<int> ReservedSeatsAsync(DateTimeOffset now, CancellationToken cancellationToken) => await (from party in db.InvitationParties join batch in db.InvitationBatches on party.BatchId equals batch.Id where party.Status == InvitationStatus.Confirmed || (party.Status == InvitationStatus.Pending && batch.DeadlineUtc > now) select party.AllocatedSeats).SumAsync(cancellationToken);
    private static async Task<int> ReservedSeatsAsync(InvitationDbContext operationDb, DateTimeOffset now, CancellationToken cancellationToken) => await (from party in operationDb.InvitationParties join batch in operationDb.InvitationBatches on party.BatchId equals batch.Id where party.Status == InvitationStatus.Confirmed || (party.Status == InvitationStatus.Pending && batch.DeadlineUtc > now) select party.AllocatedSeats).SumAsync(cancellationToken);
    private async Task RecordSupportEmailAuditAsync(string outcome, string actor, string reason, CancellationToken cancellationToken)
    {
        await using var auditDb = await dbFactory.CreateDbContextAsync(cancellationToken);
        AddAudit(auditDb, "SupportEmailChanged", outcome, null, null, actor, reason);
        await auditDb.SaveChangesAsync(cancellationToken);
    }
    private async Task RecordOrganizerRejectionAsync(string eventType, string actor, Guid partyId, string reason, CancellationToken cancellationToken)
    {
        await using var auditDb = await dbFactory.CreateDbContextAsync(cancellationToken);
        AddAudit(auditDb, eventType, "Rejected", null, partyId, actor, reason);
        await auditDb.SaveChangesAsync(cancellationToken);
    }
    private void AddAudit(InvitationDbContext operationDb, string type, string outcome, Guid? batchId, Guid? partyId, string? actor, string? reason, InvitationStatus? previous = null, InvitationStatus? requested = null, InvitationStatus? resulting = null) => operationDb.AuditEvents.Add(new AuditEvent { OccurredAtUtc = clock.UtcNow, EventType = type, Outcome = outcome, ActorCategory = "Organizer", ActorIdentifier = actor, BatchId = batchId, PartyId = partyId, CorrelationId = Guid.NewGuid().ToString("N"), ReasonCategory = reason, PreviousStatus = previous, RequestedStatus = requested, ResultingStatus = resulting });

    private async Task<DraftParseResult> BuildDraftRowsAsync(string csv, CancellationToken cancellationToken)
    {
        await using var input = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        var parsed = new CsvImportParser().Parse(input);
        var documentFindings = parsed.DocumentFindings.ToList();
        if (parsed.IgnoredHeaders.Count > 0) documentFindings.Add($"Ignorované sloupce: {string.Join(", ", parsed.IgnoredHeaders)}.");
        var documentIssue = documentFindings.Count == 0 ? null : string.Join(" ", documentFindings);
        var existing = await db.InvitationParties.Select(x => x.Email.ToUpper()).ToListAsync(cancellationToken);
        var rows = parsed.Rows.Select(row => new DraftRowInput(row.SourceRowNumber, row.Name, row.Email, row.Company, row.Priority, row.Phone, row.AllocatedSeats, row.Email is not null && existing.Contains(row.Email.ToUpperInvariant()) ? "E-mail je již pozván." : row.ValidationIssue)).ToList();
        return new DraftParseResult(rows, documentIssue, parsed.IgnoredHeaders);
    }

    private static void ValidateBatchInput(BatchDraftInput input, EventConfiguration configuration, DateTimeOffset nowUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(input.Name);
        if (input.Name.Trim().Length > 200) throw new ArgumentException("Batch name must be 200 characters or fewer.", nameof(input));
        var deadlineUtc = EventConfigurationValidation.ToUtc(input.DeadlineLocal, configuration.TimeZoneId);
        if (deadlineUtc <= nowUtc) throw new ArgumentException("The deadline must be in the future.", nameof(input));
    }

    private static string HashContent(string csv) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(csv)));
    private static string CreateRawToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

public sealed record OrganizerBatch(Guid Id, string Name, DateTimeOffset DeadlineUtc, InvitationBatchState State, uint Version);
public sealed record OrganizerEmailDispatch(string BatchName, EmailCampaignType CampaignType, DateTimeOffset CampaignCreatedAtUtc, EmailDispatchState State, int AttemptCount, DateTimeOffset? AcceptedAtUtc, string? FailureCategory);
internal sealed record DraftRowInput(int SourceRowNumber, string? Name, string? Email, string? Company, int Priority, string? Phone, int? AllocatedSeats, string? Issue);
internal sealed record DraftParseResult(IReadOnlyList<DraftRowInput> Rows, string? DocumentIssue, IReadOnlyList<string> IgnoredHeaders) { public bool IsPrepared => DocumentIssue is null && Rows.Count > 0 && Rows.All(x => x.Issue is null); }
public sealed record PartyQuery(string? Search = null, InvitationStatus? Status = null, Guid? BatchId = null, int Page = 1, int PageSize = 25);
public sealed class OrganizerParty
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string? Company { get; init; }
    public int Priority { get; init; }
    public string? Phone { get; init; }
    public int AllocatedSeats { get; init; }
    public InvitationStatus Status { get; init; }
    public string BatchName { get; init; } = string.Empty;
    public EmailDispatchState? LatestEmailState { get; init; }
    public uint Version { get; init; }
}
public sealed class OrganizerAudit
{
    public DateTimeOffset OccurredAtUtc { get; init; }
    public string EventType { get; init; } = string.Empty;
    public string Outcome { get; init; } = string.Empty;
    public string Actor { get; init; } = string.Empty;
}
public sealed record OrganizerDashboard(bool IsRsvpLocked, int ConfirmedSeats, int ActivePendingSeats, int RemainingCapacity, int PartyCount, IReadOnlyList<OrganizerParty> Parties, IReadOnlyList<OrganizerAudit> Audits, PartyQuery Query, int PageCount, EventConfiguration? Configuration);
public sealed record EventConfigurationInput(int Capacity, string EventName, DateTime DoorsLocal, DateTime StartsLocal, string VenueName, string VenueAddress, string? DressCode, string TimeZoneId, string? SupportEmail, int AccessibilityTextLimit);
public sealed record BatchDraftInput(string Name, DateTime DeadlineLocal);
public sealed record OrganizerDraft(Guid Id, string Name, DateTimeOffset DeadlineUtc, InvitationBatchState State, uint Version, string? ValidationIssue);
public sealed record OrganizerDraftRow(int SourceRowNumber, string? Name, string? Email, string? Company, int? Priority, string? Phone, int? AllocatedSeats, string? ValidationIssue);
public sealed record OrganizerDraftDetail(Guid Id, string Name, DateTimeOffset DeadlineUtc, InvitationBatchState State, uint Version, string? ValidationIssue, IReadOnlyList<OrganizerDraftRow> Rows);
