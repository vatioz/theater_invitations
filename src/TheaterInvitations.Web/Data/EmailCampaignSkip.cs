namespace TheaterInvitations.Web.Data;

public sealed class EmailCampaignSkip
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid CampaignId { get; init; }
    public Guid? PartyId { get; init; }
    public string ReasonCategory { get; init; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; init; }
}
