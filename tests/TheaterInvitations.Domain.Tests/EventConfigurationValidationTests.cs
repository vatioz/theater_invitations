using TheaterInvitations.Web.Data;

namespace TheaterInvitations.Domain.Tests;

public sealed class EventConfigurationValidationTests
{
    [Fact]
    public void Public_details_require_complete_ordered_configuration()
    {
        var configuration = ValidConfiguration();

        EventConfigurationValidation.ValidatePublicDetails(configuration);

        configuration.EventName = string.Empty;
        Assert.Throws<ArgumentException>(() => EventConfigurationValidation.ValidatePublicDetails(configuration));
        configuration.EventName = "Theater Gala";
        configuration.DoorsAtUtc = configuration.StartsAtUtc.AddMinutes(1);
        Assert.Throws<InvalidOperationException>(() => EventConfigurationValidation.ValidatePublicDetails(configuration));
    }

    [Fact]
    public void Support_email_rules_allow_development_placeholder_but_reject_it_in_production()
    {
        Assert.Equal("rsvp@example.test", EventConfigurationValidation.NormalizeSupportEmail(" rsvp@example.test ", true));
        Assert.Throws<ArgumentException>(() => EventConfigurationValidation.NormalizeSupportEmail("rsvp@example.test", false));
        Assert.Throws<ArgumentException>(() => EventConfigurationValidation.NormalizeSupportEmail("help@events.example", false));
        Assert.Throws<ArgumentException>(() => EventConfigurationValidation.NormalizeSupportEmail("invalid", false));
        Assert.Equal("help@theater.org", EventConfigurationValidation.NormalizeSupportEmail("help@theater.org", false));
    }

    private static EventConfiguration ValidConfiguration() => new()
    {
        EventName = "Theater Gala",
        DoorsAtUtc = new DateTimeOffset(2026, 8, 1, 16, 0, 0, TimeSpan.Zero),
        StartsAtUtc = new DateTimeOffset(2026, 8, 1, 17, 0, 0, TimeSpan.Zero),
        VenueName = "Main Theater",
        VenueAddress = "1 Theater Street",
        TimeZoneId = "Europe/Prague"
    };
}
