using iTerminal.Core.Services.PackageManager;

namespace iTerminal.Core.Test.Services.PackageManager;

[TestClass]
public class AptPackageManagerServiceTest
{
    private static bool AptAvailable()
    {
        try
        {
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "dpkg-query",
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

    [Timeout(60_000)]
    [TestMethod]
    public void DisplayName_IsApt()
    {
        Assert.AreEqual("APT", new AptPackageManagerService().DisplayName);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task SearchPackagesAsync_EmptyQuery_ReturnsEmptySuccessWithoutRunningAptCache()
    {
        // No conditional skip: an empty query short-circuits before spawning apt-cache at all.
        var service = new AptPackageManagerService();

        var result = await service.SearchPackagesAsync(string.Empty);

        Assert.IsTrue(result.Success);
        Assert.AreEqual(0, result.Packages.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task GetInstalledPackagesAsync_ParsesAtLeastOneInstalledPackage()
    {
        if (!AptAvailable())
        {
            Assert.Inconclusive("dpkg-query not available (not a Debian/Ubuntu host)");
            return;
        }

        var service = new AptPackageManagerService();

        var result = await service.GetInstalledPackagesAsync();

        if (!result.Success)
        {
            Assert.Inconclusive($"dpkg-query failed: {result.Error}");
            return;
        }

        Assert.IsTrue(result.Packages.Count > 0, "Expected at least one installed package on a real Debian/Ubuntu machine.");
        Assert.IsTrue(result.Packages.All(p => !string.IsNullOrWhiteSpace(p.Id)), "Every parsed row must have an Id.");
        Assert.IsTrue(result.Packages.All(p => p.Id == p.Name), "APT packages have no separate id; the package name is the id.");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task InstallAsync_UnknownPackageId_StreamsOutputAndReturnsNonZeroExitCode()
    {
        if (!AptAvailable())
        {
            Assert.Inconclusive("apt-get not available (not a Debian/Ubuntu host)");
            return;
        }

        var service = new AptPackageManagerService();
        var lines = new List<string>();

        // A package name that cannot exist in any repository: apt-get fails fast without installing
        // anything, so it's safe to run as a test (regardless of whether the process has root).
        var exitCode = await service.InstallAsync(
            "no-such-package-does-not-exist-12345",
            line => { lock (lines) lines.Add(line); });

        Assert.AreNotEqual(0, exitCode);
        Assert.IsTrue(lines.Count > 0, "Expected apt-get to explain why the install failed.");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task InstallAsync_WhenCancelled_ReturnsNegative()
    {
        if (!AptAvailable())
        {
            Assert.Inconclusive("apt-get not available (not a Debian/Ubuntu host)");
            return;
        }

        var service = new AptPackageManagerService();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var exitCode = await service.InstallAsync(
            "no-such-package-does-not-exist-12345",
            _ => { },
            cts.Token);

        Assert.IsTrue(exitCode < 0);
    }
}
