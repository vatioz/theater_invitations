namespace TheaterInvitations.Web.Data;

public sealed class InvitationBatch
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset DeadlineUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public uint Version { get; set; }
}
