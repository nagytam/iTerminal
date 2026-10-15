using System;
using Moq;
using iConsole.Services;
using iConsole.Drivers;
using iConsole.Interfaces;

namespace iConsole.Test.Common;

public static class TestHelpers
{
    public static Mock<IThemeService> CreateThemeServiceMock()
    {
        var m = new Mock<IThemeService>();
        m.SetupGet(t => t.CurrentColorScheme).Returns(new iConsole.Display.ColorScheme());
        return m;
    }

    public static Mock<IBeepService> CreateBeepServiceMock()
    {
        var m = new Mock<IBeepService>();
        return m;
    }

    public static Mock<IDateTimeProviderService> CreateDateTimeProviderMock(DateTime now)
    {
        var m = new Mock<IDateTimeProviderService>();
        m.SetupGet(d => d.Now).Returns(now);
        m.SetupGet(d => d.UtcNow).Returns(now.ToUniversalTime());
        return m;
    }

    public static Mock<IFileSystem> CreateFileSystemMock(byte[]? content = null)
    {
        var m = new Mock<IFileSystem>();
        m.Setup(f => f.ExistsFile(It.IsAny<iConsole.Data.Url>())).Returns(content != null);
        if (content != null)
        {
            var mem = new iConsole.Test.Core.MockMemoryFile(content);
            m.Setup(f => f.ReadFileAsync(It.IsAny<iConsole.Data.Url>())).ReturnsAsync(mem);
        }
        return m;
    }

    public static iConsole.Test.Core.MockBuffer CreateBuffer(int width, int height) => new iConsole.Test.Core.MockBuffer(new iConsole.Data.Size(width, height));
}
