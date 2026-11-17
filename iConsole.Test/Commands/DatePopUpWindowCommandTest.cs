using Microsoft.VisualStudio.TestTools.UnitTesting;
using iConsole.Commands;
using iConsole.Controls.PopUpWindows;
using iConsole.Services;
using Moq;

namespace iConsole.Commands.Test;

[TestClass]
public class DatePopUpWindowCommandTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void TypeExists()
    {
        Assert.IsNotNull(typeof(DatePopUpWindowCommand));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Title_IsNotTheCalendarDialogsTitle()
    {
        // Ctrl+D opens the Calendar dialog under that name; this pop-up is a different command and must not share it.
        var command = new DatePopUpWindowCommand(new DatePopUpWindow(new Mock<IThemeService>().Object));

        Assert.AreEqual("Date pop-up", command.Title);
    }
}
