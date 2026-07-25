namespace TheaterInvitations.Web.Data;

public sealed class InvitationBatch
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset DeadlineUtc { get; set; }
    public InvitationBatchState State { get; set; } = InvitationBatchState.Committed;
    public DateTimeOffset CreatedAtUtc { get; init; }
    public string? CreatedBy { get; init; }
    public DateTimeOffset ModifiedAtUtc { get; set; }
    public string? ModifiedBy { get; set; }
    public DateTimeOffset? CommittedAtUtc { get; set; }
    public string? CommittedBy { get; set; }
    public string? SourceDigest { get; set; }
    public string? ValidationIssue { get; set; }
    public uint Version { get; set; }
}
