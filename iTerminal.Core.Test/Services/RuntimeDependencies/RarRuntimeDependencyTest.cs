using iTerminal.Core.Test.Helper;
using iTerminal.Core.Services.Archives;
using iTerminal.Core.Services.PackageManager;
using iTerminal.Core.Services.RuntimeDependencies;

namespace iTerminal.Core.Test.Services.RuntimeDependencies;

[TestClass]
public class RarRuntimeDependencyTest
{
    private static Task<string?> Banner(string text) => Task.FromResult<string?>(text);

    [Timeout(60_000)]
    [TestMethod]
    public void AProgramOnThePathIsAnExistingInstallWithItsVersion()
    {
        var rar = new RarRuntimeDependency(
            name => name == "rar" ? "/usr/bin/rar" : null,
            [],
            isWindows: false, isMac: false, isPrivileged: true,
            run: (_, _, _) => Task.FromResult(0),
            readVersion: (_, _) => Banner("7.23"));

        Assert.AreEqual("7.23", rar.InstalledVersion());
        Assert.IsTrue(rar.IsExistingInstall);
        Assert.IsTrue(rar.IsCompatible(new Version(7, 23)));
        Assert.IsFalse(rar.IsCompatible(new Version(4, 2)), "RAR 4 writes a format the app does not create");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void WithoutTheProgramThereIsNoVersion()
    {
        var rar = new RarRuntimeDependency(_ => null, [], isWindows: false, isMac: false, isPrivileged: true, run: (_, _, _) => Task.FromResult(0));

        Assert.IsNull(rar.InstalledVersion());
        Assert.IsFalse(rar.IsExistingInstall);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void OnWindowsTheWinRarFolderIsLookedInWhenThePathHasNoProgram()
    {
        var folder = Path.Combine(Path.GetTempPath(), "winrar-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllText(Path.Combine(folder, "Rar.exe"), "");
            var rar = new RarRuntimeDependency(_ => null, [folder], isWindows: true, isMac: false, isPrivileged: true,
                run: (_, _, _) => Task.FromResult(0), readVersion: (_, _) => Banner("7.23"));

            Assert.AreEqual("7.23", rar.InstalledVersion());
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task InstallUsesTheSystemPackageManagerAndNamesThePackageRar()
    {
        SystemPackageInstallPlan? captured = null;
        var rar = new RarRuntimeDependency(
            name => name == "apt-get" ? "/usr/bin/apt-get" : null,
            [],
            isWindows: false, isMac: false, isPrivileged: true,
            run: (plan, _, _) =>
            {
                captured = plan;
                return Task.FromResult(0);
            },
            readVersion: (_, _) => Banner("7.23"));

        // The stand-in program is never found afterwards: the install must say so rather than report success.
        await AssertAsync.ThrowsAsync<InvalidOperationException>(() =>
            rar.InstallAsync("", new Progress<string>(), CancellationToken.None));

        Assert.IsNotNull(captured);
        Assert.AreEqual("apt-get", captured!.Program);
        CollectionAssert.Contains(captured.Arguments.ToList(), "rar");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void UnrarIsTheSeparateUnrarPackageOnAPathThatHasIt()
    {
        var unrar = new UnrarRuntimeDependency(
            name => name == "unrar" ? "/usr/bin/unrar" : null,
            [],
            isWindows: false, isMac: false, isPrivileged: true,
            run: (_, _, _) => Task.FromResult(0),
            readVersion: (_, _) => Banner("7.23"));

        Assert.AreEqual("unrar", unrar.Id);
        Assert.AreEqual("7.23", unrar.InstalledVersion());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheDefaultDependenciesStillAnswerWithoutAnyFakes()
    {
        // Looks at the real machine: where a tool is installed its banner must give a version, where none is it must not.
        var rar = new RarRuntimeDependency().InstalledVersion();
        var unrar = new UnrarRuntimeDependency().InstalledVersion();
        Assert.AreEqual(RarArchiveWriter.FindExecutable() is not null, rar is not null);
        Assert.AreEqual(UnrarArchiveReader.FindExecutable() is not null, unrar is not null);
    }
}
