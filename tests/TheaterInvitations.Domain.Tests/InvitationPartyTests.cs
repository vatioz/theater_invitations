using TheaterInvitations.Domain;

namespace TheaterInvitations.Domain.Tests;

public sealed class InvitationPartyTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Confirming_a_pending_party_confirms_the_entire_allocation()
    {
        var party = CreateParty(2);

        var result = party.Respond(RsvpResponse.Confirm, "Wheelchair space", Now.AddHours(1), false, Now);

        Assert.Equal(RsvpResult.Applied, result);
        Assert.Equal(InvitationStatus.Confirmed, party.Status);
        Assert.Equal(2, party.AllocatedSeats);
        Assert.Equal("Wheelchair space", party.AccessibilityRequirements);
    }

    [Fact]
    public void Repeating_the_same_response_is_idempotent()
    {
        var party = CreateParty(1);
        party.Respond(RsvpResponse.Confirm, null, Now.AddHours(1), false, Now);

        var result = party.Respond(RsvpResponse.Confirm, null, Now.AddHours(1), false, Now.AddMinutes(1));

        Assert.Equal(RsvpResult.Idempotent, result);
        Assert.Equal(Now, party.RespondedAtUtc);
    }

    [Fact]
    public void Confirmed_party_can_update_accessibility_requirements_before_deadline()
    {
        var party = CreateParty(1);
        party.Respond(RsvpResponse.Confirm, "Initial requirement", Now.AddHours(1), false, Now);

        var result = party.Respond(RsvpResponse.Confirm, "Updated requirement", Now.AddHours(1), false, Now.AddMinutes(1));

        Assert.Equal(RsvpResult.Applied, result);
        Assert.Equal("Updated requirement", party.AccessibilityRequirements);
        Assert.Equal(Now.AddMinutes(1), party.RespondedAtUtc);
    }

    [Fact]
    public void Declining_clears_accessibility_requirements()
    {
        var party = CreateParty(1);
        party.Respond(RsvpResponse.Confirm, "Hearing assistance", Now.AddHours(1), false, Now);

        party.Respond(RsvpResponse.Decline, null, Now.AddHours(1), false, Now.AddMinutes(1));

        Assert.Equal(InvitationStatus.Declined, party.Status);
        Assert.Null(party.AccessibilityRequirements);
    }

    [Fact]
    public void Expired_invitation_cannot_be_mutated()
    {
        var party = CreateParty(1);

        var result = party.Respond(RsvpResponse.Confirm, null, Now.AddMinutes(-1), false, Now);

        Assert.Equal(RsvpResult.Expired, result);
        Assert.Equal(InvitationStatus.Expired, party.Status);
    }

    [Fact]
    public void Global_lock_rejects_response_without_changing_state()
    {
        var party = CreateParty(1);

        var result = party.Respond(RsvpResponse.Confirm, null, Now.AddHours(1), true, Now);

        Assert.Equal(RsvpResult.Locked, result);
        Assert.Equal(InvitationStatus.Pending, party.Status);
    }

    private static InvitationParty CreateParty(int seats) => new()
    {
        AllocatedSeats = seats,
        PrimaryGuestName = "Alex Guest",
        Email = "alex@example.test",
        TokenHash = "not-used-by-domain-tests"
    };
}
