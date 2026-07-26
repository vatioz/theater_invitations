using Microsoft.AspNetCore.DataProtection;

namespace TheaterInvitations.Web.Services;

public interface IDeliveryEnvelopeProtector
{
    byte[] Protect(string token);
    string Unprotect(byte[] protectedToken);
}

public sealed class DeliveryEnvelopeProtector(IDataProtectionProvider provider) : IDeliveryEnvelopeProtector
{
    public const string Purpose = "TheaterInvitations.DeliveryEnvelope.v1";
    private readonly IDataProtector protector = provider.CreateProtector(Purpose);

    public byte[] Protect(string token) => System.Text.Encoding.UTF8.GetBytes(protector.Protect(token));
    public string Unprotect(byte[] protectedToken) => protector.Unprotect(System.Text.Encoding.UTF8.GetString(protectedToken));
}
