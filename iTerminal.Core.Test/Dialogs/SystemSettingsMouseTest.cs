using iConsole.Data;
using iConsole.Services;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Services.SystemSettings;
using Moq;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Test.Dialogs;

/// <summary>System settings with the mouse: the wheel moves the highlight, a click picks a row or opens a section.</summary>
[TestClass]
public class SystemSettingsMouseTest
{
    private static async Task<SystemSettingsDialog> OpenAsync(int variables = 6)
    {
        var service = new Mock<ISystemSettingsService>();
        service.SetupGet(s => s.IsElevated).Returns(true);
        service.SetupGet(s => s.SupportsTimeZone).Returns(true);
        service.SetupGet(s => s.CurrentTimeZoneId).Returns("Zone/B");
        service.Setup(s => s.SupportsEnvironment(It.IsAny<EnvironmentScope>())).Returns(true);
        service.Setup(s => s.GetComputerAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ComputerIdentity("DESK", "HOME", false, "My desk", true, true));
        service.Setup(s => s.GetTimeZones()).Returns([new("Zone/A", "(UTC+00:00) Alpha"), new("Zone/B", "(UTC+01:00) Beta"), new("Zone/C", "(UTC+02:00) Gamma")]);
        service.Setup(s => s.GetEnvironmentAsync(EnvironmentScope.User, It.IsAny<CancellationToken>()))
            .ReturnsAsync([.. Enumerable.Range(0, variables).Select(i => new SystemEnvironmentVariable("VAR" + i, "value" + i))]);
        var dialog = new SystemSettingsDialog(service.Object, Mock.Of<IBeepService>(), new DateTimeProviderService(), TestTheme());
        await ShowAsync(dialog);
        await dialog.LoadTask;
        return dialog;
    }

    // The strip is the first row inside the border; the rows of the body start three rows below it.
    private static MouseEvent OnStrip(SystemSettingsDialog dialog, int column) => new()
    {
        Position = new Position(dialog.AbsolutePosition.X + 1 + column, dialog.AbsolutePosition.Y + 1),
        LeftClicked = true,
    };

    private static MouseEvent OnRow(SystemSettingsDialog dialog, int row, int wheel = 0) => new()
    {
        Position = new Position(dialog.AbsolutePosition.X + 6, dialog.AbsolutePosition.Y + 1 + 3 + row),
        LeftClicked = wheel == 0,
        WheelDelta = wheel,
    };

    [TestMethod]
    public async Task AClickOnASectionsName_OpensIt()
    {
        var dialog = await OpenAsync();

        // " Computer " is ten columns, two spaces, then " Environment " and " Time zone ".
        Assert.IsTrue(await dialog.HandleMouseEventAsync(OnStrip(dialog, 14)));
        Assert.AreEqual(SystemSettingsDialog.SettingsSection.Environment, dialog.Section);

        await dialog.HandleMouseEventAsync(OnStrip(dialog, 30));
        Assert.AreEqual(SystemSettingsDialog.SettingsSection.TimeZone, dialog.Section);

        await dialog.HandleMouseEventAsync(OnStrip(dialog, 3));
        Assert.AreEqual(SystemSettingsDialog.SettingsSection.Computer, dialog.Section);
    }

    [TestMethod]
    public async Task AClickBetweenTheNames_OrBeyondThem_OpensNothing()
    {
        var dialog = await OpenAsync();

        await dialog.HandleMouseEventAsync(OnStrip(dialog, 11));
        await dialog.HandleMouseEventAsync(OnStrip(dialog, 60));

        Assert.AreEqual(SystemSettingsDialog.SettingsSection.Computer, dialog.Section);
    }

    [TestMethod]
    public async Task AClickOnARow_Highlights_It()
    {
        var dialog = await OpenAsync();
        await dialog.HandleMouseEventAsync(OnStrip(dialog, 14));

        Assert.IsTrue(await dialog.HandleMouseEventAsync(OnRow(dialog, 2)));

        Assert.AreEqual(2, dialog.Cursor);
        await dialog.HandleMouseEventAsync(OnRow(dialog, 40));
        Assert.AreEqual(2, dialog.Cursor, "a click below the list changes nothing");
    }

    [TestMethod]
    public async Task AClickOnTheComputerRows_Highlights_Them()
    {
        var dialog = await OpenAsync();

        await dialog.HandleMouseEventAsync(OnRow(dialog, 1));

        Assert.AreEqual(1, dialog.Cursor);
    }

    [TestMethod]
    public async Task TheWheelMovesTheHighlightThreeRows_AndStopsAtTheEnds()
    {
        var dialog = await OpenAsync(variables: 8);
        await dialog.HandleMouseEventAsync(OnStrip(dialog, 14));

        await dialog.HandleMouseEventAsync(OnRow(dialog, 0, wheel: -1));
        Assert.AreEqual(3, dialog.Cursor);
        await dialog.HandleMouseEventAsync(OnRow(dialog, 0, wheel: -1));
        await dialog.HandleMouseEventAsync(OnRow(dialog, 0, wheel: -1));
        Assert.AreEqual(7, dialog.Cursor);
        await dialog.HandleMouseEventAsync(OnRow(dialog, 0, wheel: 1));
        Assert.AreEqual(4, dialog.Cursor);
    }

    [TestMethod]
    public async Task TheMouseLeavesTheDialogAloneWhileAValueIsBeingTyped()
    {
        var dialog = await OpenAsync();
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter));
        Assert.IsTrue(dialog.InputActive);

        await dialog.HandleMouseEventAsync(OnStrip(dialog, 14));
        await dialog.HandleMouseEventAsync(OnRow(dialog, 2));

        Assert.AreEqual(SystemSettingsDialog.SettingsSection.Computer, dialog.Section);
        Assert.AreEqual(0, dialog.Cursor);
    }

    [TestMethod]
    public async Task AWheelOutsideTheDialogIsNotTheDialogs()
    {
        var dialog = await OpenAsync();

        Assert.IsFalse(await dialog.HandleMouseEventAsync(new MouseEvent { Position = new Position(0, 0), WheelDelta = -1 }));
    }
}
