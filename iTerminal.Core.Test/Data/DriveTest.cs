using iConsole.Data;
using iTerminal.Core.Data;

namespace iTerminal.Core.Test.Data;

[TestClass]
public class DriveTest
{
    // Asking a mapped network drive whether it is ready blocks for as long as its server takes to answer
    // (25 s each for an unreachable NAS), and which drive comes first depends on the machine.
    private static IEnumerable<DriveInfo> LocalDrives() =>
        DriveInfo.GetDrives().Where(d => d.DriveType != DriveType.Network);

    [Timeout(60_000)]
    [TestMethod]
    public void Constructor_InitializesProperties()
    {
        var drive = new Drive();
        Assert.IsNotNull(drive);
        Assert.AreEqual(string.Empty, drive.Name);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Name_SetAndGet()
    {
        var drive = new Drive { Name = "C:" };
        Assert.AreEqual("C:", drive.Name);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Url_SetAndGet()
    {
        var drive = new Drive { Url = new Url("C:\\") };
        Assert.IsNotNull(drive.Url);
        // Url may have file:// scheme prefix
        Assert.IsTrue(((string)drive.Url).Contains("C:\\"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void DriveInfo_SetAndGet()
    {
        var driveInfo = DriveInfo.GetDrives()[0];
        var drive = new Drive { DriveInfo = driveInfo };
        Assert.AreEqual(driveInfo, drive.DriveInfo);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void IsEnabled_WithMemoryDriveName_ReturnsTrue()
    {
        var drive = new Drive { Name = "memdrive://MyDrive" };
        Assert.IsTrue(drive.IsEnabled);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void IsEnabled_WithReadyDrive_ReturnsTrue()
    {
        var driveInfo = LocalDrives().FirstOrDefault(d => d.IsReady);
        if (driveInfo != null)
        {
            var drive = new Drive { DriveInfo = driveInfo };
            Assert.IsTrue(drive.IsEnabled);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public void IsEnabled_WithNotReadyDrive_ReturnsFalse()
    {
        var driveInfo = LocalDrives().FirstOrDefault(d => !d.IsReady);
        if (driveInfo != null)
        {
            var drive = new Drive { DriveInfo = driveInfo, Name = "NotMemDrive" };
            Assert.IsFalse(drive.IsEnabled);
        }
        else
        {
            // No non-ready drives on this machine. Probe uncommon drive letters for a likely-non-ready drive.
            DriveInfo? probe = null;
            for (char letter = 'Z'; letter >= 'P'; letter--)
            {
                try
                {
                    var candidate = new DriveInfo($"{letter}:\\");
                    if (!candidate.IsReady)
                    {
                        probe = candidate;
                        break;
                    }
                }
                catch
                {
                    // ignore and continue probing
                }
            }

            if (probe != null)
            {
                var drive = new Drive { DriveInfo = probe, Name = "NotMemDrive" };
                Assert.IsFalse(drive.IsEnabled);
            }
            else
            {
                // As a deterministic fallback, verify the behavior when DriveInfo is null (covered elsewhere)
                var drive = new Drive { Name = "NotMemDrive", DriveInfo = null };
                Assert.IsFalse(drive.IsEnabled);
            }
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public void IsEnabled_WithNullDriveInfo_ReturnsFalse()
    {
        var drive = new Drive { Name = "SomeDrive", DriveInfo = null };
        Assert.IsFalse(drive.IsEnabled);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void IsCloudConfigured_DefaultsToFalse()
    {
        Assert.IsFalse(new Drive().IsCloudConfigured);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void IsCloud_IsReadFromTheUrl_NotTheName()
    {
        // The row's name carries the "not configured" suffix, so the URL is the only reliable identity.
        var oneDrive = new Drive { Name = "onedrive:// Cloud sign-in not configured", Url = Container.ONEDRIVE };
        var googleDrive = new Drive { Name = "gdrive://", Url = Container.GOOGLE_DRIVE };

        Assert.IsTrue(oneDrive.IsCloud);
        Assert.IsTrue(googleDrive.IsCloud);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void IsCloud_IsTrue_ForEveryCloudProvider()
    {
        Assert.IsTrue(new Drive { Name = Container.DROPBOX, Url = Container.DROPBOX }.IsCloud);
        Assert.IsTrue(new Drive { Name = Container.AZURE_BLOB, Url = Container.AZURE_BLOB }.IsCloud);
        Assert.IsTrue(new Drive { Name = Container.S3, Url = Container.S3 }.IsCloud);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void IsCloud_IsFalse_ForALocalRow_AndForARowWithNoUrl()
    {
        var local = new Drive { Name = "local", Url = new Url(Path.GetTempPath()) };
        var noUrl = new Drive { Name = Container.ONEDRIVE };

        Assert.IsFalse(local.IsCloud);
        Assert.IsFalse(noUrl.IsCloud, "A row built without a URL must not be taken for a cloud drive.");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void IsEnabled_CloudRow_OnlyWhenItsRegistrationIsConfigured()
    {
        var notConfigured = new Drive { Name = Container.ONEDRIVE, Url = Container.ONEDRIVE, IsCloudConfigured = false };
        var configured = new Drive { Name = Container.ONEDRIVE, Url = Container.ONEDRIVE, IsCloudConfigured = true };

        Assert.IsFalse(notConfigured.IsEnabled);
        Assert.IsTrue(configured.IsEnabled);
    }
}
