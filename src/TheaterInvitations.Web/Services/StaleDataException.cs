namespace TheaterInvitations.Web.Services;

public sealed class StaleDataException(string message) : InvalidOperationException(message);
