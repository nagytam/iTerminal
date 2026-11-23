using iTerminal.Core.Services;

namespace iTerminal.Core.Test.Services;

/// <summary>
/// The SSH drive's command lines, checked as text: a path reaches the remote shell as one argument whatever it
/// contains. These are the strings a server would run, so they are the tests that stand in for a server.
/// </summary>
[TestClass]
public class SshCommandsTest
{
    [TestMethod]
    public void Quote_WrapsAPlainPathInSingleQuotes()
    {
        Assert.AreEqual("'/home/u/notes.txt'", SshCommands.Quote("/home/u/notes.txt"));
    }

    [TestMethod]
    public void Quote_KeepsSpacesSeparatorsAndSubstitutionsInsideOneArgument()
    {
        Assert.AreEqual("'/home/u/my file; $(reboot) | x'", SshCommands.Quote("/home/u/my file; $(reboot) | x"));
    }

    [TestMethod]
    public void Quote_EscapesASingleQuoteTheWayPosixDoes()
    {
        Assert.AreEqual("'/home/u/it'\\''s'", SshCommands.Quote("/home/u/it's"));
    }

    [TestMethod]
    public void RemoveFile_PassesTheWholePathAfterDoubleDash()
    {
        Assert.AreEqual("rm -- '/home/u/my file'", SshCommands.RemoveFile("/home/u/my file"));
    }

    [TestMethod]
    public void RemoveFile_DoesNotReadADashedNameAsAnOption()
    {
        Assert.AreEqual("rm -- '-rf'", SshCommands.RemoveFile("-rf"));
    }

    [TestMethod]
    public void RemoveTree_IsRecursive()
    {
        Assert.AreEqual("rm -R -- '/home/u/a;b'", SshCommands.RemoveTree("/home/u/a;b"));
    }

    [TestMethod]
    public void RemoveEmptyFolder_UsesRmdirSoAFolderWithContentsIsNotTouched()
    {
        Assert.AreEqual("rmdir -- '/home/u/folder'", SshCommands.RemoveEmptyFolder("/home/u/folder"));
    }

    [TestMethod]
    public void Copy_KeepsAnExistingTargetOnlyWhenNotOverwriting()
    {
        Assert.AreEqual("cp -r -- '/a b' '/c d'", SshCommands.Copy("/a b", "/c d", noClobber: false));
        Assert.AreEqual("cp -r -n -- '/a b' '/c d'", SshCommands.Copy("/a b", "/c d", noClobber: true));
    }

    [TestMethod]
    public void Move_KeepsAnExistingTargetOnlyWhenNotOverwriting()
    {
        Assert.AreEqual("mv -- '/a' '/b'", SshCommands.Move("/a", "/b", noClobber: false));
        Assert.AreEqual("mv -n -- '/a' '/b'", SshCommands.Move("/a", "/b", noClobber: true));
    }

    [TestMethod]
    public void List_QuotesThePathAndEndsOptionsBeforeIt()
    {
        Assert.AreEqual("ls -la -- '/home/u/my dir'", SshCommands.List("/home/u/my dir"));
    }

    [TestMethod]
    public void ExistenceChecks_QuoteThePath()
    {
        Assert.AreEqual("test -f '/home/u/a b' && echo 1 || echo 0", SshCommands.TestFile("/home/u/a b"));
        Assert.AreEqual("test -d '/home/u/a b' && echo 1 || echo 0", SshCommands.TestFolder("/home/u/a b"));
    }

    [TestMethod]
    public void MakeFolder_QuotesThePath()
    {
        Assert.AreEqual("mkdir -- '/home/u/new dir'", SshCommands.MakeFolder("/home/u/new dir"));
    }

    [TestMethod]
    public void ChangeDirectory_QuotesTheWorkingFolder()
    {
        Assert.AreEqual("cd '/home/u/my dir';", SshCommands.ChangeDirectory("/home/u/my dir"));
    }
}
