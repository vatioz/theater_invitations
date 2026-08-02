using System.Globalization;
using System.Data;
using System.Security.Cryptography;
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
    private static string CreateRawToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

}

public sealed record OrganizerBatch(Guid Id, string Name, DateTimeOffset DeadlineUtc, InvitationBatchState State, uint Version);
public sealed record OrganizerEmailDispatch(string BatchName, EmailCampaignType CampaignType, DateTimeOffset CampaignCreatedAtUtc, EmailDispatchState State, int AttemptCount, DateTimeOffset? AcceptedAtUtc, string? FailureCategory);
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
