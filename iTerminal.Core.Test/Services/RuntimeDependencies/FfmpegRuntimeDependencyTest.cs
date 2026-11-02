using iTerminal.Core.Services.RuntimeDependencies;

namespace iTerminal.Core.Test.Services.RuntimeDependencies;

[TestClass]
public class FfmpegRuntimeDependencyTest
{
    private static Func<string, string?> Path(params string[] tools) =>
        program => tools.Contains(program) ? "/bin/" + program : null;

    private static FfmpegRuntimeDependency Create(
        Func<string, string?> findOnPath,
        Func<SystemPackageInstallPlan, Action<string>, CancellationToken, Task<int>>? run = null,
        Func<string, TimeSpan, Task<string?>>? readVersion = null,
        bool windows = false,
        bool mac = false,
        bool privileged = false) =>
        new(findOnPath, [], readVersion ?? ((_, _) => Task.FromResult<string?>("7.1.0")), run ?? ((_, _, _) => Task.FromResult(0)), windows, mac, privileged);

    [Timeout(60_000)]
    [TestMethod]
    [DataRow("ffmpeg version 7.1.1-essentials_build-www.gyan.dev Copyright", "7.1.1")]
    [DataRow("ffmpeg version n6.0 Copyright", "6.0.0")]
    [DataRow("ffmpeg version 6.1.1-3ubuntu5 Copyright", "6.1.1")]
    [DataRow("ffmpeg version 7.0 Copyright", "7.0.0")]
    [DataRow("ffmpeg version N-117000-g1a2b3c4d5e Copyright", "99.0.0")]
    public void ParseVersion_ReadsTheNumbers(string output, string expected) =>
        Assert.AreEqual(expected, FfmpegRuntimeDependency.ParseVersion(output));

    [Timeout(60_000)]
    [TestMethod]
    public void ParseVersion_RejectsOtherOutput() =>
        Assert.IsNull(FfmpegRuntimeDependency.ParseVersion("command not found"));

    [Timeout(60_000)]
    [TestMethod]
    public void NothingOnThePath_IsMissing()
    {
        FfmpegRuntimeDependency dependency = Create(Path());

        Assert.IsNull(dependency.InstalledVersion());
        Assert.IsFalse(dependency.IsExistingInstall);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void FfmpegWithoutFfprobe_IsStillMissing()
    {
        FfmpegRuntimeDependency dependency = Create(Path("ffmpeg"));

        Assert.IsNull(dependency.InstalledVersion());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void BothProgramsOnThePath_AreAnExistingInstallAndNeverUpdated()
    {
        FfmpegRuntimeDependency dependency = Create(Path("ffmpeg", "ffprobe"));

        Assert.AreEqual("7.1.0", dependency.InstalledVersion());
        Assert.IsTrue(dependency.IsExistingInstall);
        Assert.IsNotNull(dependency.ExistingInstallNote);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AProgramThatIsNotAWorkingFfmpeg_ReadsAsMissing()
    {
        FfmpegRuntimeDependency dependency = Create(Path("ffmpeg", "ffprobe"), readVersion: (_, _) => Task.FromResult<string?>(null));

        Assert.IsNull(dependency.InstalledVersion());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void FourOrLaterIsCompatible()
    {
        FfmpegRuntimeDependency dependency = Create(Path());

        Assert.IsTrue(dependency.IsCompatible(new Version(4, 0, 0)));
        Assert.IsTrue(dependency.IsCompatible(new Version(8, 0, 0)));
        Assert.IsFalse(dependency.IsCompatible(new Version(3, 4, 0)));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Plan_Windows_UsesWingetWithTheGyanPackage()
    {
        SystemPackageInstallPlan? plan = SystemPackageInstallPlan.For(FfmpegRuntimeDependency.PackageNames, true, false, false, Path("winget"));

        Assert.IsNotNull(plan);
        Assert.AreEqual("winget", plan.Program);
        CollectionAssert.Contains(plan.Arguments.ToList(), "Gyan.FFmpeg");
        Assert.IsFalse(plan.RequiresSudo);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Plan_Windows_WithoutWinget_IsNull() =>
        Assert.IsNull(SystemPackageInstallPlan.For(FfmpegRuntimeDependency.PackageNames, true, false, false, Path()));

    [Timeout(60_000)]
    [TestMethod]
    public void Plan_Mac_UsesHomebrew()
    {
        SystemPackageInstallPlan? plan = SystemPackageInstallPlan.For(FfmpegRuntimeDependency.PackageNames, false, true, false, Path("brew"));

        Assert.AreEqual("brew", plan!.Program);
        CollectionAssert.AreEqual(new[] { "install", "ffmpeg" }, plan.Arguments.ToArray());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Plan_Apt_AsUser_GoesThroughSudoWithoutAPrompt()
    {
        SystemPackageInstallPlan? plan = SystemPackageInstallPlan.For(FfmpegRuntimeDependency.PackageNames, false, false, false, Path("apt-get", "sudo"));

        Assert.IsNotNull(plan);
        Assert.AreEqual("sudo", plan.Program);
        Assert.AreEqual("-n", plan.Arguments[0]);
        Assert.IsTrue(plan.RequiresSudo);
        StringAssert.Contains(plan.CommandLine, "apt-get install -y ffmpeg");
        Assert.AreEqual("sudo apt-get install -y ffmpeg", plan.ManualCommand);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Plan_Apt_AsRoot_RunsAptGetDirectlyAndNonInteractively()
    {
        SystemPackageInstallPlan? plan = SystemPackageInstallPlan.For(FfmpegRuntimeDependency.PackageNames, false, false, true, Path("apt-get", "sudo"));

        Assert.AreEqual("apt-get", plan!.Program);
        Assert.AreEqual("noninteractive", plan.Environment!["DEBIAN_FRONTEND"]);
    }

    [Timeout(60_000)]
    [TestMethod]
    [DataRow("dnf", "ffmpeg-free")]
    [DataRow("zypper", "ffmpeg")]
    [DataRow("pacman", "ffmpeg")]
    [DataRow("apk", "ffmpeg")]
    public void Plan_OtherLinuxManagers_NameTheirOwnPackage(string tool, string package)
    {
        SystemPackageInstallPlan? plan = SystemPackageInstallPlan.For(FfmpegRuntimeDependency.PackageNames, false, false, true, Path(tool));

        Assert.AreEqual(tool, plan!.Program);
        CollectionAssert.Contains(plan.Arguments.ToList(), package);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Plan_NoPackageManager_IsNull() =>
        Assert.IsNull(SystemPackageInstallPlan.For(FfmpegRuntimeDependency.PackageNames, false, false, true, Path()));

    [Timeout(60_000)]
    [TestMethod]
    public async Task Install_RunsThePlan_AndSucceedsWhenBothProgramsAppear()
    {
        bool installed = false;
        SystemPackageInstallPlan? seen = null;
        FfmpegRuntimeDependency dependency = Create(
            program => program == "apt-get" ? "/bin/apt-get" : program is "ffmpeg" or "ffprobe" && installed ? "/bin/" + program : null,
            run: (plan, _, _) =>
            {
                seen = plan;
                installed = true;
                return Task.FromResult(0);
            },
            privileged: true);

        await dependency.InstallAsync("4.0.0", new Progress<string>(), CancellationToken.None);

        Assert.AreEqual("apt-get", seen?.Program);
        Assert.AreEqual("7.1.0", dependency.InstalledVersion());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Install_FailureNamesTheCommandToRunByHand()
    {
        FfmpegRuntimeDependency dependency = Create(Path("apt-get", "sudo"), run: (_, _, _) => Task.FromResult(1));

        InvalidOperationException exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => dependency.InstallAsync("4.0.0", new Progress<string>(), CancellationToken.None));

        StringAssert.Contains(exception.Message, "sudo apt-get install -y ffmpeg");
    }

    [Timeout(60_000)]
    [TestMethod]
    [DataRow(-1978335189)]
    [DataRow(-1978335135)]
    public async Task Install_WinGetSaysAlreadyInstalled_IsNotAFailure(int exitCode)
    {
        FfmpegRuntimeDependency dependency = Create(Path("winget", "ffmpeg", "ffprobe"), run: (_, _, _) => Task.FromResult(exitCode), windows: true);

        await dependency.InstallAsync("4.0.0", new Progress<string>(), CancellationToken.None);

        Assert.AreEqual("7.1.0", dependency.InstalledVersion());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Install_WithoutAPackageManager_SaysWhereToGetFfmpeg()
    {
        FfmpegRuntimeDependency dependency = Create(Path());

        InvalidOperationException exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => dependency.InstallAsync("4.0.0", new Progress<string>(), CancellationToken.None));

        StringAssert.Contains(exception.Message, "ffmpeg.org");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Install_WhenTheProgramsAreStillMissingAfterwards_Fails()
    {
        FfmpegRuntimeDependency dependency = Create(Path("apt-get"), privileged: true);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => dependency.InstallAsync("4.0.0", new Progress<string>(), CancellationToken.None));
    }
}
