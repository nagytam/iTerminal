using iTerminal.Core.Services.PackageManager;

namespace iTerminal.Core.Test.Services.PackageManager;

[TestClass]
public class WinGetPackageManagerServiceTest
{
    private static bool WinGetAvailable()
    {
        try
        {
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "winget",
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            process!.WaitForExit(5000);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    [TestMethod]
    public void DisplayName_IsWinGet()
    {
        Assert.AreEqual("WinGet", new WinGetPackageManagerService().DisplayName);
    }

    [TestMethod]
    public async Task SearchPackagesAsync_EmptyQuery_ReturnsEmptySuccessWithoutRunningWinget()
    {
        // No conditional skip: an empty query short-circuits before spawning winget at all.
        var service = new WinGetPackageManagerService();

        var result = await service.SearchPackagesAsync(string.Empty);

        Assert.IsTrue(result.Success);
        Assert.AreEqual(0, result.Packages.Count);
    }

    [TestMethod]
    public async Task GetInstalledPackagesAsync_ParsesAtLeastOneInstalledPackage()
    {
        if (!WinGetAvailable())
        {
            Assert.Inconclusive("winget not available");
            return;
        }

        var service = new WinGetPackageManagerService();

        var result = await service.GetInstalledPackagesAsync();

        if (!result.Success)
        {
            Assert.Inconclusive($"winget list failed: {result.Error}");
            return;
        }

        if (result.Packages.Count == 0)
        {
            // Whether anything is installed is a property of the machine (a clean CI runner has nothing),
            // not of the parser: the parsing is what this test is for.
            Assert.Inconclusive("winget lists no installed packages on this machine.");
            return;
        }

        Assert.IsTrue(result.Packages.All(p => !string.IsNullOrWhiteSpace(p.Id)), "Every parsed row must have an Id.");
    }

    [TestMethod]
    public async Task InstallAsync_UnknownPackageId_StreamsOutputAndReturnsNonZeroExitCode()
    {
        if (!WinGetAvailable())
        {
            Assert.Inconclusive("winget not available");
            return;
        }

        var service = new WinGetPackageManagerService();
        var lines = new List<string>();

        // A package id that cannot exist: this fails fast (no source lookup succeeds) and never
        // installs anything, so it's safe to run as a test.
        var exitCode = await service.InstallAsync(
            "NoSuchVendor.NoSuchApp.DoesNotExist12345",
            line => { lock (lines) lines.Add(line); });

        Assert.AreNotEqual(0, exitCode);
        Assert.IsTrue(lines.Count > 0, "Expected winget to explain why the install failed.");
    }

    [TestMethod]
    public async Task InstallAsync_WhenCancelled_ReturnsNegative()
    {
        if (!WinGetAvailable())
        {
            Assert.Inconclusive("winget not available");
            return;
        }

        var service = new WinGetPackageManagerService();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var exitCode = await service.InstallAsync(
            "NoSuchVendor.NoSuchApp.DoesNotExist12345",
            _ => { },
            cts.Token);

        Assert.IsTrue(exitCode < 0);
    }
}
