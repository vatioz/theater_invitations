namespace TheaterInvitations.Web.Data;

public sealed class EmailSuppression
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string NormalizedEmail { get; init; } = string.Empty;
    public string ReasonCategory { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; init; }
    public string CreatedBy { get; init; } = string.Empty;
    public uint Version { get; set; }
}
