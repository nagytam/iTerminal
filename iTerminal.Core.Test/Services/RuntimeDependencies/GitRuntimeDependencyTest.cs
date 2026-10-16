using iTerminal.Core.Services.RuntimeDependencies;

namespace iTerminal.Core.Test.Services.RuntimeDependencies;

[TestClass]
public class GitRuntimeDependencyTest
{
    private static Func<string, string?> Path(params string[] tools) =>
        program => tools.Contains(program) ? "/bin/" + program : null;

    private static GitRuntimeDependency Create(
        Func<string, string?> findOnPath,
        Func<GitInstallPlan, Action<string>, CancellationToken, Task<int>>? run = null,
        Func<string, TimeSpan, Task<string?>>? readVersion = null,
        bool windows = false,
        bool mac = false,
        bool privileged = false) =>
        new(findOnPath, [], readVersion ?? ((_, _) => Task.FromResult<string?>("2.43.0")), run ?? ((_, _, _) => Task.FromResult(0)), windows, mac, privileged);

    [TestMethod]
    public void ParseVersion_ReadsTheFirstThreeNumbers()
    {
        Assert.AreEqual("2.43.0", GitRuntimeDependency.ParseVersion("git version 2.43.0"));
        Assert.AreEqual("2.47.0", GitRuntimeDependency.ParseVersion("git version 2.47.0.windows.1"));
        Assert.AreEqual("2.39.5", GitRuntimeDependency.ParseVersion("git version 2.39.5 (Apple Git-154)"));
        Assert.IsNull(GitRuntimeDependency.ParseVersion("command not found"));
    }

    [TestMethod]
    public void NoGitOnThePath_IsMissing()
    {
        GitRuntimeDependency dependency = Create(Path());

        Assert.IsNull(dependency.InstalledVersion());
        Assert.IsFalse(dependency.IsExistingInstall);
    }

    [TestMethod]
    public void GitOnThePath_IsAnExistingInstallAndNeverUpdated()
    {
        GitRuntimeDependency dependency = Create(Path("git"));

        Assert.AreEqual("2.43.0", dependency.InstalledVersion());
        Assert.IsTrue(dependency.IsExistingInstall);
        Assert.IsNotNull(dependency.ExistingInstallNote);
    }

    [TestMethod]
    public void AProgramThatIsNotAWorkingGit_ReadsAsMissing()
    {
        GitRuntimeDependency dependency = Create(Path("git"), readVersion: (_, _) => Task.FromResult<string?>(null));

        Assert.IsNull(dependency.InstalledVersion());
    }

    [TestMethod]
    public void AnyMajorTwoOrLaterIsCompatible()
    {
        GitRuntimeDependency dependency = Create(Path());

        Assert.IsTrue(dependency.IsCompatible(new Version(2, 50, 0)));
        Assert.IsTrue(dependency.IsCompatible(new Version(3, 0, 0)));
        Assert.IsFalse(dependency.IsCompatible(new Version(1, 9, 0)));
    }

    [TestMethod]
    public void Plan_Windows_UsesWinget()
    {
        GitInstallPlan? plan = GitInstallPlan.For(true, false, false, Path("winget"));

        Assert.IsNotNull(plan);
        Assert.AreEqual("winget", plan.Program);
        CollectionAssert.Contains(plan.Arguments.ToList(), "Git.Git");
        Assert.IsFalse(plan.RequiresSudo);
    }

    [TestMethod]
    public void Plan_Windows_WithoutWinget_IsNull() =>
        Assert.IsNull(GitInstallPlan.For(true, false, false, Path()));

    [TestMethod]
    public void Plan_Apt_AsRoot_RunsAptGetDirectly()
    {
        GitInstallPlan? plan = GitInstallPlan.For(false, false, true, Path("apt-get", "sudo"));

        Assert.IsNotNull(plan);
        Assert.AreEqual("apt-get", plan.Program);
        Assert.AreEqual("noninteractive", plan.Environment!["DEBIAN_FRONTEND"]);
    }

    [TestMethod]
    public void Plan_Apt_AsUser_GoesThroughSudoWithoutAPrompt()
    {
        GitInstallPlan? plan = GitInstallPlan.For(false, false, false, Path("apt-get", "sudo"));

        Assert.IsNotNull(plan);
        Assert.AreEqual("sudo", plan.Program);
        Assert.AreEqual("-n", plan.Arguments[0]);
        Assert.IsTrue(plan.RequiresSudo);
        StringAssert.Contains(plan.CommandLine, "apt-get install -y git");
    }

    [TestMethod]
    [DataRow("dnf", "dnf")]
    [DataRow("zypper", "zypper")]
    [DataRow("pacman", "pacman")]
    [DataRow("apk", "apk")]
    public void Plan_OtherLinuxManagers_AreFound(string tool, string expectedProgram)
    {
        GitInstallPlan? plan = GitInstallPlan.For(false, false, true, Path(tool));

        Assert.IsNotNull(plan);
        Assert.AreEqual(expectedProgram, plan.Program);
    }

    [TestMethod]
    public void Plan_Mac_UsesHomebrew() =>
        Assert.AreEqual("brew", GitInstallPlan.For(false, true, false, Path("brew"))!.Program);

    [TestMethod]
    public void Plan_NoPackageManager_IsNull() =>
        Assert.IsNull(GitInstallPlan.For(false, false, true, Path()));

    [TestMethod]
    public async Task Install_RunsThePlan_AndSucceedsWhenGitAppears()
    {
        bool installed = false;
        GitInstallPlan? seen = null;
        GitRuntimeDependency dependency = Create(
            program => program == "apt-get" ? "/bin/apt-get" : program == "git" && installed ? "/bin/git" : null,
            run: (plan, _, _) =>
            {
                seen = plan;
                installed = true;
                return Task.FromResult(0);
            },
            privileged: true);

        await dependency.InstallAsync("2.0.0", new Progress<string>(), CancellationToken.None);

        Assert.AreEqual("apt-get", seen?.Program);
        Assert.AreEqual("2.43.0", dependency.InstalledVersion());
    }

    [TestMethod]
    public async Task Install_FailureNamesTheCommandToRunByHand()
    {
        GitRuntimeDependency dependency = Create(Path("apt-get", "sudo"), run: (_, _, _) => Task.FromResult(1));

        InvalidOperationException exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => dependency.InstallAsync("2.0.0", new Progress<string>(), CancellationToken.None));

        StringAssert.Contains(exception.Message, "sudo apt-get install -y git");
    }

    [TestMethod]
    public async Task Install_WithoutAPackageManager_SaysWhereToGetGit()
    {
        GitRuntimeDependency dependency = Create(Path());

        InvalidOperationException exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => dependency.InstallAsync("2.0.0", new Progress<string>(), CancellationToken.None));

        StringAssert.Contains(exception.Message, "git-scm.com");
    }

    [TestMethod]
    public async Task Install_WhenGitStillMissingAfterwards_Fails()
    {
        GitRuntimeDependency dependency = Create(Path("apt-get"), privileged: true);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => dependency.InstallAsync("2.0.0", new Progress<string>(), CancellationToken.None));
    }
}
