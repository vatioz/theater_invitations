using TheaterInvitations.Domain;

namespace TheaterInvitations.Web.Services;

public sealed record RsvpInvitation(
    string PrimaryGuestName,
    int AllocatedSeats,
    DateTimeOffset DeadlineUtc,
    string EventName,
    DateTimeOffset DoorsAtUtc,
    DateTimeOffset StartsAtUtc,
    string VenueName,
    string VenueAddress,
    string? DressCode,
    string TimeZoneId,
    string SupportEmail,
    int AccessibilityTextLimit,
    InvitationStatus Status,
    bool IsLocked,
    string? AccessibilityRequirements,
    uint Version)
{
    public bool IsExpired { get; init; }
    public bool IsConfigurationAvailable { get; init; }
    public bool HasEventDetails =>
        !string.IsNullOrWhiteSpace(EventName) &&
        DoorsAtUtc != default &&
        StartsAtUtc != default &&
        !string.IsNullOrWhiteSpace(VenueName) &&
        !string.IsNullOrWhiteSpace(VenueAddress) &&
        !string.IsNullOrWhiteSpace(TimeZoneId);
}
