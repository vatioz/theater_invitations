namespace TheaterInvitations.Web.Data;

public sealed class EmailSenderConfiguration
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string FromDisplayName { get; set; } = string.Empty;
    public string FromAddress { get; set; } = string.Empty;
    public string ReplyToAddress { get; set; } = string.Empty;
    public int DailySendCeiling { get; set; }
    public bool IsDomainVerified { get; set; }
    public DateTimeOffset? VerifiedAtUtc { get; set; }
    public string? VerifiedBy { get; set; }
    public uint Version { get; set; }
}
