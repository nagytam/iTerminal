using System.Text;
using iConsole.Services;

namespace iConsole.Test.Services;

/// <summary>
/// Reading an Access database. No Access file is kept in the repository, so these tests cover the refusal of a file that
/// is not a JET database; the tables and rows are checked by hand against real .mdb and .accdb files.
/// </summary>
[TestClass]
public class AccessDatabaseTextReaderTest
{
    [TestMethod]
    public void AFileThatIsNotAJetDatabase_IsReportedNotRead()
    {
        string path = Path.Combine(Path.GetTempPath(), "iTerminal-access-" + Guid.NewGuid().ToString("N") + ".mdb");
        File.WriteAllText(path, "this is not an access database, just text", Encoding.ASCII);
        try
        {
            IReadOnlyList<string> lines = AccessDatabaseTextReader.Read(path);

            Assert.AreEqual(1, lines.Count, string.Join("\n", lines));
            Assert.IsTrue(lines[0].StartsWith("[Could not read the database:"), lines[0]);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
