using iTerminal.Core.Model.Git;

namespace iTerminal.Core.Test.Model.Git;

[TestClass]
public class GitFileStatusTest
{
    [TestMethod]
    [DataRow("UU")]
    [DataRow("AA")]
    [DataRow("DD")]
    [DataRow("AU")]
    [DataRow("UA")]
    [DataRow("DU")]
    [DataRow("UD")]
    public void AConflictedFileIsListedOnlyAsAWorkingTreeChange(string code)
    {
        // Both sides of an unmerged path carry a letter, so read naively a conflict looks staged as
        // well as modified — and the file would appear in both panels, where unstaging it in one of
        // them is not a thing a user can meaningfully do.
        var file = new GitFileStatus("src/Conflicted.cs", code, IsUntracked: false);

        Assert.IsTrue(file.IsConflicted);
        Assert.IsTrue(file.HasWorkTreeChange);
        Assert.IsFalse(file.HasStagedChange);
        Assert.AreEqual("!", file.Describe(staged: false));
    }

    [TestMethod]
    public void AFileStagedAndModifiedAgainIsListedInBothPanels()
    {
        var file = new GitFileStatus("src/Foo.cs", "MM", IsUntracked: false);

        Assert.IsFalse(file.IsConflicted);
        Assert.IsTrue(file.HasStagedChange);
        Assert.IsTrue(file.HasWorkTreeChange);
    }

    [TestMethod]
    public void AnUntrackedFileIsNeitherStagedNorConflicted()
    {
        var file = new GitFileStatus("new.txt", "??", IsUntracked: true);

        Assert.IsFalse(file.IsConflicted);
        Assert.IsFalse(file.HasStagedChange);
        Assert.IsTrue(file.HasWorkTreeChange);
        Assert.AreEqual("?", file.Describe(staged: false));
    }
}
