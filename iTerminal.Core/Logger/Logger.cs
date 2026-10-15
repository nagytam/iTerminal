using iTerminal.Core.Services;
using Serilog;
using Serilog.Events;

namespace iTerminal.Core.Logger;

public class Logger : ILogger
{
    private string _logFileName;
    private ILogger _logger { get; set; }

    public Logger(ISettingsService settingsService)
    {
        settingsService ??= NullSettingsService.Instance;

        _logFileName = settingsService.Get(nameof(_logFileName)) ?? @"iTerminal.log";
        settingsService.Set(nameof(_logFileName), _logFileName);

        _logger = new LoggerConfiguration()
            .WriteTo.File(_logFileName)
            .CreateLogger();
    }

    public void Write(LogEvent logEvent)
    {
        _logger.Write(logEvent);
    }
}
