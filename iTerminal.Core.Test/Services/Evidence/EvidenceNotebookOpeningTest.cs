using iTerminal.Core.Commands.Evidence;
using iTerminal.Core.Test.Helper;

namespace iTerminal.Core.Test.Services.Evidence;

/// <summary>The folder the open prompt names: a typed path becomes a full path, or nothing when it names no folder.</summary>
[TestClass]
public class EvidenceNotebookOpeningTest
{
    [TestMethod]
    [DataRow("~/cases/case-42", true)]
    [DataRow("   ", false)]
    [DataRow("relative/folder", true)]
    public void TheTypedFolderResolvesToAFullPathOrNothing(string typed, bool usable)
    {
        var resolved = EvidenceNotebookOpening.Resolve(typed);

        Assert.AreEqual(usable, resolved is not null);
        if (resolved is not null)
        {
            Assert.IsTrue(Path.IsPathFullyQualified(resolved));
        }
    }

    [TestMethod]
    public void ATildeMeansTheHomeFolder()
    {
        var resolved = EvidenceNotebookOpening.Resolve("~/cases");

        Assert.IsNotNull(resolved);
        Assert.IsTrue(resolved.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), StringComparison.Ordinal));
    }
}
