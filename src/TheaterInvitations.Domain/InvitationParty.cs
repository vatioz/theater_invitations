namespace TheaterInvitations.Domain;

public sealed class InvitationParty
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid BatchId { get; init; }
    public string PrimaryGuestName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Company { get; set; }
    public int AllocatedSeats { get; set; }
    public string TokenHash { get; init; } = string.Empty;
    public InvitationStatus Status { get; private set; } = InvitationStatus.Pending;
    public string? AccessibilityRequirements { get; private set; }
    public DateTimeOffset? RespondedAtUtc { get; private set; }
    public uint Version { get; private set; }

    public bool IsEffectivelyExpired(DateTimeOffset deadlineUtc, DateTimeOffset nowUtc) =>
        Status == InvitationStatus.Expired || (Status == InvitationStatus.Pending && nowUtc >= deadlineUtc);

    public RsvpResult Respond(RsvpResponse response, string? accessibilityRequirements, DateTimeOffset deadlineUtc, bool isLocked, DateTimeOffset nowUtc)
    {
        if (isLocked)
        {
            return RsvpResult.Locked;
        }

        if (IsEffectivelyExpired(deadlineUtc, nowUtc))
        {
            Status = InvitationStatus.Expired;
            AccessibilityRequirements = null;
            return RsvpResult.Expired;
        }

        var requestedStatus = response == RsvpResponse.Confirm ? InvitationStatus.Confirmed : InvitationStatus.Declined;
        if (Status == requestedStatus)
        {
            if (requestedStatus == InvitationStatus.Confirmed && AccessibilityRequirements != accessibilityRequirements?.Trim())
            {
                AccessibilityRequirements = accessibilityRequirements?.Trim();
                RespondedAtUtc = nowUtc;
                return RsvpResult.Applied;
            }

            return RsvpResult.Idempotent;
        }

        Status = requestedStatus;
        AccessibilityRequirements = response == RsvpResponse.Confirm ? accessibilityRequirements?.Trim() : null;
        RespondedAtUtc = nowUtc;
        return RsvpResult.Applied;
    }

    public void CorrectDetails(string primaryGuestName, string email, string? company, int allocatedSeats)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(primaryGuestName);
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(allocatedSeats);
        PrimaryGuestName = primaryGuestName.Trim();
        Email = email.Trim();
        Company = string.IsNullOrWhiteSpace(company) ? null : company.Trim();
        AllocatedSeats = allocatedSeats;
    }

    public void OverrideStatus(InvitationStatus status, DateTimeOffset nowUtc)
    {
        Status = status;
        AccessibilityRequirements = status == InvitationStatus.Confirmed ? AccessibilityRequirements : null;
        RespondedAtUtc = nowUtc;
    }
}
