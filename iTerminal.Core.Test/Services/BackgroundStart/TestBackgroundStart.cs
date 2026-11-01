using iTerminal.Core.Services;
using iTerminal.Core.Services.BackgroundStart;
using Moq;

namespace iTerminal.Core.Test.Services.BackgroundStart;

/// <summary>A registry whose every service is configured with <paramref name="mode"/>, for tests of the services that use it.</summary>
internal static class TestBackgroundStart
{
    public static BackgroundStartService Configured(BackgroundStartMode mode, TimeSpan? delayedStart = null)
    {
        Mock<ISettingsService> settings = new();
        settings.Setup(service => service.Get(It.IsAny<string>())).Returns(mode.ToString());
        return new BackgroundStartService(settings.Object, delayedStart);
    }

    public static BackgroundStartService Unset(TimeSpan? delayedStart = null)
    {
        Mock<ISettingsService> settings = new();
        settings.Setup(service => service.Get(It.IsAny<string>())).Returns((string?)null);
        return new BackgroundStartService(settings.Object, delayedStart);
    }
}
