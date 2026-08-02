using System.Globalization;
using TheaterInvitations.Domain;
using TheaterInvitations.Web.Data;

namespace TheaterInvitations.Web.Services;

public static class CzechPresentationFormatter
{
    public static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("cs-CZ");

    public static string FormatPublicDate(DateTimeOffset value, TimeZoneInfo timeZone) => TimeZoneInfo.ConvertTime(value, timeZone).ToString("D", Culture);
    public static string FormatPublicTime(DateTimeOffset value, TimeZoneInfo timeZone) => TimeZoneInfo.ConvertTime(value, timeZone).ToString("t", Culture);
    public static string FormatPublicDateTime(DateTimeOffset value, TimeZoneInfo timeZone) => TimeZoneInfo.ConvertTime(value, timeZone).ToString("f", Culture);
    public static string FormatOrganizerDeadline(DateTimeOffset value, TimeZoneInfo timeZone) => $"{FormatPublicDateTime(value, timeZone)} ({timeZone.Id})";

    public static string Seats(int count) => count switch
    {
        1 => "1 místo",
        > 1 and < 5 => $"{count} místa",
        _ => $"{count} míst"
    };

    public static string DisplayInvitationStatus(InvitationStatus value) => value switch
    {
        InvitationStatus.Pending => "Čeká na odpověď",
        InvitationStatus.Confirmed => "Potvrzeno",
        InvitationStatus.Declined => "Odmítnuto",
        InvitationStatus.Expired => "Platnost vypršela",
        _ => value.ToString()
    };

    public static string BatchState(InvitationBatchState value) => value switch
    {
        InvitationBatchState.Committed => "Potvrzeno",
        _ => value.ToString()
    };

    public static string CampaignType(EmailCampaignType value) => value switch
    {
        EmailCampaignType.InitialInvitation => "První pozvánka",
        EmailCampaignType.Reminder => "Připomenutí",
        EmailCampaignType.Resend => "Opětovné odeslání",
        _ => value.ToString()
    };

    public static string CampaignState(EmailCampaignState value) => value switch
    {
        EmailCampaignState.Draft => "Návrh",
        EmailCampaignState.ReadyForReview => "Připraveno ke kontrole",
        EmailCampaignState.Queued => "Ve frontě",
        EmailCampaignState.Sending => "Odesílá se",
        EmailCampaignState.Completed => "Dokončeno",
        EmailCampaignState.PartiallyFailed => "Částečně neúspěšné",
        EmailCampaignState.Failed => "Neúspěšné",
        EmailCampaignState.Invalidated => "Kontrola je neplatná",
        _ => value.ToString()
    };

    public static string TemplateState(EmailTemplateState value) => value switch
    {
        EmailTemplateState.Active => "Aktivní",
        EmailTemplateState.Retired => "Vyřazeno",
        _ => value.ToString()
    };

    public static string TemplateType(EmailTemplateType value) => value switch
    {
        EmailTemplateType.InitialInvitation => "První pozvánka",
        EmailTemplateType.Reminder => "Připomenutí",
        _ => value.ToString()
    };

    public static string DispatchState(EmailDispatchState? value) => value switch
    {
        null => "-",
        EmailDispatchState.Prepared => "Připraveno",
        EmailDispatchState.Queued => "Ve frontě",
        EmailDispatchState.Sending => "Odesílá se",
        EmailDispatchState.Accepted => "Přijato poskytovatelem",
        EmailDispatchState.Delivered => "Doručeno",
        EmailDispatchState.Failed => "Neúspěšné",
        EmailDispatchState.Bounced => "Nedoručitelné",
        EmailDispatchState.Complained => "Stížnost",
        EmailDispatchState.Suppressed => "Potlačeno",
        _ => value?.ToString() ?? "-"
    };

    public static string Role(string value) => value switch
    {
        "Operator" => "Operátor",
        "ElevatedOperator" => "Rozšířený operátor",
        _ => value
    };
}
