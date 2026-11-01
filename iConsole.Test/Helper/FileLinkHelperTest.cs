using iConsole.Helper;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace iConsole.Test.Helper;

/// <summary>Links are told from what they point to, and a link that leads back into its own tree is recognised — the root included.</summary>
[TestClass]
public class FileLinkHelperTest
{
    private string _root = string.Empty;

    [TestInitialize]
    public void Initialize()
    {
        _root = Path.Combine(Path.GetTempPath(), "FileLinkHelperTest_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.Combine(_root, "tree", "inner"));
        Directory.CreateDirectory(Path.Combine(_root, "elsewhere"));
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
        }
    }

    private string Link(string name, string target)
    {
        var link = Path.Combine(_root, "tree", name);
        try
        {
            Directory.CreateSymbolicLink(link, target);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            Assert.Inconclusive($"A symbolic link cannot be created here: {exception.Message}");
        }

        return link;
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AnOrdinaryFolder_IsNotALink()
    {
        Assert.IsFalse(FileLinkHelper.IsLink(Path.Combine(_root, "tree")));
        Assert.IsNull(FileLinkHelper.GetLinkTarget(Path.Combine(_root, "tree")));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ALinkToAFolderElsewhere_DoesNotLeadToAnAncestor()
    {
        var link = Link("to-elsewhere", Path.Combine(_root, "elsewhere"));

        Assert.IsTrue(FileLinkHelper.IsLink(link));
        Assert.IsFalse(FileLinkHelper.LeadsToAnAncestor(link));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ALinkToItsOwnParent_LeadsToAnAncestor()
    {
        var link = Link("up", Path.Combine(_root, "tree"));

        Assert.IsTrue(FileLinkHelper.LeadsToAnAncestor(link));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ALinkToItself_LeadsToAnAncestor()
    {
        var link = Link("self", Path.Combine(_root, "tree", "self"));
        // A link to itself does not resolve; the helper treats that as a loop, not as something to walk.
        Assert.IsTrue(FileLinkHelper.LeadsToAnAncestor(link));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ALinkToTheRootOfTheDisk_LeadsToAnAncestor()
    {
        // Every path is inside the root: a root-pointing link used to slip past the check, as the root's separator was doubled.
        var root = Path.GetPathRoot(Path.GetFullPath(_root))!;
        var link = Link("to-root", root);

        Assert.IsTrue(FileLinkHelper.LeadsToAnAncestor(link));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AFolderThatIsNotALink_IsNotTakenForOne()
    {
        Assert.IsFalse(FileLinkHelper.LeadsToAnAncestor(Path.Combine(_root, "elsewhere")));
    }
}
