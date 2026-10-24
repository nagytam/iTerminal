using iTerminal.Core.Jobs;
using iTerminal.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace iTerminal.Core.Jobs.Test;

/// <summary>Settings are saved when they changed, and once more on the way out; a save that fails does not take the job down.</summary>
[TestClass]
public class SettingsSynchronizationJobTest
{
    [TestMethod]
    public void OnStop_WhenSettingsChanged_SavesThem()
    {
        var settings = new Mock<ISettingsService>();
        settings.Setup(s => s.IsDirty).Returns(true);

        new SettingsSynchronizationJob(settings.Object).OnAfterStop();

        settings.Verify(s => s.Write(), Times.Once);
    }

    [TestMethod]
    public void OnStop_WhenNothingChanged_DoesNotWrite()
    {
        var settings = new Mock<ISettingsService>();
        settings.Setup(s => s.IsDirty).Returns(false);

        new SettingsSynchronizationJob(settings.Object).OnAfterStop();

        settings.Verify(s => s.Write(), Times.Never);
    }

    [TestMethod]
    public void OnStop_WhenTheSaveFails_DoesNotThrow()
    {
        var settings = new Mock<ISettingsService>();
        settings.Setup(s => s.IsDirty).Returns(true);
        settings.Setup(s => s.Write()).Throws(new IOException("database is locked"));

        new SettingsSynchronizationJob(settings.Object).OnAfterStop();

        settings.Verify(s => s.Write(), Times.Once);
    }
}
