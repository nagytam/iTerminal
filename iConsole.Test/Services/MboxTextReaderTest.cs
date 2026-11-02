using System.Text;
using iConsole.Services;

namespace iConsole.Test.Services;

/// <summary>Reading a mailbox: each message is split at its "From " line and shown like an .eml file.</summary>
[TestClass]
public class MboxTextReaderTest
{
    private const string Mailbox =
        "From alice@example.com Mon Mar  2 09:00:00 2026\n"
        + "From: Alice <alice@example.com>\n"
        + "To: Bob <bob@example.com>\n"
        + "Subject: First\n"
        + "Date: Mon, 2 Mar 2026 09:00:00 +0000\n"
        + "\n"
        + "Hello Bob.\n"
        + ">From the archive: quoted line.\n"
        + "\n"
        + "From bob@example.com Mon Mar  2 10:00:00 2026\n"
        + "From: Bob <bob@example.com>\n"
        + "To: Alice <alice@example.com>\n"
        + "Subject: Re: First\n"
        + "Date: Mon, 2 Mar 2026 10:00:00 +0000\n"
        + "\n"
        + "Thanks, Alice.\n";

    private static IReadOnlyList<string> Read(string text) => MboxTextReader.Read(Encoding.UTF8.GetBytes(text));

    [TestMethod]
    public void EachMessage_IsNumberedAndShownWithItsHeaders()
    {
        IReadOnlyList<string> lines = Read(Mailbox);

        Assert.AreEqual("Message 1 of 2", lines[0]);
        CollectionAssert.Contains(lines.ToArray(), "Subject: First");
        Assert.IsTrue(lines.Contains("Message 2 of 2"), string.Join("\n", lines));
        CollectionAssert.Contains(lines.ToArray(), "Subject: Re: First");
    }

    [TestMethod]
    public void QuotedFromLines_AreUnquoted()
    {
        IReadOnlyList<string> lines = Read(Mailbox);

        CollectionAssert.Contains(lines.ToArray(), "From the archive: quoted line.");
        Assert.IsFalse(lines.Any(l => l.StartsWith(">From")));
    }

    [TestMethod]
    public void AMessageLine_IsNotMistakenForAMessageStart()
    {
        // Only a line that begins with "From " starts a message; "From:" in a header does not.
        IReadOnlyList<string> lines = Read("From a@example.com Mon Mar  2 09:00:00 2026\nFrom: a@example.com\nSubject: One\n\nBody\n");

        Assert.IsTrue(lines.Contains("Message 1 of 1"), string.Join("\n", lines));
    }

    [TestMethod]
    public void AFileWithNoMessages_SaysSo()
    {
        CollectionAssert.AreEqual(new[] { "[No messages were found in this file]" }, Read("not a mailbox\n").ToArray());
    }
}
