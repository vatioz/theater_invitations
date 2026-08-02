using System.Text;

namespace TheaterInvitations.Web.Data;

public static class PartyDataValidation
{
    public static string NormalizeName(string? name)
    {
        var value = name?.Trim();
        if (string.IsNullOrWhiteSpace(value) || value.Length > 200) throw new ArgumentException("Jméno musí být vyplněné a dlouhé nejvýše 200 znaků.", nameof(name));
        return value;
    }

    public static string? NormalizeCompany(string? company)
    {
        var value = string.IsNullOrWhiteSpace(company) ? null : company.Trim();
        if (value?.Length > 200) throw new ArgumentException("Společnost může mít nejvýše 200 znaků.", nameof(company));
        return value;
    }

    public static int NormalizePriority(string? priority)
    {
        if (string.IsNullOrWhiteSpace(priority)) return 3;
        if (!int.TryParse(priority.Trim(), out var value) || value is < 1 or > 3) throw new ArgumentException("Priorita musí být celé číslo 1, 2 nebo 3.", nameof(priority));
        return value;
    }

    public static string? NormalizePhone(string? phone)
    {
        var value = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();
        if (value is not null && (value.Length > 64 || value.Any(char.IsControl))) throw new ArgumentException("Telefon může mít nejvýše 64 znaků a nesmí obsahovat řídicí znaky.", nameof(phone));
        return value;
    }

    public static int NormalizeSeats(string? seats)
    {
        if (!int.TryParse(seats?.Trim(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var value) || value <= 0) throw new ArgumentException("Počet míst musí být kladné celé číslo.", nameof(seats));
        return value;
    }
}
