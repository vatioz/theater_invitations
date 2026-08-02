using System.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using TheaterInvitations.Domain;
using TheaterInvitations.Web.Data;

namespace TheaterInvitations.Web.Services;

public sealed class BatchImportPreviewStore : IDisposable
{
    private readonly object gate = new();
    private StoredBatchImportPreview? current;

    public string Replace(string actor, StoredBatchImportPreview preview)
    {
        lock (gate)
        {
            current = preview with { Actor = actor, PreviewId = CreateId(), ExpiresAtUtc = preview.CreatedAtUtc.AddMinutes(10) };
            return current.PreviewId;
        }
    }

    public StoredBatchImportPreview? Take(string actor, string previewId, DateTimeOffset nowUtc)
    {
        lock (gate)
        {
            if (current is null || current.Actor != actor || current.PreviewId != previewId || current.ExpiresAtUtc <= nowUtc) return null;
            var result = current;
            current = null;
            return result;
        }
    }

    public void Dispose()
    {
        lock (gate) current = null;
    }

    private static string CreateId() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

public sealed record StoredBatchImportPreview(string Actor, string PreviewId, DateTimeOffset CreatedAtUtc, DateTimeOffset ExpiresAtUtc, byte[] SourceBytes, string Name, DateTime DeadlineLocal);

public sealed class BatchImportService(
    InvitationDbContext db,
    IDbContextFactory<InvitationDbContext> dbFactory,
    IClock clock,
    IOrganizerAuthorization authorization,
    ITransactionRetry retry,
    BatchImportPreviewStore previews)
{
    private readonly CsvImportParser parser = new();

    public async Task<BatchImportPreview> PreviewAsync(BatchImportInput input, Stream source, CancellationToken cancellationToken = default)
    {
        var actor = await authorization.RequireAsync("OrganizerOperator", cancellationToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.Name);
        var name = input.Name.Trim();
        if (name.Length > 200) throw new ArgumentException("Název dávky může mít nejvýše 200 znaků.", nameof(input));
        var configuration = await db.EventConfigurations.AsNoTracking().SingleAsync(cancellationToken);
        var deadlineUtc = EventConfigurationValidation.ToUtc(input.DeadlineLocal, configuration.TimeZoneId);
        if (deadlineUtc <= clock.UtcNow) throw new ArgumentException("Termín musí být v budoucnosti.", nameof(input));

        var sourceBytes = await ReadBoundedAsync(source, cancellationToken);
        var parsed = Parse(sourceBytes);
        var existingEmails = await db.InvitationParties.AsNoTracking().Select(x => x.Email).ToListAsync(cancellationToken);
        var existingEmailSet = existingEmails.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var rows = parsed.Rows.Select(row =>
        {
            var findings = row.Findings.ToList();
            if (row.Email is not null && existingEmailSet.Contains(row.Email)) findings.Add("E-mail je již pozván v potvrzené dávce.");
            return new BatchImportPreviewRow(row.SourceRowNumber, row.Name, row.Email, row.AllocatedSeats, row.Company, row.Priority, row.Phone, findings);
        }).ToList();
        var batchNameExists = await db.InvitationBatches.AnyAsync(x => x.Name.ToUpper() == name.ToUpper(), cancellationToken);
        var reserved = await ReservedSeatsAsync(db, clock.UtcNow, cancellationToken);
        var total = parsed.AllocatedSeatTotal ?? 0;
        var documentFindings = parsed.DocumentFindings.ToList();
        if (batchNameExists) documentFindings.Add("Název dávky již existuje.");
        var preview = new BatchImportPreview(
            string.Empty,
            name,
            input.DeadlineLocal,
            deadlineUtc,
            configuration.TimeZoneId,
            documentFindings,
            parsed.IgnoredHeaders,
            rows,
            rows.Count,
            rows.Count(x => x.Findings.Count == 0),
            rows.Count(x => x.Findings.Count > 0),
            total,
            reserved,
            configuration.Capacity - reserved,
            configuration.Capacity - reserved - total,
            !batchNameExists && parsed.IsValid && rows.All(x => x.Findings.Count == 0) && deadlineUtc > clock.UtcNow && (long)reserved + total <= configuration.Capacity);
        var previewId = previews.Replace(actor, new StoredBatchImportPreview(actor, string.Empty, clock.UtcNow, clock.UtcNow, sourceBytes, name, input.DeadlineLocal));
        return preview with { PreviewId = previewId };
    }

    public async Task ConfirmAsync(string previewId, CancellationToken cancellationToken = default)
    {
        var actor = await authorization.RequireAsync("OrganizerOperator", cancellationToken);
        var stored = previews.Take(actor, previewId, clock.UtcNow) ?? throw new InvalidOperationException("Náhled již není dostupný. Nahrajte soubor znovu.");
        try
        {
            await retry.ExecuteAsync(async token =>
            {
                await using var operationDb = await dbFactory.CreateDbContextAsync(token);
                await using var transaction = operationDb.Database.IsRelational() ? await operationDb.Database.BeginTransactionAsync(IsolationLevel.Serializable, token) : null;
                var configuration = await operationDb.EventConfigurations.SingleAsync(token);
                var parsed = Parse(stored.SourceBytes);
                var deadlineUtc = EventConfigurationValidation.ToUtc(stored.DeadlineLocal, configuration.TimeZoneId);
                var reason = await ValidateForImportAsync(operationDb, parsed, stored.Name, deadlineUtc, configuration, token);
                if (reason is not null) throw new BatchImportRejectedException(reason);
                var batch = new InvitationBatch { Name = stored.Name, DeadlineUtc = deadlineUtc, State = InvitationBatchState.Committed, CreatedAtUtc = clock.UtcNow, CreatedBy = actor, ModifiedAtUtc = clock.UtcNow, ModifiedBy = actor, CommittedAtUtc = clock.UtcNow, CommittedBy = actor, SourceDigest = Convert.ToHexString(SHA256.HashData(stored.SourceBytes)) };
                operationDb.InvitationBatches.Add(batch);
                foreach (var row in parsed.Rows)
                {
                    var rawToken = CreateRawToken();
                    var hash = RsvpService.HashToken(rawToken);
                    var party = new InvitationParty { BatchId = batch.Id, PrimaryGuestName = row.Name!, Email = row.Email!, Company = row.Company, Priority = row.Priority, Phone = row.Phone, AllocatedSeats = row.AllocatedSeats!.Value, TokenHash = hash };
                    operationDb.InvitationParties.Add(party);
                    operationDb.RsvpTokens.Add(new RsvpToken { PartyId = party.Id, Hash = hash, RawToken = rawToken, IssuedAtUtc = clock.UtcNow });
                }
                operationDb.AuditEvents.Add(new AuditEvent { OccurredAtUtc = clock.UtcNow, EventType = "BatchImported", Outcome = "Accepted", ActorCategory = "Organizer", ActorIdentifier = actor, BatchId = batch.Id, CorrelationId = Guid.NewGuid().ToString("N") });
                await operationDb.SaveChangesAsync(token);
                if (transaction is not null) await transaction.CommitAsync(token);
                return true;
            }, cancellationToken);
        }
        catch (BatchImportRejectedException exception)
        {
            await RecordRejectionAsync(actor, exception.Reason, cancellationToken);
            throw new InvalidOperationException("Import již není platný. Zkontrolujte aktuální údaje a vytvořte nový náhled.");
        }
        catch (DbUpdateException)
        {
            await RecordRejectionAsync(actor, "concurrent-conflict", cancellationToken);
            throw new InvalidOperationException("Import již není platný kvůli souběžné změně. Vytvořte nový náhled.");
        }
    }

    private CsvImportDocument Parse(byte[] bytes) => parser.Parse(new MemoryStream(bytes));

    private static async Task<byte[]> ReadBoundedAsync(Stream source, CancellationToken cancellationToken)
    {
        await using var bytes = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (bytes.Length + read > CsvImportParser.DefaultMaximumBytes) throw new ArgumentException("CSV nesmí být větší než 1 MB.", nameof(source));
            await bytes.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        return bytes.ToArray();
    }

    private async Task<string?> ValidateForImportAsync(InvitationDbContext operationDb, CsvImportDocument parsed, string name, DateTimeOffset deadlineUtc, EventConfiguration configuration, CancellationToken cancellationToken)
    {
        if (!parsed.IsValid) return "invalid-source";
        if (deadlineUtc <= clock.UtcNow) return "deadline-expired";
        if (await operationDb.InvitationBatches.AnyAsync(x => x.Name.ToUpper() == name.ToUpper(), cancellationToken)) return "duplicate-batch-name";
        var emails = parsed.Rows.Select(x => x.Email!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var committedEmails = await operationDb.InvitationParties.Select(x => x.Email).ToListAsync(cancellationToken);
        if (committedEmails.Any(emails.Contains)) return "duplicate-email";
        var total = parsed.AllocatedSeatTotal ?? 0;
        if ((long)await ReservedSeatsAsync(operationDb, clock.UtcNow, cancellationToken) + total > configuration.Capacity) return "capacity-exceeded";
        return null;
    }

    private async Task RecordRejectionAsync(string actor, string reason, CancellationToken cancellationToken)
    {
        await using var auditDb = await dbFactory.CreateDbContextAsync(cancellationToken);
        auditDb.AuditEvents.Add(new AuditEvent { OccurredAtUtc = clock.UtcNow, EventType = "BatchImported", Outcome = "Rejected", ActorCategory = "Organizer", ActorIdentifier = actor, ReasonCategory = reason, CorrelationId = Guid.NewGuid().ToString("N") });
        await auditDb.SaveChangesAsync(cancellationToken);
    }

    private static async Task<int> ReservedSeatsAsync(InvitationDbContext context, DateTimeOffset now, CancellationToken cancellationToken) => await (from party in context.InvitationParties join batch in context.InvitationBatches on party.BatchId equals batch.Id where party.Status == InvitationStatus.Confirmed || (party.Status == InvitationStatus.Pending && batch.DeadlineUtc > now) select party.AllocatedSeats).SumAsync(cancellationToken);
    private static string CreateRawToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

public sealed class BatchImportRejectedException(string reason) : Exception
{
    public string Reason { get; } = reason;
}

public sealed record BatchImportInput(string Name, DateTime DeadlineLocal);
public sealed record BatchImportPreview(string PreviewId, string Name, DateTime DeadlineLocal, DateTimeOffset DeadlineUtc, string TimeZoneId, IReadOnlyList<string> DocumentFindings, IReadOnlyList<string> IgnoredHeaders, IReadOnlyList<BatchImportPreviewRow> Rows, int PartyCount, int ValidRowCount, int InvalidRowCount, int AllocatedSeatTotal, int ReservedSeats, int RemainingCapacity, int ProjectedRemainingCapacity, bool IsValid);
public sealed record BatchImportPreviewRow(int SourceRowNumber, string? Name, string? Email, int? AllocatedSeats, string? Company, int Priority, string? Phone, IReadOnlyList<string> Findings);
