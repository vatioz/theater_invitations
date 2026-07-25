namespace TheaterInvitations.Web.Data;

public sealed class ProtectedDeliveryEnvelope
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid PartyId { get; init; }
    public Guid TokenId { get; init; }
    public byte[] ProtectedToken { get; init; } = Array.Empty<byte>();
    public DateTimeOffset CreatedAtUtc { get; init; }
    public string ProtectionPurpose { get; init; } = string.Empty;
}
