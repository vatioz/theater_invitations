namespace TheaterInvitations.Web.Data;

public sealed class RsvpToken
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid PartyId { get; init; }
    public string Hash { get; init; } = string.Empty;
    public string? RawToken { get; set; }
    public DateTimeOffset IssuedAtUtc { get; init; }
    public DateTimeOffset? RevokedAtUtc { get; set; }
    public string? RevocationReasonCategory { get; set; }
    public uint Version { get; set; }
}
