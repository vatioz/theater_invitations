using TheaterInvitations.Domain;

namespace TheaterInvitations.Web.Data;

public sealed class AuditEvent
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public DateTimeOffset OccurredAtUtc { get; init; }
    public string EventType { get; init; } = string.Empty;
    public string Outcome { get; init; } = string.Empty;
    public string ActorCategory { get; init; } = string.Empty;
    public string? ActorIdentifier { get; init; }
    public Guid? PartyId { get; init; }
    public Guid? BatchId { get; init; }
    public string CorrelationId { get; init; } = string.Empty;
    public string? ReasonCategory { get; init; }
    public InvitationStatus? PreviousStatus { get; init; }
    public InvitationStatus? RequestedStatus { get; init; }
    public InvitationStatus? ResultingStatus { get; init; }
}
