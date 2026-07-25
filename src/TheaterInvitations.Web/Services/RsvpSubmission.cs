using TheaterInvitations.Domain;

namespace TheaterInvitations.Web.Services;

public sealed record RsvpSubmission(RsvpResponse Response, string? AccessibilityRequirements, uint? ExpectedVersion = null);

public sealed record RsvpSubmissionResult(RsvpResult Result)
{
    public bool IsValidToken { get; init; } = true;
}
