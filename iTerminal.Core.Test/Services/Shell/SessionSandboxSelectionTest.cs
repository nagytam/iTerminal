using iTerminal.Core.Services.Shell;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace iTerminal.Core.Services.Shell.Test;

/// <summary>
/// Which sandbox the session service offers and starts with: the saved choice when it is available, else none; a
/// switch is saved, refused for an unknown or unavailable name, and refused while a command runs. No shell is started.
/// </summary>
[TestClass]
public class SessionSandboxSelectionTest
{
    private sealed class StubSandbox(string name, bool available) : ISessionSandbox
    {
        public string Name => name;

        public bool IsAvailable => available;

        public SessionLaunch Wrap(SessionLaunch shell, string folder) => shell;
    }

    private static (LocalShellSessionService Service, Mock<ISettingsService> Settings) Create(string? saved)
    {
        var settings = new Mock<ISettingsService>();
        settings.Setup(s => s.Get(SessionSandboxes.SettingKey)).Returns(saved);
        var service = new LocalShellSessionService(settings.Object,
        [
            new NoSandbox(),
            new StubSandbox("bubblewrap", available: true),
            new StubSandbox("firejail", available: false),
        ]);
        return (service, settings);
    }

    [TestMethod]
    public void AvailableSandboxes_ListsOnlyTheAvailableOnes_AndNoneFirst()
    {
        var (service, _) = Create(saved: null);

        CollectionAssert.AreEqual(new[] { "none", "bubblewrap" }, service.AvailableSandboxes().ToArray());
    }

    [TestMethod]
    public void SelectedSandbox_IsTheSavedChoice_WhenItIsAvailable()
    {
        var (service, _) = Create("bubblewrap");

        Assert.AreEqual("bubblewrap", service.SelectedSandbox);
    }

    [TestMethod]
    public void SelectedSandbox_FallsBackToNone_WhenTheSavedChoiceIsNotAvailable_OrUnknown()
    {
        Assert.AreEqual("none", Create("firejail").Service.SelectedSandbox);
        Assert.AreEqual("none", Create("landlock").Service.SelectedSandbox);
        Assert.AreEqual("none", Create(saved: null).Service.SelectedSandbox);
    }

    [TestMethod]
    public void SwitchSandbox_SavesTheChoiceAsTheDefault()
    {
        var (service, settings) = Create(saved: null);

        Assert.IsTrue(service.SwitchSandbox("bubblewrap"));

        Assert.AreEqual("bubblewrap", service.SelectedSandbox);
        settings.Verify(s => s.Set(SessionSandboxes.SettingKey, "bubblewrap"), Times.Once);
    }

    [TestMethod]
    public void SwitchSandbox_RefusesAnUnknownOrUnavailableName_AndKeepsTheSelection()
    {
        var (service, settings) = Create(saved: null);

        Assert.IsFalse(service.SwitchSandbox("firejail"));
        Assert.IsFalse(service.SwitchSandbox("landlock"));

        Assert.AreEqual("none", service.SelectedSandbox);
        settings.Verify(s => s.Set(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [TestMethod]
    public void SwitchSandbox_ToTheSameChoice_IsAcceptedWithoutSaving()
    {
        var (service, settings) = Create(saved: "bubblewrap");

        Assert.IsTrue(service.SwitchSandbox("bubblewrap"));

        settings.Verify(s => s.Set(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }
}
