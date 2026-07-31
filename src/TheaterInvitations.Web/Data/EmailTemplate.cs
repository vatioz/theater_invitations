namespace TheaterInvitations.Web.Data;

public sealed class EmailTemplate
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public EmailTemplateType Type { get; set; }
    public int VersionNumber { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string HtmlBody { get; set; } = string.Empty;
    public string PlainTextBody { get; set; } = string.Empty;
    public EmailTemplateState State { get; set; }
    public string ContentDigest { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; init; }
    public string CreatedBy { get; init; } = string.Empty;
    public DateTimeOffset? ApprovedAtUtc { get; set; }
    public string? ApprovedBy { get; set; }
    public uint Version { get; set; }
}
