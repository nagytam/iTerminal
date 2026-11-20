using iTerminal.Core.Services.Shell;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace iTerminal.Core.Services.Shell.Test;

/// <summary>What each shell is started with, how a command is wrapped and marked, and which shell a session starts as.</summary>
[TestClass]
public class ShellKindTest
{
    [TestMethod]
    public void TryParse_IsCaseInsensitive_AndRefusesAnythingElse()
    {
        Assert.IsTrue(ShellKinds.TryParse("BASH", out var bash));
        Assert.AreEqual(ShellKind.Bash, bash);
        Assert.IsTrue(ShellKinds.TryParse(" sh ", out var sh));
        Assert.AreEqual(ShellKind.Sh, sh);
        Assert.IsFalse(ShellKinds.TryParse("zsh", out _));
        Assert.IsFalse(ShellKinds.TryParse(null, out _));
    }

    [TestMethod]
    public void Bash_StartsWithoutItsStartupFiles_AndSh_HasNoArguments()
    {
        CollectionAssert.AreEqual(new[] { "--noprofile", "--norc" }, ShellKinds.LaunchArguments(ShellKind.Bash).ToArray());
        Assert.AreEqual(0, ShellKinds.LaunchArguments(ShellKind.Sh).Count);
    }

    [TestMethod]
    public void Wrap_GroupsTheCommand_AndTakesStdinFromDevNull()
    {
        Assert.AreEqual("{ cd x && cat\n} </dev/null", ShellKinds.Wrap("cd x && cat"));
    }

    [TestMethod]
    public void MarkerLine_CarriesTheTokenStatusAndFolder_OnStdout_AndTheTokenAloneOnStderr()
    {
        var marker = ShellKinds.MarkerLine(ShellKind.Sh, "TOKEN");

        StringAssert.Contains(marker, "'TOKEN'");
        StringAssert.Contains(marker, "$PWD");
        StringAssert.Contains(marker, ">&2");
    }

    [TestMethod]
    public void Quote_KeepsEverythingLiteral_AndEscapesAQuote()
    {
        Assert.AreEqual("'it'\\''s'", ShellKinds.Quote("it's"));
        Assert.AreEqual("'a $b; c'", ShellKinds.Quote("a $b; c"));
    }

    [TestMethod]
    public void ChangeDirectory_QuotesTheFolder()
    {
        Assert.AreEqual("cd -- '/tmp/my folder'", ShellKinds.ChangeDirectory(ShellKind.Bash, "/tmp/my folder"));
    }

    [TestMethod]
    public void Initial_UsesTheSetting_WhenItIsAvailable()
    {
        Assert.AreEqual(ShellKind.Bash, LocalShellSessionService.Initial("bash", [ShellKind.Sh, ShellKind.Bash]));
    }

    [TestMethod]
    public void Initial_FallsBackToSh_WhenTheSettingIsNotAvailable()
    {
        Assert.AreEqual(ShellKind.Sh, LocalShellSessionService.Initial("bash", [ShellKind.Sh]));
    }
}
