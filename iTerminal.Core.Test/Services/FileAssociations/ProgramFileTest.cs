using System.Runtime.Versioning;
using iTerminal.Core.Services.FileAssociations;

namespace iTerminal.Core.Test.Services.FileAssociations;

[TestClass]
public class ProgramFileTest
{
    private string _folder = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _folder = Directory.CreateTempSubdirectory("iTerminal.Program.").FullName;
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    [UnsupportedOSPlatform("windows")]
    public void IsRunnable_AFileWithoutAnExecuteBit_IsNotAProgram()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The execute bit is a Unix notion; the Windows rule is the extension.");
        }

        var script = Path.Combine(_folder, "run.txt");
        File.WriteAllText(script, "echo hi\n");
        File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        Assert.IsFalse(ProgramFile.IsRunnable(script), "a file without its execute bit goes to the system default");
    }

    [Timeout(60_000)]
    [TestMethod]
    [DataRow("run.sh")]
    [DataRow("run.ps1")]
    [DataRow("RUN.PS1")]
    public void IsRunnable_AShellOrPowerShellScript_IsAProgramWithoutAnExecuteBit(string name)
    {
        var script = Path.Combine(_folder, name);
        File.WriteAllText(script, "echo hi\n");

        Assert.IsTrue(ProgramFile.IsRunnable(script));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void CommandFor_AScript_StartsItWithItsInterpreter()
    {
        Assert.StartsWith("bash ", ProgramFile.CommandFor("run.sh"));
        Assert.Contains("-File", ProgramFile.CommandFor("run.ps1"));
    }

    [Timeout(60_000)]
    [TestMethod]
    [UnsupportedOSPlatform("windows")]
    public void IsRunnable_AFileWithAnExecuteBit_IsAProgram()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The execute bit is a Unix notion; the Windows rule is the extension.");
        }

        var program = Path.Combine(_folder, "tool");
        File.WriteAllText(program, "#!/bin/sh\n");
        File.SetUnixFileMode(program, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        Assert.IsTrue(ProgramFile.IsRunnable(program));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void IsRunnable_AFolderOrAMissingFile_IsNotAProgram()
    {
        Assert.IsFalse(ProgramFile.IsRunnable(_folder));
        Assert.IsFalse(ProgramFile.IsRunnable(Path.Combine(_folder, "missing")));
        Assert.IsFalse(ProgramFile.IsRunnable(null));
        Assert.IsFalse(ProgramFile.IsRunnable(string.Empty));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void CommandFor_OnUnix_RunsTheNameFromTheCurrentFolder_AndKeepsItLiteral()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The Unix command form does not apply on Windows.");
        }

        Assert.AreEqual("./'tool'", ProgramFile.CommandFor("tool"));
        Assert.AreEqual("bash ./'my tool.sh'", ProgramFile.CommandFor("my tool.sh"));
        Assert.AreEqual("./'it'\\''s'", ProgramFile.CommandFor("it's"), "a single quote is closed, escaped and reopened");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void CommandFor_OnWindows_QuotesOnlyANameWithASpace()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The Windows command form does not apply on Unix.");
        }

        Assert.AreEqual(".\\tool.exe", ProgramFile.CommandFor("tool.exe"));
        Assert.AreEqual(".\\run.bat", ProgramFile.CommandFor("run.bat"));
        Assert.AreEqual("cmd /c \".\\my tool.bat\"", ProgramFile.CommandFor("my tool.bat"));
    }
}
