using iConsole.Data;
using iTerminal.Core.Data;

namespace iTerminal.Core.Test.Data;

[TestClass]
public class DriveTest
{
    [TestMethod]
    public void Constructor_InitializesProperties()
    {
        var drive = new Drive();
        Assert.IsNotNull(drive);
        Assert.AreEqual(string.Empty, drive.Name);
    }

    [TestMethod]
    public void Name_SetAndGet()
    {
        var drive = new Drive { Name = "C:" };
        Assert.AreEqual("C:", drive.Name);
    }

    [TestMethod]
    public void Url_SetAndGet()
    {
        var drive = new Drive { Url = new Url("C:\\") };
        Assert.IsNotNull(drive.Url);
        // Url may have file:// scheme prefix
        Assert.IsTrue(((string)drive.Url).Contains("C:\\"));
    }

    [TestMethod]
    public void DriveInfo_SetAndGet()
    {
        var driveInfo = DriveInfo.GetDrives()[0];
        var drive = new Drive { DriveInfo = driveInfo };
        Assert.AreEqual(driveInfo, drive.DriveInfo);
    }

    [TestMethod]
    public void IsEnabled_WithMemoryDriveName_ReturnsTrue()
    {
        var drive = new Drive { Name = "memdrive://MyDrive" };
        Assert.IsTrue(drive.IsEnabled);
    }

    [TestMethod]
    public void IsEnabled_WithReadyDrive_ReturnsTrue()
    {
        var driveInfo = DriveInfo.GetDrives().FirstOrDefault(d => d.IsReady);
        if (driveInfo != null)
        {
            var drive = new Drive { DriveInfo = driveInfo };
            Assert.IsTrue(drive.IsEnabled);
        }
    }

    [TestMethod]
    public void IsEnabled_WithNotReadyDrive_ReturnsFalse()
    {
        var driveInfo = DriveInfo.GetDrives().FirstOrDefault(d => !d.IsReady);
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

    [TestMethod]
    public void IsEnabled_WithNullDriveInfo_ReturnsFalse()
    {
        var drive = new Drive { Name = "SomeDrive", DriveInfo = null };
        Assert.IsFalse(drive.IsEnabled);
    }
}
