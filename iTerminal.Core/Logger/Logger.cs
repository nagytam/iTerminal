using iTerminal.Core.Services;
using Serilog;
using Serilog.Events;

namespace iTerminal.Core.Logger;

public class Logger : ILogger
{
    private string _logFileName;
    private ILogger _logger { get; set; }

    public Logger(ISettingsService settingsService)
        : this(settingsService, null)
    {
    }

    /// <summary>
    /// A relative log file name is kept in <paramref name="directory"/> (the app's data folder), not in
    /// whatever folder the program was started from, where it showed up in the user's file panels.
    /// </summary>
    public Logger(ISettingsService settingsService, string? directory)
    {
        settingsService ??= NullSettingsService.Instance;

        _logFileName = settingsService.Get(nameof(_logFileName)) ?? @"iTerminal.log";
        settingsService.Set(nameof(_logFileName), _logFileName);

        string path = directory is { Length: > 0 } && !Path.IsPathRooted(_logFileName)
            ? Path.Combine(directory, _logFileName)
            : _logFileName;

        _logger = new LoggerConfiguration()
            .WriteTo.File(path)
            .CreateLogger();
    }

    public void Write(LogEvent logEvent)
    {
        _logger.Write(logEvent);
    }
}
