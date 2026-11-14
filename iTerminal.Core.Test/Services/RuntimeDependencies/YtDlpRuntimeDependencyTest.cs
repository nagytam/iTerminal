using iTerminal.Core.Services.RuntimeDependencies;
using iTerminal.Core.Test.Helper;

namespace iTerminal.Core.Test.Services.RuntimeDependencies;

[TestClass]
public class YtDlpRuntimeDependencyTest
{
    private static Func<string, string?> Path(params string[] tools) =>
        program => tools.Contains(program) ? "/bin/" + program : null;

    private static YtDlpRuntimeDependency Create(
        Func<string, string?> findOnPath,
        Func<SystemPackageInstallPlan, Action<string>, CancellationToken, Task<int>>? run = null,
        Func<string, TimeSpan, Task<string?>>? readVersion = null,
        bool windows = false,
        bool mac = false,
        bool privileged = false) =>
        new(findOnPath, [], readVersion ?? ((_, _) => Task.FromResult<string?>("2025.09.26")), run ?? ((_, _, _) => Task.FromResult(0)), windows, mac, privileged);

    [TestMethod]
    public void NothingOnThePath_IsMissing()
    {
        YtDlpRuntimeDependency dependency = Create(Path());

        Assert.IsNull(dependency.InstalledVersion());
        Assert.IsFalse(dependency.IsExistingInstall);
    }

    [TestMethod]
    public void AnInstalledProgram_ReportsItsVersionAsAnExistingInstall()
    {
        YtDlpRuntimeDependency dependency = Create(Path("yt-dlp"));

        Assert.AreEqual("2025.09.26", dependency.InstalledVersion());
        Assert.IsTrue(dependency.IsExistingInstall);
        StringAssert.Contains(dependency.ExistingInstallNote!, "/bin/yt-dlp");
    }

    [TestMethod]
    public void AProgramThatDoesNotAnswerVersionIsMissing()
    {
        YtDlpRuntimeDependency dependency = Create(Path("yt-dlp"), readVersion: (_, _) => Task.FromResult<string?>(null));

        Assert.IsNull(dependency.InstalledVersion());
    }

    [TestMethod]
    public void TheVersionIsReadOnceUntilInvalidated()
    {
        int reads = 0;
        YtDlpRuntimeDependency dependency = Create(Path("yt-dlp"), readVersion: (_, _) =>
        {
            reads++;
            return Task.FromResult<string?>("2025.09.26");
        });

        dependency.InstalledVersion();
        dependency.InstalledVersion();
        Assert.AreEqual(1, reads, "the version is cached by path and write time, not asked for on every call");

        dependency.Invalidate();
        dependency.InstalledVersion();
        Assert.AreEqual(2, reads);
    }

    [TestMethod]
    [DataRow("2025.01.01", true)]
    [DataRow("2025.09.26", true)]
    [DataRow("2026.11.14.120000", true)]
    [DataRow("2024.12.31", false)]
    public void TheYearIsTheCompatibilityLine(string version, bool expected) =>
        Assert.AreEqual(expected, Create(Path()).IsCompatible(Version.Parse(version)));

    [TestMethod]
    public async Task InstallWithNoPackageManagerSaysHowToInstallIt()
    {
        YtDlpRuntimeDependency dependency = Create(Path());

        await AssertAsync.ThrowsAsync<InvalidOperationException>(() =>
            dependency.InstallAsync("2025.09.26", new Progress<string>(), CancellationToken.None));
    }

    [TestMethod]
    public async Task InstallRunsTheSystemsPackageManager()
    {
        SystemPackageInstallPlan? ran = null;
        YtDlpRuntimeDependency dependency = Create(
            Path("apt-get"),
            run: (plan, _, _) =>
            {
                ran = plan;
                return Task.FromResult(0);
            },
            privileged: true);

        // The program is not installed by the fake run, so the install reports the failure it must, not a silent success.
        InvalidOperationException exception = await AssertAsync.ThrowsAsync<InvalidOperationException>(() =>
            dependency.InstallAsync("2025.09.26", new Progress<string>(), CancellationToken.None));

        Assert.IsNotNull(ran);
        Assert.AreEqual("apt-get", ran.Program);
        CollectionAssert.AreEqual(new[] { "install", "-y", "yt-dlp" }, ran.Arguments.ToArray());
        StringAssert.Contains(exception.Message, "yt-dlp was not found afterwards");
    }

    [TestMethod]
    public async Task InstallThatFailsReportsTheExitCodeAndTheCommandToRunByHand()
    {
        YtDlpRuntimeDependency dependency = Create(
            Path("apt-get", "sudo"),
            run: (_, _, _) => Task.FromResult(100),
            privileged: false);

        InvalidOperationException exception = await AssertAsync.ThrowsAsync<InvalidOperationException>(() =>
            dependency.InstallAsync("2025.09.26", new Progress<string>(), CancellationToken.None));

        StringAssert.Contains(exception.Message, "exited with code 100");
        StringAssert.Contains(exception.Message, "sudo apt-get install -y yt-dlp");
    }
}
