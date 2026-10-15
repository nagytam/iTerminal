namespace iConsole.Services;

public interface IDateTimeProviderService
{
    DateTime Now { get; }
    DateTime UtcNow { get; }
}