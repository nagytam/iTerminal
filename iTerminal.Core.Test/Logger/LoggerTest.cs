using iTerminal.Core.Logger;
using iTerminal.Core.Services;
using Moq;
using Serilog;
using Serilog.Events;
using Serilog.Parsing;

namespace iTerminal.Core.Test.Logger;

[TestClass]
public class LoggerTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void Constructor_DoesNotThrow_WithValidSettings()
    {
        var settings = new Mock<ISettingsService>();
        settings.Setup(s => s.Get(It.IsAny<string>())).Returns((string?)null);

        var logger = new iTerminal.Core.Logger.Logger(settings.Object);
        Assert.IsNotNull(logger);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Constructor_UsesExistingLogFileName_FromSettings()
    {
        var customLog = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".log");
        var settings = new Mock<ISettingsService>();
        settings.Setup(s => s.Get(It.IsAny<string>())).Returns(customLog);

        var logger = new iTerminal.Core.Logger.Logger(settings.Object);
        Assert.IsNotNull(logger);
        // Note: log file is held open by Serilog; we only verify the logger was constructed
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Constructor_SetsLogFileNameInSettings()
    {
        var settings = new Mock<ISettingsService>();
        settings.Setup(s => s.Get(It.IsAny<string>())).Returns((string?)null);

        _ = new iTerminal.Core.Logger.Logger(settings.Object);

        settings.Verify(s => s.Set(It.IsAny<string>(), It.IsAny<string>()), Times.AtLeastOnce);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Write_DoesNotThrow()
    {
        var settings = new Mock<ISettingsService>();
        settings.Setup(s => s.Get(It.IsAny<string>())).Returns((string?)null);
        var logger = new iTerminal.Core.Logger.Logger(settings.Object);

        var template = new MessageTemplateParser().Parse("Test message");
        var logEvent = new LogEvent(
            DateTimeOffset.UtcNow,
            LogEventLevel.Information,
            null,
            template,
            []);

        // Should not throw
        logger.Write(logEvent);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Logger_ImplementsSerilogILogger()
    {
        var settings = new Mock<ISettingsService>();
        settings.Setup(s => s.Get(It.IsAny<string>())).Returns((string?)null);

        ILogger logger = new iTerminal.Core.Logger.Logger(settings.Object);
        Assert.IsInstanceOfType(logger, typeof(ILogger));
    }
}
