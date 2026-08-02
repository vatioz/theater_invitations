namespace TheaterInvitations.Web.Data;

public sealed class EmailDailyAllowance
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public DateOnly DayUtc { get; set; }
    public int ReservedCount { get; set; }
    public uint Version { get; set; }
}
