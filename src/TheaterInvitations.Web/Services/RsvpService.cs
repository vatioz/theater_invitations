using System.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using TheaterInvitations.Domain;
using TheaterInvitations.Web.Data;

namespace TheaterInvitations.Web.Services;

public sealed class RsvpService(IDbContextFactory<InvitationDbContext> dbFactory, IClock clock, ITransactionRetry retry)
{
    public async Task<RsvpSubmissionResult> SubmitAsync(string token, RsvpSubmission submission, string correlationId, CancellationToken cancellationToken = default)
    {
        return await retry.ExecuteAsync(async tokenCancellation =>
        {
            await using var operationDb = await dbFactory.CreateDbContextAsync(tokenCancellation);
            return await SubmitOnceAsync(operationDb, token, submission, correlationId, tokenCancellation);
        }, cancellationToken);
    }

    private async Task<RsvpSubmissionResult> SubmitOnceAsync(InvitationDbContext operationDb, string token, RsvpSubmission submission, string correlationId, CancellationToken cancellationToken)
    {
        var tokenHash = HashToken(token);
        var nowUtc = clock.UtcNow;
        await using var transaction = operationDb.Database.IsRelational()
            ? await operationDb.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;

        var party = await operationDb.InvitationParties.SingleOrDefaultAsync(x => x.TokenHash == tokenHash, cancellationToken);
        if (party is null)
        {
            AddAudit(operationDb, "RsvpSubmitted", "Rejected", null, null, correlationId, "invalid-token", null, RequestedStatus(submission.Response), null);
            await operationDb.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return new RsvpSubmissionResult(RsvpResult.Expired) { IsValidToken = false };
        }

        var batch = await operationDb.InvitationBatches.SingleAsync(x => x.Id == party.BatchId, cancellationToken);
        var configuration = await operationDb.EventConfigurations.SingleOrDefaultAsync(cancellationToken);
        if (configuration is null)
        {
            AddAudit(operationDb, "RsvpSubmitted", "Rejected", party.Id, batch.Id, correlationId, "configuration-unavailable", party.Status, RequestedStatus(submission.Response), party.Status);
            await operationDb.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return new RsvpSubmissionResult(RsvpResult.Locked);
        }

        if (submission.ExpectedVersion is not null && submission.ExpectedVersion != party.Version)
        {
            AddAudit(operationDb, "RsvpSubmitted", "Rejected", party.Id, batch.Id, correlationId, "stale", party.Status, RequestedStatus(submission.Response), party.Status);
            await operationDb.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return new RsvpSubmissionResult(RsvpResult.Stale);
        }

        if (submission.Response == RsvpResponse.Confirm && submission.AccessibilityRequirements?.Length > configuration.AccessibilityTextLimit)
        {
            AddAudit(operationDb, "RsvpSubmitted", "Rejected", party.Id, batch.Id, correlationId, "accessibility-limit-exceeded", party.Status, RequestedStatus(submission.Response), party.Status);
            await operationDb.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            throw new ArgumentException("Accessibility requirements exceed the configured limit.", nameof(submission));
        }

        if (submission.Response == RsvpResponse.Confirm && !party.IsEffectivelyExpired(batch.DeadlineUtc, nowUtc) && !configuration.IsRsvpLocked)
        {
            var reservedByOtherParties = await ReservedSeatsExceptAsync(operationDb, party.Id, nowUtc, cancellationToken);
            if (reservedByOtherParties + party.AllocatedSeats > configuration.Capacity)
            {
                AddAudit(operationDb, "RsvpSubmitted", "Rejected", party.Id, batch.Id, correlationId, "capacity-exceeded", party.Status, RequestedStatus(submission.Response), party.Status);
                await operationDb.SaveChangesAsync(cancellationToken);
                if (transaction is not null)
                {
                    await transaction.CommitAsync(cancellationToken);
                }

                return new RsvpSubmissionResult(RsvpResult.CapacityExceeded);
            }
        }

        var previousStatus = party.Status;
        var result = party.Respond(submission.Response, submission.AccessibilityRequirements, batch.DeadlineUtc, configuration.IsRsvpLocked, nowUtc);
        var outcome = result is RsvpResult.Applied or RsvpResult.Idempotent ? "Accepted" : "Rejected";
        var reason = result switch
        {
            RsvpResult.Locked => "locked",
            RsvpResult.Expired => "expired",
            _ => null
        };
        AddAudit(operationDb, "RsvpSubmitted", outcome, party.Id, batch.Id, correlationId, reason, previousStatus, RequestedStatus(submission.Response), party.Status);
        await operationDb.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return new RsvpSubmissionResult(result);
    }

    public static string HashToken(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }

    private static async Task<int> ReservedSeatsExceptAsync(InvitationDbContext operationDb, Guid partyId, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        return await (
            from party in operationDb.InvitationParties
            join batch in operationDb.InvitationBatches on party.BatchId equals batch.Id
            where party.Id != partyId && (party.Status == InvitationStatus.Confirmed || (party.Status == InvitationStatus.Pending && batch.DeadlineUtc > nowUtc))
            select party.AllocatedSeats).SumAsync(cancellationToken);
    }

    private static InvitationStatus RequestedStatus(RsvpResponse response) =>
        response == RsvpResponse.Confirm ? InvitationStatus.Confirmed : InvitationStatus.Declined;

    private void AddAudit(InvitationDbContext operationDb, string eventType, string outcome, Guid? partyId, Guid? batchId, string correlationId, string? reason, InvitationStatus? previousStatus, InvitationStatus? requestedStatus, InvitationStatus? resultingStatus)
    {
        operationDb.AuditEvents.Add(new AuditEvent
        {
            OccurredAtUtc = clock.UtcNow,
            EventType = eventType,
            Outcome = outcome,
            ActorCategory = "Invitee",
            PartyId = partyId,
            BatchId = batchId,
            CorrelationId = correlationId,
            ReasonCategory = reason,
            PreviousStatus = previousStatus,
            RequestedStatus = requestedStatus,
            ResultingStatus = resultingStatus
        });
    }
}
