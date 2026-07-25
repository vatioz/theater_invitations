using System.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using TheaterInvitations.Domain;
using TheaterInvitations.Web.Data;

namespace TheaterInvitations.Web.Services;

public sealed class RsvpService(InvitationDbContext db, IClock clock)
{
    public async Task<RsvpSubmissionResult> SubmitAsync(string token, RsvpSubmission submission, string correlationId, CancellationToken cancellationToken = default)
    {
        var tokenHash = HashToken(token);
        var nowUtc = clock.UtcNow;
        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;

        var party = await db.InvitationParties.SingleOrDefaultAsync(x => x.TokenHash == tokenHash, cancellationToken);
        if (party is null)
        {
            await WriteAuditAsync("RsvpSubmitted", "Rejected", null, null, correlationId, "invalid-token", null, RequestedStatus(submission.Response), null, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return new RsvpSubmissionResult(RsvpResult.Expired) { IsValidToken = false };
        }

        var batch = await db.InvitationBatches.SingleAsync(x => x.Id == party.BatchId, cancellationToken);
        var configuration = await db.EventConfigurations.SingleAsync(cancellationToken);

        if (submission.Response == RsvpResponse.Confirm && submission.AccessibilityRequirements?.Length > configuration.AccessibilityTextLimit)
        {
            await WriteAuditAsync("RsvpSubmitted", "Rejected", party.Id, batch.Id, correlationId, "accessibility-limit-exceeded", party.Status, RequestedStatus(submission.Response), party.Status, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            throw new ArgumentException("Accessibility requirements exceed the configured limit.", nameof(submission));
        }

        if (submission.Response == RsvpResponse.Confirm && !party.IsEffectivelyExpired(batch.DeadlineUtc, nowUtc) && !configuration.IsRsvpLocked)
        {
            var reservedByOtherParties = await ReservedSeatsExceptAsync(party.Id, nowUtc, cancellationToken);
            if (reservedByOtherParties + party.AllocatedSeats > configuration.Capacity)
            {
                await WriteAuditAsync("RsvpSubmitted", "Rejected", party.Id, batch.Id, correlationId, "capacity-exceeded", party.Status, RequestedStatus(submission.Response), party.Status, cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
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
        await WriteAuditAsync("RsvpSubmitted", outcome, party.Id, batch.Id, correlationId, reason, previousStatus, RequestedStatus(submission.Response), party.Status, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
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

    private async Task<int> ReservedSeatsExceptAsync(Guid partyId, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        return await (
            from party in db.InvitationParties
            join batch in db.InvitationBatches on party.BatchId equals batch.Id
            where party.Id != partyId && (party.Status == InvitationStatus.Confirmed || (party.Status == InvitationStatus.Pending && batch.DeadlineUtc > nowUtc))
            select party.AllocatedSeats).SumAsync(cancellationToken);
    }

    private static InvitationStatus RequestedStatus(RsvpResponse response) =>
        response == RsvpResponse.Confirm ? InvitationStatus.Confirmed : InvitationStatus.Declined;

    private Task WriteAuditAsync(string eventType, string outcome, Guid? partyId, Guid? batchId, string correlationId, string? reason, InvitationStatus? previousStatus, InvitationStatus? requestedStatus, InvitationStatus? resultingStatus, CancellationToken cancellationToken)
    {
        db.AuditEvents.Add(new AuditEvent
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
        return Task.CompletedTask;
    }
}
