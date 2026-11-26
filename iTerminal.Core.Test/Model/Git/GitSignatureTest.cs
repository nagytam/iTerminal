using iTerminal.Core.Model.Git;

namespace iTerminal.Core.Test.Model.Git;

/// <summary>The letters of git's %G? and what the commit list and the git:// drive show for them.</summary>
[TestClass]
public class GitSignatureTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void AnUnsignedCommitShowsNothing()
    {
        Assert.IsFalse(GitSignature.IsSigned('N'));
        Assert.AreEqual(string.Empty, GitSignature.Marker('N'));
        Assert.AreEqual(string.Empty, GitSignature.Describe('N'));
        Assert.AreEqual(string.Empty, GitSignature.Marker('\0'));
    }

    [Timeout(60_000)]
    [TestMethod]
    [DataRow('G', "[G] ")]
    [DataRow('B', "[B] ")]
    [DataRow('U', "[U] ")]
    [DataRow('X', "[X] ")]
    [DataRow('Y', "[Y] ")]
    [DataRow('R', "[R] ")]
    [DataRow('E', "[E] ")]
    public void ASignedCommitShowsItsLetter(char letter, string marker)
    {
        Assert.IsTrue(GitSignature.IsSigned(letter));
        Assert.AreEqual(marker, GitSignature.Marker(letter));
        Assert.IsTrue(GitSignature.Describe(letter).Length > 0);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Describe_NamesTheSignerInsideThePhrase_AndStaysEmptyForUnsigned()
    {
        string phrase = GitSignature.Describe('G', "Ann Example");

        StringAssert.Contains(phrase, "not checked for trust; signer: Ann Example)");
        Assert.AreEqual(GitSignature.Describe('G'), GitSignature.Describe('G', string.Empty));
        Assert.AreEqual(string.Empty, GitSignature.Describe('N', "Ann Example"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AGoodSignatureSaysTheKeyIsNotCheckedForTrust()
    {
        StringAssert.Contains(GitSignature.Describe('G'), "not checked for trust");
        StringAssert.Contains(GitSignature.Describe('B'), "BAD");
    }
}
