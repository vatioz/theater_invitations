namespace TheaterInvitations.Web.Data;

public sealed class InvitationDraftRow
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid BatchId { get; init; }
    public int SourceRowNumber { get; init; }
    public string? PrimaryGuestName { get; init; }
    public string? Email { get; init; }
    public string? Company { get; init; }
    public int? Priority { get; init; }
    public string? Phone { get; init; }
    public int? AllocatedSeats { get; init; }
    public string? ValidationIssue { get; init; }
}
