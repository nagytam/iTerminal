using iConsole.Controls.StatusBar;
using iConsole.Data;
using iConsole.Display;
using iConsole.Model;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls.StatusBar;

[TestClass]
public class SearchResultsIndicatorTest
{
    [TestMethod]
    public void Constructor_CreatesInstance()
    {
        var mockThemeService = new Mock<IThemeService>();
        var searchModel = new SearchModel();

        var indicator = new SearchResultsIndicator(searchModel, mockThemeService.Object);

        Assert.IsNotNull(indicator);
    }

    [TestMethod]
    public void Constructor_SetsDefaultViewSize()
    {
        var mockThemeService = new Mock<IThemeService>();
        var searchModel = new SearchModel();

        var indicator = new SearchResultsIndicator(searchModel, mockThemeService.Object);

        Assert.AreEqual(10, indicator.ViewSize.Width);
        Assert.AreEqual(1, indicator.ViewSize.Height);
    }

    [TestMethod]
    public void Draw_ShowsSearchHitCount()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var searchModel = new SearchModel();
        var buffer = new MockBuffer(new Size(20, 1));
        var indicator = new SearchResultsIndicator(searchModel, mockThemeService.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(20, 1)
        };

        indicator.Draw(buffer);

        Assert.IsTrue(buffer.ContentAsString.Contains("Search:") || buffer.ContentAsString.Contains("0"));
    }

    [TestMethod]
    public void Draw_ShowsCorrectCountWhenHitsAdded()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var searchModel = new SearchModel();
        searchModel.Hits.Add(new Position(0, 0));
        searchModel.Hits.Add(new Position(1, 0));
        var buffer = new MockBuffer(new Size(30, 1));
        var indicator = new SearchResultsIndicator(searchModel, mockThemeService.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(30, 1)
        };

        indicator.Draw(buffer);

        Assert.IsTrue(buffer.ContentAsString.Contains("2"));
    }

    [TestMethod]
    public void PropertyChanged_OnSearchModel_TriggersStateChange()
    {
        var mockThemeService = new Mock<IThemeService>();
        var searchModel = new SearchModel();
        var indicator = new SearchResultsIndicator(searchModel, mockThemeService.Object);
        indicator.HasStateChanged = false;

        searchModel.Search = "test";

        Assert.IsTrue(indicator.HasStateChanged);
    }

    [TestMethod]
    public void Indicator_IsInstanceOfStatusBarItem()
    {
        var mockThemeService = new Mock<IThemeService>();
        var searchModel = new SearchModel();

        var indicator = new SearchResultsIndicator(searchModel, mockThemeService.Object);

        Assert.IsInstanceOfType(indicator, typeof(StatusBarItem));
    }
}
