namespace TheaterInvitations.Web.Data;

public sealed class EmailCampaign
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public EmailCampaignType Type { get; set; }
    public EmailCampaignState State { get; set; }
    public Guid BatchId { get; init; }
    public Guid TemplateId { get; init; }
    public int TemplateVersionNumber { get; init; }
    public string TemplateDigest { get; init; } = string.Empty;
    public string FromDisplayName { get; init; } = string.Empty;
    public string FromAddress { get; init; } = string.Empty;
    public string ReplyToAddress { get; init; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; init; }
    public string CreatedBy { get; init; } = string.Empty;
    public DateTimeOffset QueuedAtUtc { get; set; }
    public string ReviewFingerprint { get; set; } = string.Empty;
    public DateTimeOffset? InvalidatedAtUtc { get; set; }
    public string? InvalidationReasonCategory { get; set; }
    public DateTimeOffset? PausedAtUtc { get; set; }
    public DateTimeOffset? ContinueAfterUtc { get; set; }
    public uint Version { get; set; }
}
