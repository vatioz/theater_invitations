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
            AllocatedSeats = x.party.AllocatedSeats,
            Status = x.party.Status,
            BatchName = x.batch.Name,
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
            AllocatedSeats = x.party.AllocatedSeats,
            Status = x.party.Status,
            BatchName = x.batch.Name,
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
        var statusCounts = await db.InvitationParties.GroupBy(x => x.Status).Select(x => new { Status = x.Key, Seats = x.Sum(p => p.AllocatedSeats) }).ToListAsync(cancellationToken);
        return new OrganizerDashboard(configuration?.IsRsvpLocked ?? false, statusCounts.SingleOrDefault(x => x.Status == InvitationStatus.Confirmed)?.Seats ?? 0, statusCounts.SingleOrDefault(x => x.Status == InvitationStatus.Pending)?.Seats ?? 0, (configuration?.Capacity ?? 0) - reserved, totalParties, parties, audits, query, (int)Math.Ceiling(totalParties / (double)query.PageSize), configuration);
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

    public async Task<ImportPreview> PreviewImportAsync(string csv, CancellationToken cancellationToken = default)
    {
        var rows = ParseCsv(csv);
        var errors = new List<string>();
        var valid = new List<ImportRow>();
        if (rows.Count == 0 || !rows[0].SequenceEqual(new[] { "primary_guest_name", "email", "company", "allocated_seats" }, StringComparer.Ordinal)) return new ImportPreview(valid, new[] { "CSV must use the canonical header row." });
        var existing = await db.InvitationParties.Select(x => x.Email.ToUpper()).ToListAsync(cancellationToken);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 1; index < rows.Count; index++)
        {
            var fields = rows[index];
            var rowNumber = index + 1;
            if (fields.Length != 4)
            {
                errors.Add($"Row {rowNumber} must contain exactly 4 columns."); continue;
            }
            if (string.IsNullOrWhiteSpace(fields[0]))
            {
                errors.Add($"Row {rowNumber}: primary_guest_name is required."); continue;
            }
            if (string.IsNullOrWhiteSpace(fields[1]))
            {
                errors.Add($"Row {rowNumber}: email is required."); continue;
            }
            if (!int.TryParse(fields[3], CultureInfo.InvariantCulture, out var seats) || seats <= 0)
            {
                errors.Add($"Row {rowNumber}: allocated_seats must be a positive integer."); continue;
            }
            var email = fields[1].Trim();
            if (!seen.Add(email) || existing.Contains(email.ToUpperInvariant())) { errors.Add($"Row {rowNumber}: email is already invited or duplicated in this upload."); continue; }
            valid.Add(new ImportRow(fields[0].Trim(), email, string.IsNullOrWhiteSpace(fields[2]) ? null : fields[2].Trim(), seats));
        }
        var capacity = (await db.EventConfigurations.SingleAsync(cancellationToken)).Capacity;
        if (await ReservedSeatsAsync(clock.UtcNow, cancellationToken) + valid.Sum(x => x.AllocatedSeats) > capacity) errors.Add("The import would exceed remaining capacity.");
        return new ImportPreview(valid, errors);
    }

    public async Task CommitImportAsync(ImportPreview preview, string batchName, CancellationToken cancellationToken = default)
    {
        var actor = await authorization.RequireAsync("OrganizerOperator", cancellationToken);
        if (!preview.IsValid) throw new InvalidOperationException("Only a valid preview may be committed.");
        await retry.ExecuteAsync(async token =>
        {
            await using var operationDb = await dbFactory.CreateDbContextAsync(token);
            await using var transaction = operationDb.Database.IsRelational() ? await operationDb.Database.BeginTransactionAsync(IsolationLevel.Serializable, token) : null;
            var configuration = await operationDb.EventConfigurations.SingleAsync(token);
            if (await ReservedSeatsAsync(operationDb, clock.UtcNow, token) + preview.TotalSeats > configuration.Capacity) throw new InvalidOperationException("The import would exceed remaining capacity.");
            var batch = new InvitationBatch { Name = batchName, DeadlineUtc = clock.UtcNow.AddDays(14), CreatedAtUtc = clock.UtcNow };
            operationDb.InvitationBatches.Add(batch);
            foreach (var row in preview.ValidRows) operationDb.InvitationParties.Add(new InvitationParty { BatchId = batch.Id, PrimaryGuestName = row.Name, Email = row.Email, Company = row.Company, AllocatedSeats = row.AllocatedSeats, TokenHash = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)) });
            AddAudit(operationDb, "BatchImported", "Accepted", batch.Id, null, actor, null);
            await operationDb.SaveChangesAsync(token);
            if (transaction is not null) await transaction.CommitAsync(token);
            return true;
        }, cancellationToken);
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

    public async Task CorrectPartyAsync(Guid partyId, uint expectedVersion, string name, string email, string? company, int seats, CancellationToken cancellationToken = default)
    {
        var actor = await authorization.RequireAsync("OrganizerOperator", cancellationToken);
        await retry.ExecuteAsync(async token =>
        {
            await using var operationDb = await dbFactory.CreateDbContextAsync(token);
            await using var transaction = operationDb.Database.IsRelational() ? await operationDb.Database.BeginTransactionAsync(IsolationLevel.Serializable, token) : null;
            var party = await operationDb.InvitationParties.SingleAsync(x => x.Id == partyId, token);
            if (party.Version != expectedVersion) throw new StaleDataException("This party changed after you opened it. The latest values have been loaded.");
            if (await operationDb.InvitationParties.AnyAsync(x => x.Id != partyId && x.Email.ToUpper() == email.Trim().ToUpper(), token)) throw new InvalidOperationException("Email is already invited.");
            if (seats > party.AllocatedSeats && await ReservedSeatsAsync(operationDb, clock.UtcNow, token) - party.AllocatedSeats + seats > (await operationDb.EventConfigurations.SingleAsync(token)).Capacity) throw new InvalidOperationException("The correction would exceed remaining capacity.");
            party.CorrectDetails(name, email, company, seats);
            AddAudit(operationDb, "PartyCorrected", "Accepted", null, partyId, actor, null);
            await operationDb.SaveChangesAsync(token);
            if (transaction is not null) await transaction.CommitAsync(token);
            return true;
        }, cancellationToken);
    }

    public async Task OverrideStatusAsync(Guid partyId, uint expectedVersion, InvitationStatus status, string reason, CancellationToken cancellationToken = default)
    {
        var actor = await authorization.RequireAsync("ElevatedOperator", cancellationToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        await retry.ExecuteAsync(async token =>
        {
            await using var operationDb = await dbFactory.CreateDbContextAsync(token);
            await using var transaction = operationDb.Database.IsRelational() ? await operationDb.Database.BeginTransactionAsync(IsolationLevel.Serializable, token) : null;
            var party = await operationDb.InvitationParties.SingleAsync(x => x.Id == partyId, token);
            if (party.Version != expectedVersion) throw new StaleDataException("This party changed after you opened it. The latest values have been loaded.");
            var becomingReserved = (status is InvitationStatus.Pending or InvitationStatus.Confirmed) && (party.Status is InvitationStatus.Declined or InvitationStatus.Expired);
            if (becomingReserved && await ReservedSeatsAsync(operationDb, clock.UtcNow, token) + party.AllocatedSeats > (await operationDb.EventConfigurations.SingleAsync(token)).Capacity) throw new InvalidOperationException("The override would exceed remaining capacity.");
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
    private void AddAudit(InvitationDbContext operationDb, string type, string outcome, Guid? batchId, Guid? partyId, string? actor, string? reason, InvitationStatus? previous = null, InvitationStatus? requested = null, InvitationStatus? resulting = null) => operationDb.AuditEvents.Add(new AuditEvent { OccurredAtUtc = clock.UtcNow, EventType = type, Outcome = outcome, ActorCategory = "Organizer", ActorIdentifier = actor, BatchId = batchId, PartyId = partyId, CorrelationId = Guid.NewGuid().ToString("N"), ReasonCategory = reason, PreviousStatus = previous, RequestedStatus = requested, ResultingStatus = resulting });

    private static List<string[]> ParseCsv(string text)
    {
        var rows = new List<string[]>();
        var fields = new List<string>();
        var field = new System.Text.StringBuilder();
        var quoted = false;
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (character == '"' && quoted && index + 1 < text.Length && text[index + 1] == '"') { field.Append(character); index++; }
            else if (character == '"') quoted = !quoted;
            else if (character == ',' && !quoted) { fields.Add(field.ToString()); field.Clear(); }
            else if ((character == '\r' || character == '\n') && !quoted)
            {
                if (character == '\r' && index + 1 < text.Length && text[index + 1] == '\n') index++;
                fields.Add(field.ToString()); field.Clear();
                if (fields.Any(x => x.Length > 0)) rows.Add(fields.ToArray());
                fields.Clear();
            }
            else field.Append(character);
        }
        if (quoted) return new List<string[]>();
        if (field.Length > 0 || fields.Count > 0) { fields.Add(field.ToString()); rows.Add(fields.ToArray()); }
        return rows;
    }
}

public sealed record ImportRow(string Name, string Email, string? Company, int AllocatedSeats);
public sealed record ImportPreview(IReadOnlyList<ImportRow> ValidRows, IReadOnlyList<string> Errors) { public int TotalSeats => ValidRows.Sum(x => x.AllocatedSeats); public bool IsValid => Errors.Count == 0; }
public sealed record PartyQuery(string? Search = null, InvitationStatus? Status = null, Guid? BatchId = null, int Page = 1, int PageSize = 25);
public sealed class OrganizerParty
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string? Company { get; init; }
    public int AllocatedSeats { get; init; }
    public InvitationStatus Status { get; init; }
    public string BatchName { get; init; } = string.Empty;
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
