namespace TheaterInvitations.Web.Data;

public enum EmailCampaignState
{
    Draft = 0,
    ReadyForReview = 1,
    Queued = 2,
    Sending = 3,
    Completed = 4,
    PartiallyFailed = 5,
    Failed = 6,
    Invalidated = 7,
    PausedDailyLimit = 8
}
