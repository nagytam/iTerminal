using iTerminal.Core.Rename;

namespace iTerminal.Core.Test.Rename;

[TestClass]
public class MultiRenamePatternTest
{
    [TestMethod]
    public void TheDefaultPatternRenamesNothing()
    {
        Assert.AreEqual("a.txt", MultiRenamePattern.Expand(MultiRenamePattern.DefaultPattern, "a.txt", isFolder: false, 1, string.Empty, string.Empty));
        Assert.AreEqual("Photos", MultiRenamePattern.Expand(MultiRenamePattern.DefaultPattern, "Photos", isFolder: true, 1, string.Empty, string.Empty));
    }

    [TestMethod]
    public void TheCounterStartsAtOneAndIsPaddedToTheWidthGiven()
    {
        Assert.AreEqual("a_1.txt", MultiRenamePattern.Expand("[C]_[N]", "a.txt", false, 1, string.Empty, string.Empty));
        Assert.AreEqual("a_007.txt", MultiRenamePattern.Expand("[C]_[N:3]", "a.txt", false, 7, string.Empty, string.Empty));
        Assert.AreEqual("a_0012.txt", MultiRenamePattern.Expand("[C]_[N:4]", "a.txt", false, 12, string.Empty, string.Empty));
    }

    [TestMethod]
    public void AFileKeepsItsExtensionWhenThePatternDoesNotNameIt()
    {
        Assert.AreEqual("holiday_02.jpg", MultiRenamePattern.Expand("holiday_[N:2]", "IMG_9.jpg", false, 2, string.Empty, string.Empty));
    }

    [TestMethod]
    public void TheExtensionTokenPlacesTheExtensionWhereThePatternPutsIt()
    {
        Assert.AreEqual("a.bak.txt", MultiRenamePattern.Expand("[C].bak", "a.txt", false, 1, string.Empty, string.Empty),
            "without [E] in the pattern the extension is kept, after it");
        Assert.AreEqual("txt-a", MultiRenamePattern.Expand("[E]-[C]", "a.txt", false, 1, string.Empty, string.Empty));
    }

    [TestMethod]
    public void ADotfileHasNoExtension()
    {
        Assert.AreEqual(".bashrc", MultiRenamePattern.Expand("[C]", ".bashrc", false, 1, string.Empty, string.Empty));
        Assert.AreEqual("x_.bashrc", MultiRenamePattern.Expand("x_[C]", ".bashrc", false, 1, string.Empty, string.Empty));
        Assert.AreEqual(string.Empty, MultiRenamePattern.Expand("[E]", ".bashrc", false, 1, string.Empty, string.Empty));
    }

    [TestMethod]
    public void ANameWithoutAnExtensionKeepsNothingExtra()
    {
        Assert.AreEqual("README_1", MultiRenamePattern.Expand("[C]_[N]", "README", false, 1, string.Empty, string.Empty));
        Assert.AreEqual("a.", MultiRenamePattern.Expand("[C]", "a.", false, 1, string.Empty, string.Empty),
            "a trailing dot is part of the name, not an empty extension");
    }

    [TestMethod]
    public void AFolderHasNoExtensionSoItsWholeNameIsTheStem()
    {
        Assert.AreEqual("v1.2_backup", MultiRenamePattern.Expand("[C]_backup", "v1.2", isFolder: true, 1, string.Empty, string.Empty));
        Assert.AreEqual(string.Empty, MultiRenamePattern.Expand("[E]", "v1.2", isFolder: true, 1, string.Empty, string.Empty));
    }

    [TestMethod]
    public void FindAndReplaceApplyToTheExpandedPatternAsPlainText()
    {
        Assert.AreEqual("IMG-1.jpg", MultiRenamePattern.Expand("[C]", "IMG_1.jpg", false, 1, "_", "-"));
        Assert.AreEqual("a+b.txt", MultiRenamePattern.Expand("[C]", "a.b.txt", false, 1, ".", "+"));
        // The counter is expanded first ("01"), then Find/Replace runs on the result.
        Assert.AreEqual("0x_0.txt", MultiRenamePattern.Expand("[N:2]", "a.txt", false, 1, "1", "x_0"));
    }

    [TestMethod]
    public void AnUnknownTokenIsCopiedAsTyped()
    {
        Assert.AreEqual("[X]_a.txt", MultiRenamePattern.Expand("[X]_[C]", "a.txt", false, 1, string.Empty, string.Empty));
    }

    [TestMethod]
    public void AnEmptyNameStaysEmptyAndIsNotAHiddenFile()
    {
        // Appending the extension to nothing would give ".pdf", a hidden file the planner would accept.
        Assert.AreEqual(string.Empty, MultiRenamePattern.Expand("[C]", "report.pdf", false, 1, "report", string.Empty));
        Assert.AreEqual(string.Empty, MultiRenamePattern.Expand(string.Empty, "report.pdf", false, 1, string.Empty, string.Empty));
    }
}
