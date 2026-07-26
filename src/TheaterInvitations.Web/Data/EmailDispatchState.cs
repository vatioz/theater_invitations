namespace TheaterInvitations.Web.Data;

public enum EmailDispatchState
{
    Prepared = 0,
    Queued = 1,
    Sending = 2,
    Accepted = 3,
    Delivered = 4,
    Failed = 5,
    Bounced = 6,
    Complained = 7,
    Suppressed = 8
}
