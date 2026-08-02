using System.Net.Mail;

namespace TheaterInvitations.Web.Data;

public static class PartyEmailValidation
{
    public static string Normalize(string email)
    {
        var value = email?.Trim();
        if (!MailAddress.TryCreate(value, out var address) ||
            !address.Address.Equals(value, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Zadejte platnou e-mailovou adresu.", nameof(email));
        }

        return value;
    }
}
