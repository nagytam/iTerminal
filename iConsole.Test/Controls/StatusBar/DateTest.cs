using iConsole.Controls.StatusBar;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls.StatusBar;

[TestClass]
public class DateTest
{
    private static Mock<IThemeService> CreateThemeServiceMock()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        return mockThemeService;
    }

    private static Mock<IDateTimeProviderService> CreateDateTimeProviderMock(DateTime now)
    {
        var mockDateTimeProviderService = new Mock<IDateTimeProviderService>();
        mockDateTimeProviderService.Setup(m => m.Now).Returns(now);
        mockDateTimeProviderService.Setup(m => m.UtcNow).Returns(now.ToUniversalTime());
        return mockDateTimeProviderService;
    }

    [TestMethod]
    public void Constructor_InitializesWithCorrectSize()
    {
        var mockThemeService = CreateThemeServiceMock();
        var mockDateTimeProviderService = CreateDateTimeProviderMock(new DateTime(2024, 01, 02, 03, 04, 05));

        using var date = new Date(mockThemeService.Object, mockDateTimeProviderService.Object);

        Assert.AreEqual(20, date.ViewSize.Width);
        Assert.AreEqual(1, date.ViewSize.Height);
    }

    [TestMethod]
    public void Constructor_InitializesText()
    {
        var mockThemeService = CreateThemeServiceMock();
        var mockDateTimeProviderService = CreateDateTimeProviderMock(new DateTime(2024, 01, 02, 03, 04, 05));

        using var date = new Date(mockThemeService.Object, mockDateTimeProviderService.Object);

        Assert.IsNotNull(date.Text);
    }

    [TestMethod]
    public void Text_ContainsDateTime()
    {
        var mockThemeService = CreateThemeServiceMock();
        var mockDateTimeProviderService = CreateDateTimeProviderMock(new DateTime(2024, 01, 02, 03, 04, 05));

        using var date = new Date(mockThemeService.Object, mockDateTimeProviderService.Object);

        Assert.IsTrue(date.Text.Contains("-"));
        Assert.IsTrue(date.Text.Contains(":"));
    }

    [TestMethod]
    public void Text_UpdatesOverTime()
    {
        var first = new DateTime(2024, 01, 02, 03, 04, 05);
        var second = first.AddSeconds(1);
        var mockThemeService = CreateThemeServiceMock();
        var mockDateTimeProviderService = new Mock<IDateTimeProviderService>();
        mockDateTimeProviderService.SetupSequence(m => m.Now)
            .Returns(first)
            .Returns(second);
        mockDateTimeProviderService.Setup(m => m.UtcNow).Returns(first.ToUniversalTime());

        using var date = new Date(mockThemeService.Object, mockDateTimeProviderService.Object);
        var firstText = date.Text;
        date.OnIdle();
        var secondText = date.Text;

        Assert.IsNotNull(firstText);
        Assert.IsNotNull(secondText);
        Assert.AreNotEqual(firstText, secondText);
    }

    [TestMethod]
    public void Dispose_DoesNotThrow()
    {
        var mockThemeService = CreateThemeServiceMock();
        var mockDateTimeProviderService = CreateDateTimeProviderMock(new DateTime(2024, 01, 02, 03, 04, 05));

        var date = new Date(mockThemeService.Object, mockDateTimeProviderService.Object);

        date.Dispose();
        // If no exception, test passes
    }

    [TestMethod]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        var mockThemeService = CreateThemeServiceMock();
        var mockDateTimeProviderService = CreateDateTimeProviderMock(new DateTime(2024, 01, 02, 03, 04, 05));

        var date = new Date(mockThemeService.Object, mockDateTimeProviderService.Object);

        date.Dispose();
        date.Dispose();
        // If no exception, test passes
    }

    [TestMethod]
    public void Text_IsInCorrectFormat()
    {
        var mockThemeService = CreateThemeServiceMock();
        var mockDateTimeProviderService = CreateDateTimeProviderMock(new DateTime(2024, 01, 02, 03, 04, 05));

        using var date = new Date(mockThemeService.Object, mockDateTimeProviderService.Object);

        // Format should be yyyy-MM-dd HH:mm:ss
        Assert.AreEqual(19, date.Text.Length); // yyyy-MM-dd HH:mm:ss is 19 characters
    }

    [TestMethod]
    public void Draw_RendersFormattedDate()
    {
        var mockThemeService = CreateThemeServiceMock();
        var mockDateTimeProviderService = CreateDateTimeProviderMock(new DateTime(2024, 06, 15, 12, 30, 45));
        var buffer = new MockBuffer(new Size(20, 1));

        using var date = new Date(mockThemeService.Object, mockDateTimeProviderService.Object)
        {
            Position = new Position(0, 0)
        };

        date.Draw(buffer);

        Assert.IsTrue(buffer.ContentAsString.Contains("2024-06-15"));
        Assert.IsTrue(buffer.ContentAsString.Contains("12:30:45"));
    }

    [TestMethod]
    public void Draw_TextPaddedToViewWidth()
    {
        var mockThemeService = CreateThemeServiceMock();
        var mockDateTimeProviderService = CreateDateTimeProviderMock(new DateTime(2024, 01, 02, 03, 04, 05));
        var buffer = new MockBuffer(new Size(20, 1));

        using var date = new Date(mockThemeService.Object, mockDateTimeProviderService.Object)
        {
            Position = new Position(0, 0)
        };

        date.Draw(buffer);

        var lines = buffer.ContentAsString.Split(Environment.NewLine);
        Assert.AreEqual(20, lines[0].Length);
    }

    [TestMethod]
    public void Draw_AfterOnIdle_ShowsUpdatedTime()
    {
        var first = new DateTime(2024, 01, 02, 03, 04, 05);
        var second = new DateTime(2024, 01, 02, 03, 04, 06);
        var mockThemeService = CreateThemeServiceMock();
        var mockDtp = new Mock<IDateTimeProviderService>();
        mockDtp.SetupSequence(m => m.Now)
            .Returns(first)
            .Returns(second);
        var buffer = new MockBuffer(new Size(20, 1));

        using var date = new Date(mockThemeService.Object, mockDtp.Object)
        {
            Position = new Position(0, 0)
        };
        date.OnIdle();
        date.Draw(buffer);

        Assert.IsTrue(buffer.ContentAsString.Contains("03:04:06"));
    }
}
