using TheaterInvitations.Domain;
using TheaterInvitations.Web.Data;
using TheaterInvitations.Web.Services;

namespace TheaterInvitations.Domain.Tests;

public sealed class CzechPresentationFormatterTests
{
    private static readonly TimeZoneInfo Prague = TimeZoneInfo.FindSystemTimeZoneById("Europe/Prague");

    [Theory]
    [InlineData(1, "1 místo")]
    [InlineData(2, "2 místa")]
    [InlineData(4, "4 místa")]
    [InlineData(5, "5 míst")]
    [InlineData(11, "11 míst")]
    [InlineData(21, "21 míst")]
    public void Seats_Uses_Czech_Plural_Forms(int count, string expected) => Assert.Equal(expected, CzechPresentationFormatter.Seats(count));

    [Fact]
    public void Public_and_organizer_dates_use_Czech_and_only_organizer_shows_zone()
    {
        var instant = new DateTimeOffset(2026, 8, 20, 17, 30, 0, TimeSpan.Zero);

        var publicValue = CzechPresentationFormatter.FormatPublicDateTime(instant, Prague);
        var organizerValue = CzechPresentationFormatter.FormatOrganizerDeadline(instant, Prague);

        Assert.Contains("19:30", publicValue);
        Assert.DoesNotContain("Europe/Prague", publicValue);
        Assert.EndsWith("(Europe/Prague)", organizerValue);
    }

    [Fact]
    public void Display_values_are_Czech()
    {
        Assert.Equal("Potvrzeno", CzechPresentationFormatter.DisplayInvitationStatus(InvitationStatus.Confirmed));
        Assert.Equal("Připraveno ke kontrole", CzechPresentationFormatter.CampaignState(EmailCampaignState.ReadyForReview));
        Assert.Equal("Operátor", CzechPresentationFormatter.Role("Operator"));
    }
}
