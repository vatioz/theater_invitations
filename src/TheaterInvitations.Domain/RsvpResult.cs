namespace TheaterInvitations.Domain;

public enum RsvpResult
{
    Applied,
    Idempotent,
    Locked,
    Expired,
    CapacityExceeded,
    Stale
}
