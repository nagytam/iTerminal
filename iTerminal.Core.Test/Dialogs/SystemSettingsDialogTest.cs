using iConsole.Services;
using iTerminal.Core.Services.SystemSettings;
using Moq;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Dialogs.Test;

[TestClass]
public class SystemSettingsDialogTest
{
    private static Mock<ISystemSettingsService> Service(bool elevated = true)
    {
        var service = new Mock<ISystemSettingsService>();
        service.SetupGet(s => s.IsElevated).Returns(elevated);
        service.SetupGet(s => s.SupportsTimeZone).Returns(true);
        service.SetupGet(s => s.CurrentTimeZoneId).Returns("Zone/B");
        service.Setup(s => s.SupportsEnvironment(It.IsAny<EnvironmentScope>())).Returns(true);
        service.Setup(s => s.GetComputerAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ComputerIdentity("DESK", "HOME", false, "My desk", true, true));
        service.Setup(s => s.GetTimeZones()).Returns([new("Zone/A", "(UTC+00:00) Alpha"), new("Zone/B", "(UTC+01:00) Beta"), new("Zone/C", "(UTC+02:00) Gamma")]);
        service.Setup(s => s.GetEnvironmentAsync(EnvironmentScope.User, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new SystemEnvironmentVariable("EDITOR", "vim"), new SystemEnvironmentVariable("PAGER", "less")]);
        service.Setup(s => s.GetEnvironmentAsync(EnvironmentScope.Machine, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new SystemEnvironmentVariable("JAVA_HOME", "/opt/java")]);
        service.Setup(s => s.SetEnvironmentVariableAsync(It.IsAny<EnvironmentScope>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SystemSettingResult.Ok("saved"));
        service.Setup(s => s.RemoveEnvironmentVariableAsync(It.IsAny<EnvironmentScope>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SystemSettingResult.Ok("removed"));
        service.Setup(s => s.SetComputerNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SystemSettingResult.Ok("renamed", restartRequired: true));
        service.Setup(s => s.SetTimeZoneAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SystemSettingResult.Ok("zone set"));
        return service;
    }

    private static async Task<(SystemSettingsDialog Dialog, iConsole.Controls.Control Root)> OpenAsync(Mock<ISystemSettingsService> service)
    {
        var dialog = new SystemSettingsDialog(service.Object, Mock.Of<IBeepService>(), new DateTimeProviderService(), TestTheme());
        var root = await ShowAsync(dialog);
        await dialog.LoadTask;
        return (dialog, root);
    }

    private static async Task PressAsync(SystemSettingsDialog dialog, ConsoleKey key, char keyChar = '\0')
    {
        await dialog.HandleKeyEventAsync(Key(key, keyChar));
        await dialog.PendingOperation;
    }

    [TestMethod]
    public async Task OpensOnTheComputerWithItsNameWorkgroupAndDescription()
    {
        var (dialog, _) = await OpenAsync(Service());

        Assert.AreEqual(SystemSettingsDialog.SettingsSection.Computer, dialog.Section);
        StringAssert.Contains(dialog.Text, "DESK");
        StringAssert.Contains(dialog.Text, "Workgroup");
        StringAssert.Contains(dialog.Text, "HOME");
        StringAssert.Contains(dialog.Text, "My desk");
        StringAssert.Contains(dialog.Text, "Running as administrator");
    }

    [TestMethod]
    public async Task WithoutAdministratorRightsItSaysSoAtTheTop()
    {
        var (dialog, _) = await OpenAsync(Service(elevated: false));

        StringAssert.Contains(dialog.Text, "Not administrator");
        StringAssert.Contains(dialog.Text, "Ctrl+Shift+A");
    }

    [TestMethod]
    public async Task TheDialogDrawsItsRowsOnScreen()
    {
        var (dialog, root) = await OpenAsync(Service());

        var screen = Render(root);

        AssertScreenContains(screen, "System Settings");
        AssertScreenContains(screen, "Computer name");
        AssertScreenContains(screen, "DESK");
    }

    [TestMethod]
    public async Task RightArrowMovesThroughTheSectionsAndWrapsAround()
    {
        var (dialog, _) = await OpenAsync(Service());

        await PressAsync(dialog, ConsoleKey.RightArrow);
        Assert.AreEqual(SystemSettingsDialog.SettingsSection.Environment, dialog.Section);
        StringAssert.Contains(dialog.Text, "EDITOR");
        StringAssert.Contains(dialog.Text, "vim");

        await PressAsync(dialog, ConsoleKey.RightArrow);
        Assert.AreEqual(SystemSettingsDialog.SettingsSection.TimeZone, dialog.Section);
        StringAssert.Contains(dialog.Text, "Beta");

        await PressAsync(dialog, ConsoleKey.RightArrow);
        Assert.AreEqual(SystemSettingsDialog.SettingsSection.Computer, dialog.Section);
    }

    [TestMethod]
    public async Task EnterOnTheNameOpensTheInputAndEnterAgainRenames()
    {
        var service = Service();
        var (dialog, _) = await OpenAsync(service);

        await PressAsync(dialog, ConsoleKey.Enter);
        Assert.IsTrue(dialog.InputActive);
        for (var i = 0; i < "DESK".Length; i++)
        {
            await PressAsync(dialog, ConsoleKey.Backspace);
        }

        await TypeAsync(dialog, "LAPTOP");
        await PressAsync(dialog, ConsoleKey.Enter);

        Assert.IsFalse(dialog.InputActive);
        service.Verify(s => s.SetComputerNameAsync("LAPTOP", It.IsAny<CancellationToken>()), Times.Once);
        StringAssert.Contains(dialog.Message, "renamed");
    }

    [TestMethod]
    public async Task EscapeInTheInputCancelsTheInputAndNotTheDialog()
    {
        var service = Service();
        var (dialog, root) = await OpenAsync(service);

        await PressAsync(dialog, ConsoleKey.Enter);
        await PressAsync(dialog, ConsoleKey.Escape);

        Assert.IsFalse(dialog.InputActive);
        Assert.IsTrue(root.Controls.AsSnapshot().Contains(dialog), "the dialog is still up");
        service.Verify(s => s.SetComputerNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

        await PressAsync(dialog, ConsoleKey.Escape);
        Assert.IsFalse(root.Controls.AsSnapshot().Contains(dialog), "the next Esc closes it");
    }

    [TestMethod]
    public async Task ARefusedChangeShowsTheReasonAndLeavesTheDialogUsable()
    {
        var service = Service();
        service.Setup(s => s.SetComputerNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SystemSettingResult.Fail("A computer name cannot be made of digits only."));
        var (dialog, _) = await OpenAsync(service);

        await PressAsync(dialog, ConsoleKey.Enter);
        await PressAsync(dialog, ConsoleKey.Enter);

        StringAssert.Contains(dialog.Message, "digits only");
        StringAssert.Contains(dialog.Text, "DESK", "the name shown is still the system's, not the one typed");
    }

    [TestMethod]
    public async Task InsertAsksForANameThenAValueAndAddsTheVariableToTheScopeInView()
    {
        var service = Service();
        var (dialog, _) = await OpenAsync(service);
        await PressAsync(dialog, ConsoleKey.RightArrow);

        await PressAsync(dialog, ConsoleKey.Insert);
        await TypeAsync(dialog, "MY_VAR");
        await PressAsync(dialog, ConsoleKey.Enter);
        Assert.IsTrue(dialog.InputActive, "the value is asked next");
        await TypeAsync(dialog, "42");
        await PressAsync(dialog, ConsoleKey.Enter);

        service.Verify(s => s.SetEnvironmentVariableAsync(EnvironmentScope.User, "MY_VAR", "42", It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task ABadVariableNameIsRefusedBeforeTheValueIsAsked()
    {
        var (dialog, _) = await OpenAsync(Service());
        await PressAsync(dialog, ConsoleKey.RightArrow);

        await PressAsync(dialog, ConsoleKey.Insert);
        await TypeAsync(dialog, "a=b");
        await PressAsync(dialog, ConsoleKey.Enter);

        Assert.IsFalse(dialog.InputActive);
        StringAssert.Contains(dialog.Message, "=");
    }

    [TestMethod]
    public async Task DeleteAsksFirstAndOnlyYRemovesTheVariable()
    {
        var service = Service();
        var (dialog, _) = await OpenAsync(service);
        await PressAsync(dialog, ConsoleKey.RightArrow);

        await PressAsync(dialog, ConsoleKey.Delete);
        StringAssert.Contains(dialog.Text, "Remove EDITOR?");
        await PressAsync(dialog, ConsoleKey.Q, 'q');
        StringAssert.Contains(dialog.Text, "Remove EDITOR?", "another key is no answer");
        await PressAsync(dialog, ConsoleKey.N, 'n');
        service.Verify(s => s.RemoveEnvironmentVariableAsync(It.IsAny<EnvironmentScope>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

        await PressAsync(dialog, ConsoleKey.Delete);
        await PressAsync(dialog, ConsoleKey.Y, 'y');

        service.Verify(s => s.RemoveEnvironmentVariableAsync(EnvironmentScope.User, "EDITOR", It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task SSwitchesBetweenTheUserAndTheMachineVariables()
    {
        var (dialog, _) = await OpenAsync(Service());
        await PressAsync(dialog, ConsoleKey.RightArrow);

        await PressAsync(dialog, ConsoleKey.S, 's');

        Assert.AreEqual(EnvironmentScope.Machine, dialog.Scope);
        StringAssert.Contains(dialog.Text, "JAVA_HOME");
        Assert.IsFalse(dialog.Text.Contains("EDITOR"), "the user's variables are gone from view");
    }

    [TestMethod]
    public async Task TheTimeZoneListStartsOnTheCurrentZoneAndEnterAppliesAnother()
    {
        var service = Service();
        var (dialog, _) = await OpenAsync(service);
        await PressAsync(dialog, ConsoleKey.RightArrow);
        await PressAsync(dialog, ConsoleKey.RightArrow);

        Assert.AreEqual(1, dialog.Cursor, "Beta is the current zone");
        await PressAsync(dialog, ConsoleKey.Enter);
        service.Verify(s => s.SetTimeZoneAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        StringAssert.Contains(dialog.Message, "current");

        await PressAsync(dialog, ConsoleKey.DownArrow);
        await PressAsync(dialog, ConsoleKey.Enter);

        service.Verify(s => s.SetTimeZoneAsync("Zone/C", It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task TheCursorStaysInsideTheList()
    {
        var (dialog, _) = await OpenAsync(Service());
        await PressAsync(dialog, ConsoleKey.RightArrow);

        await PressAsync(dialog, ConsoleKey.End);
        Assert.AreEqual(1, dialog.Cursor);
        await PressAsync(dialog, ConsoleKey.DownArrow);
        Assert.AreEqual(1, dialog.Cursor);
        await PressAsync(dialog, ConsoleKey.Home);
        await PressAsync(dialog, ConsoleKey.UpArrow);
        Assert.AreEqual(0, dialog.Cursor);
    }

    [TestMethod]
    public async Task AFailedRead_IsSaidOnTheDialog_NotLeftAsReading()
    {
        var service = Service();
        service.Setup(s => s.GetComputerAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("access refused"));
        var dialog = new SystemSettingsDialog(service.Object, Mock.Of<IBeepService>(), new DateTimeProviderService(), TestTheme());
        await ShowAsync(dialog);

        await dialog.LoadTask;

        AssertScreenContains(Render(dialog), "Could not read the system settings: access refused");
        AssertScreenDoesNotContain(Render(dialog), "Reading");
    }
}
