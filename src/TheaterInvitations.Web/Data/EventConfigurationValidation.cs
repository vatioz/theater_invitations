using System.Net.Mail;

namespace TheaterInvitations.Web.Data;

public static class EventConfigurationValidation
{
    public static void ValidatePublicDetails(EventConfiguration configuration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configuration.EventName);
        ArgumentException.ThrowIfNullOrWhiteSpace(configuration.VenueName);
        ArgumentException.ThrowIfNullOrWhiteSpace(configuration.VenueAddress);
        ArgumentException.ThrowIfNullOrWhiteSpace(configuration.TimeZoneId);

        ValidateTimeZone(configuration.TimeZoneId);
        ValidateEventTimes(configuration);
    }

    public static void ValidateTimeZone(string timeZoneId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(timeZoneId);
        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId.Trim());
        }
        catch (TimeZoneNotFoundException exception)
        {
            throw new ArgumentException("Enter a valid event time-zone ID.", nameof(timeZoneId), exception);
        }
        catch (InvalidTimeZoneException exception)
        {
            throw new ArgumentException("Enter a valid event time-zone ID.", nameof(timeZoneId), exception);
        }
    }

    public static void ValidateEventTimes(EventConfiguration configuration)
    {
        if (configuration.DoorsAtUtc != default && configuration.StartsAtUtc != default && configuration.DoorsAtUtc > configuration.StartsAtUtc)
        {
            throw new InvalidOperationException("Event doors time must not be later than the start time.");
        }
    }

    public static DateTimeOffset ToUtc(DateTime localTime, string timeZoneId)
    {
        ValidateTimeZone(timeZoneId);
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId.Trim());
        var unspecified = DateTime.SpecifyKind(localTime, DateTimeKind.Unspecified);
        if (timeZone.IsInvalidTime(unspecified) || timeZone.IsAmbiguousTime(unspecified))
        {
            throw new ArgumentException("Choose an unambiguous local event time.");
        }

        return new DateTimeOffset(unspecified, timeZone.GetUtcOffset(unspecified)).ToUniversalTime();
    }

    public static string NormalizeSupportEmail(string email, bool isDevelopment)
    {
        if (!MailAddress.TryCreate(email?.Trim(), out var address))
        {
            throw new ArgumentException("Enter a valid support email address.", nameof(email));
        }

        var domain = address.Host;
        if (!isDevelopment &&
            (domain.Equals("example", StringComparison.OrdinalIgnoreCase) ||
             domain.EndsWith(".example", StringComparison.OrdinalIgnoreCase) ||
             address.Address.Equals("rsvp@example.test", StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("Configure an approved production support email address.", nameof(email));
        }

        return address.Address;
    }

}
