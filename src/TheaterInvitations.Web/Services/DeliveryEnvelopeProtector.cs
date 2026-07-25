using Microsoft.AspNetCore.DataProtection;

namespace TheaterInvitations.Web.Services;

public interface IDeliveryEnvelopeProtector
{
    byte[] Protect(string token);
}

public sealed class DeliveryEnvelopeProtector(IDataProtectionProvider provider) : IDeliveryEnvelopeProtector
{
    public const string Purpose = "TheaterInvitations.DeliveryEnvelope.v1";
    private readonly IDataProtector protector = provider.CreateProtector(Purpose);

    public byte[] Protect(string token) => System.Text.Encoding.UTF8.GetBytes(protector.Protect(token));
}
