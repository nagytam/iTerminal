using System.Text;
using iConsole.Services;
using OpenMcdf;

namespace iConsole.Test.Services;

/// <summary>Reading an Outlook .msg: the MAPI property streams of the compound file, and its attachments.</summary>
[TestClass]
public class OutlookMessageReaderTest
{
    private static void Unicode(RootStorage root, string name, string value) => Stream(root, name, Encoding.Unicode.GetBytes(value));

    private static void Stream(RootStorage root, string name, byte[] bytes)
    {
        CfbStream stream = root.CreateStream(name);
        stream.Write(bytes, 0, bytes.Length);
        stream.Flush();
    }

    private static byte[] MakeMsg(bool withAttachment)
    {
        using MemoryStream file = new();
        using (RootStorage root = RootStorage.Create(file, OpenMcdf.Version.V3, StorageModeFlags.LeaveOpen | StorageModeFlags.Transacted))
        {
            Unicode(root, "__substg1.0_0037001F", "Quarterly report");
            Unicode(root, "__substg1.0_0C1A001F", "Ann");
            Unicode(root, "__substg1.0_0C1F001F", "ann@example.org");
            Unicode(root, "__substg1.0_0E04001F", "Bob <bob@example.org>");
            Unicode(root, "__substg1.0_1000001F", "Body text line.\r\nSecond line.");
            Stream(root, "__substg1.0_00390040", BitConverter.GetBytes(new DateTime(2024, 1, 15, 9, 30, 0, DateTimeKind.Utc).ToFileTimeUtc()));
            if (withAttachment)
            {
                Storage attachment = root.CreateStorage("__attach_version1.0_#00000000");
                CfbStream name = attachment.CreateStream("__substg1.0_3707001F");
                byte[] bytes = Encoding.Unicode.GetBytes("report.pdf");
                name.Write(bytes, 0, bytes.Length);
                name.Flush();
            }

            root.Flush(true);
            root.Commit();
        }

        return file.ToArray();
    }

    [TestMethod]
    public void AMessage_ShowsSenderRecipientDateSubjectAndBody()
    {
        List<string> lines = [.. OutlookMessageReader.Read(MakeMsg(withAttachment: false))];

        CollectionAssert.Contains(lines, "From: Ann <ann@example.org>");
        CollectionAssert.Contains(lines, "To: Bob <bob@example.org>");
        CollectionAssert.Contains(lines, "Date: 2024-01-15 09:30 UTC");
        CollectionAssert.Contains(lines, "Subject: Quarterly report");
        CollectionAssert.Contains(lines, "Body text line.");
        CollectionAssert.Contains(lines, "Second line.");
    }

    [TestMethod]
    public void Attachments_AreListedByName()
    {
        List<string> lines = [.. OutlookMessageReader.Read(MakeMsg(withAttachment: true))];

        CollectionAssert.Contains(lines, "[Attachment: report.pdf]");
    }

    [TestMethod]
    public void NotACompoundFile_TheReaderSaysSo()
    {
        List<string> lines = [.. OutlookMessageReader.Read(Encoding.ASCII.GetBytes("not an outlook message"))];

        Assert.IsTrue(lines.Single().StartsWith("[Could not read the message:"), string.Join("\n", lines));
    }
}
