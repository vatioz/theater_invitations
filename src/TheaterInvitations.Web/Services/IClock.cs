namespace TheaterInvitations.Web.Services;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
