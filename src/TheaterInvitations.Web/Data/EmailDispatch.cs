namespace TheaterInvitations.Web.Data;

public sealed class EmailDispatch
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid CampaignId { get; init; }
    public Guid PartyId { get; init; }
    public Guid TokenId { get; init; }
    public string RecipientEmail { get; init; } = string.Empty;
    public string RecipientName { get; init; } = string.Empty;
    public int AllocatedSeats { get; init; }
    public DateTimeOffset DeadlineUtc { get; init; }
    public string IdempotencyKey { get; init; } = string.Empty;
    public EmailDispatchState State { get; set; }
    public int AttemptCount { get; set; }
    public DateTimeOffset? AcceptedAtUtc { get; set; }
    public string? ProviderMessageId { get; set; }
    public string? FailureCategory { get; set; }
    public Guid? ClaimId { get; set; }
    public DateTimeOffset? ClaimedAtUtc { get; set; }
}
