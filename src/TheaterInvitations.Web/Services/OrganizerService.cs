using System.Globalization;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using TheaterInvitations.Domain;
using TheaterInvitations.Web.Data;

namespace TheaterInvitations.Web.Services;

public sealed class OrganizerService(InvitationDbContext db, IClock clock)
{
    public async Task<OrganizerDashboard> GetDashboardAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var configuration = await db.EventConfigurations.SingleAsync(cancellationToken);
        var parties = await (from party in db.InvitationParties join batch in db.InvitationBatches on party.BatchId equals batch.Id select new OrganizerParty(party.PrimaryGuestName, party.Email, party.AllocatedSeats, party.Status, batch.Name)).ToListAsync(cancellationToken);
        var reserved = await ReservedSeatsAsync(now, cancellationToken);
        var audits = await db.AuditEvents.OrderByDescending(x => x.OccurredAtUtc).Take(20).Select(x => new OrganizerAudit(x.OccurredAtUtc, x.EventType, x.Outcome, x.ActorIdentifier ?? x.ActorCategory)).ToListAsync(cancellationToken);
        return new OrganizerDashboard(configuration.IsRsvpLocked, parties.Where(x => x.Status == InvitationStatus.Confirmed).Sum(x => x.AllocatedSeats), parties.Where(x => x.Status == InvitationStatus.Pending).Sum(x => x.AllocatedSeats), configuration.Capacity - reserved, parties.Count, parties, audits);
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
        if (!preview.IsValid) throw new InvalidOperationException("Only a valid preview may be committed.");
        await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(cancellationToken) : null;
        var configuration = await db.EventConfigurations.SingleAsync(cancellationToken);
        if (await ReservedSeatsAsync(clock.UtcNow, cancellationToken) + preview.TotalSeats > configuration.Capacity)
        {
            throw new InvalidOperationException("The import would exceed remaining capacity.");
        }
        var batch = new InvitationBatch { Name = batchName, DeadlineUtc = clock.UtcNow.AddDays(14), CreatedAtUtc = clock.UtcNow };
        db.InvitationBatches.Add(batch);
        foreach (var row in preview.ValidRows) db.InvitationParties.Add(new InvitationParty { BatchId = batch.Id, PrimaryGuestName = row.Name, Email = row.Email, Company = row.Company, AllocatedSeats = row.AllocatedSeats, TokenHash = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)) });
        await AuditAsync("BatchImported", "Accepted", batch.Id, null, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
    }

    public async Task SetGlobalLockAsync(bool isLocked, CancellationToken cancellationToken = default)
    {
        var configuration = await db.EventConfigurations.SingleAsync(cancellationToken);
        configuration.IsRsvpLocked = isLocked;
        configuration.LockedAtUtc = isLocked ? clock.UtcNow : null;
        await AuditAsync(isLocked ? "RsvpLocked" : "RsvpUnlocked", "Accepted", null, null, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<int> ReservedSeatsAsync(DateTimeOffset now, CancellationToken cancellationToken) => await (from party in db.InvitationParties join batch in db.InvitationBatches on party.BatchId equals batch.Id where party.Status == InvitationStatus.Confirmed || (party.Status == InvitationStatus.Pending && batch.DeadlineUtc > now) select party.AllocatedSeats).SumAsync(cancellationToken);
    private Task AuditAsync(string type, string outcome, Guid? batchId, Guid? partyId, CancellationToken cancellationToken) { db.AuditEvents.Add(new AuditEvent { OccurredAtUtc = clock.UtcNow, EventType = type, Outcome = outcome, ActorCategory = "Organizer", BatchId = batchId, PartyId = partyId, CorrelationId = Guid.NewGuid().ToString("N") }); return Task.CompletedTask; }

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
public sealed record OrganizerParty(string Name, string Email, int AllocatedSeats, InvitationStatus Status, string BatchName);
public sealed record OrganizerAudit(DateTimeOffset OccurredAtUtc, string EventType, string Outcome, string Actor);
public sealed record OrganizerDashboard(bool IsRsvpLocked, int ConfirmedSeats, int ActivePendingSeats, int RemainingCapacity, int PartyCount, IReadOnlyList<OrganizerParty> Parties, IReadOnlyList<OrganizerAudit> Audits);
