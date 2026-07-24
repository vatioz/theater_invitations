namespace TheaterInvitations.Web.Data;

public sealed class EventConfiguration
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public int Capacity { get; set; }
    public string TimeZoneId { get; set; } = string.Empty;
    public string SupportEmail { get; set; } = string.Empty;
    public int AccessibilityTextLimit { get; set; }
    public bool IsRsvpLocked { get; set; }
    public DateTimeOffset? LockedAtUtc { get; set; }
    public string? LockedBy { get; set; }
    public uint Version { get; set; }
}
