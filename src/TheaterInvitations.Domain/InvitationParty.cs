namespace TheaterInvitations.Domain;

public sealed class InvitationParty
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid BatchId { get; init; }
    public string PrimaryGuestName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Company { get; set; }
    public int AllocatedSeats { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public InvitationStatus Status { get; private set; } = InvitationStatus.Pending;
    public string? AccessibilityRequirements { get; private set; }
    public DateTimeOffset? RespondedAtUtc { get; private set; }
    public ExpirationSource ExpirationSource { get; private set; }
    public uint Version { get; private set; }

    public bool IsEffectivelyExpired(DateTimeOffset deadlineUtc, DateTimeOffset nowUtc) =>
        Status == InvitationStatus.Expired || (Status == InvitationStatus.Pending && nowUtc >= deadlineUtc);

    public bool HasRecordedResponse => Status is InvitationStatus.Confirmed or InvitationStatus.Declined;

    public bool IsResponseWindowClosed(DateTimeOffset deadlineUtc, bool isLocked, DateTimeOffset nowUtc) =>
        isLocked || nowUtc >= deadlineUtc || Status == InvitationStatus.Expired;

    public RsvpResult Respond(RsvpResponse response, string? accessibilityRequirements, DateTimeOffset deadlineUtc, bool isLocked, DateTimeOffset nowUtc)
    {
        if (isLocked)
        {
            return RsvpResult.Locked;
        }

        if (nowUtc >= deadlineUtc)
        {
            if (Status == InvitationStatus.Pending)
            {
                Status = InvitationStatus.Expired;
                ExpirationSource = ExpirationSource.SystemDeadline;
                AccessibilityRequirements = null;
            }

            return RsvpResult.Expired;
        }

        if (Status == InvitationStatus.Expired)
        {
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
        ExpirationSource = ExpirationSource.None;
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
        ExpirationSource = status == InvitationStatus.Expired ? ExpirationSource.OrganizerOverride : ExpirationSource.None;
        AccessibilityRequirements = status == InvitationStatus.Confirmed ? AccessibilityRequirements : null;
        RespondedAtUtc = nowUtc;
    }

    public bool ReopenSystemExpiration()
    {
        if (Status != InvitationStatus.Expired || ExpirationSource != ExpirationSource.SystemDeadline || RespondedAtUtc is not null)
        {
            return false;
        }

        Status = InvitationStatus.Pending;
        ExpirationSource = ExpirationSource.None;
        return true;
    }
}
