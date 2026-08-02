using System.Net;
using System.Text.RegularExpressions;

namespace TheaterInvitations.Web.Services;

public sealed class EmailTemplateRenderer
{
    private static readonly Regex Placeholder = new("\\{\\{([a-z_]+)\\}\\}", RegexOptions.Compiled);
    private static readonly HashSet<string> Supported = new(StringComparer.Ordinal)
    {
        "guest_name", "allocation", "event_name", "event_date", "doors_time", "start_time", "venue_name", "venue_address", "deadline", "rsvp_url", "support_email"
    };

    public static void Validate(string subject, string htmlBody, string plainTextBody)
    {
        foreach (var match in Placeholder.Matches(subject).Cast<Match>().Concat(Placeholder.Matches(htmlBody).Cast<Match>()).Concat(Placeholder.Matches(plainTextBody).Cast<Match>()))
        {
            if (!Supported.Contains(match.Groups[1].Value)) throw new ArgumentException($"Nepodporovaný zástupný symbol e-mailu '{{{{{match.Groups[1].Value}}}}}'.");
        }
    }

    public EmailRenderResult Render(string subject, string htmlBody, string plainTextBody, EmailRenderData data)
    {
        string Resolve(string name, bool html) => name switch
        {
            "guest_name" => Encode(data.GuestName, html),
            "allocation" => Encode(data.Allocation, html),
            "event_name" => Encode(data.EventName, html),
            "event_date" => Encode(data.EventDate, html),
            "doors_time" => Encode(data.DoorsTime, html),
            "start_time" => Encode(data.StartTime, html),
            "venue_name" => Encode(data.VenueName, html),
            "venue_address" => Encode(data.VenueAddress, html),
            "deadline" => Encode(data.Deadline, html),
            "rsvp_url" => Encode(data.RsvpUrl, html),
            "support_email" => Encode(data.SupportEmail, html),
            _ => string.Empty
        };
        string Replace(string input, bool html) => Placeholder.Replace(input, match => Resolve(match.Groups[1].Value, html));
        return new EmailRenderResult(Replace(subject, false), Replace(htmlBody, true), Replace(plainTextBody, false));
    }

    private static string Encode(string value, bool html) => html ? WebUtility.HtmlEncode(value) : value;
}

public sealed record EmailRenderData(string GuestName, string Allocation, string EventName, string EventDate, string DoorsTime, string StartTime, string VenueName, string VenueAddress, string Deadline, string RsvpUrl, string SupportEmail);
public sealed record EmailRenderResult(string Subject, string HtmlBody, string PlainTextBody);
