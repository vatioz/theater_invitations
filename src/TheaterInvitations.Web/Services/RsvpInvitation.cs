using TheaterInvitations.Domain;

namespace TheaterInvitations.Web.Services;

public sealed record RsvpInvitation(
    string PrimaryGuestName,
    int AllocatedSeats,
    DateTimeOffset DeadlineUtc,
    string TimeZoneId,
    string SupportEmail,
    int AccessibilityTextLimit,
    InvitationStatus Status,
    bool IsLocked,
    string? AccessibilityRequirements)
{
    public bool IsExpired { get; init; }
}
