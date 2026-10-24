using System.Text;
using iConsole.Xml;

namespace iConsole.Test.Xml;

/// <summary>Validating a document against the schema or DTD it names, with schema files read from the disk next to it.</summary>
[TestClass]
public class XmlValidatorTest
{
    private const string Schema = """
        <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema">
          <xs:element name="book">
            <xs:complexType>
              <xs:sequence><xs:element name="title" type="xs:string"/></xs:sequence>
              <xs:attribute name="id" type="xs:int" use="required"/>
            </xs:complexType>
          </xs:element>
        </xs:schema>
        """;

    private string _directory = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _directory = Path.Combine(Path.GetTempPath(), "xmlvalidate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "book.xsd"), Schema);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            Directory.Delete(_directory, true);
        }
        catch
        {
        }
    }

    private string Document(string name, string xml)
    {
        string path = Path.Combine(_directory, name);
        File.WriteAllText(path, xml);
        return path;
    }

    private static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);

    [TestMethod]
    public void ADocumentThatMatchesItsSchema_HasNoIssues()
    {
        string path = Document("valid.xml", """
            <book xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xsi:noNamespaceSchemaLocation="book.xsd" id="1">
              <title>Dune</title>
            </book>
            """);

        IReadOnlyList<XmlValidationIssue> issues = XmlValidator.Validate(File.ReadAllBytes(path), path);

        CollectionAssert.AreEqual(Array.Empty<XmlValidationIssue>(), issues.ToList());
    }

    [TestMethod]
    public void AnElementTheSchemaDoesNotAllow_IsAnErrorAtItsLine()
    {
        string path = Document("wrong.xml", """
            <book xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xsi:noNamespaceSchemaLocation="book.xsd" id="1">
              <author>Frank Herbert</author>
            </book>
            """);

        XmlValidationIssue issue = XmlValidator.Validate(File.ReadAllBytes(path), path).Single(i => i.IsError);

        Assert.AreEqual(2, issue.Line);
        Assert.IsTrue(issue.Message.Contains("author"), issue.Message);
    }

    [TestMethod]
    public void AMissingRequiredAttribute_IsAnError()
    {
        string path = Document("noid.xml", """
            <book xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xsi:noNamespaceSchemaLocation="book.xsd">
              <title>Dune</title>
            </book>
            """);

        IReadOnlyList<XmlValidationIssue> issues = XmlValidator.Validate(File.ReadAllBytes(path), path);

        Assert.IsTrue(issues.Any(i => i.IsError && i.Message.Contains("id")), string.Join("\n", issues.Select(i => i.Message)));
    }

    [TestMethod]
    public void ASchemaThatIsNotThere_IsReported()
    {
        string path = Document("missing.xml", """
            <book xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xsi:noNamespaceSchemaLocation="gone.xsd" id="1"/>
            """);

        IReadOnlyList<XmlValidationIssue> issues = XmlValidator.Validate(File.ReadAllBytes(path), path);

        // The framework reports a schema it cannot find as a warning: the element then has no schema to check against.
        Assert.IsTrue(issues.Count > 0, "a schema that cannot be read is reported, not skipped");
        Assert.IsTrue(issues.All(i => !i.IsError), "a missing schema is a warning, the document itself is well-formed");
    }

    [TestMethod]
    public void ADtd_ValidatesTheDocumentAgainstItsDoctype()
    {
        string path = Document("note.xml", """
            <!DOCTYPE note [ <!ELEMENT note (to)> <!ELEMENT to (#PCDATA)> ]>
            <note><from>Ann</from></note>
            """);

        IReadOnlyList<XmlValidationIssue> issues = XmlValidator.Validate(File.ReadAllBytes(path), path);

        Assert.IsTrue(issues.Any(i => i.IsError && i.Message.Contains("from")), string.Join("\n", issues.Select(i => i.Message)));
    }

    [TestMethod]
    public void ANotWellFormedDocument_ReportsWhereItBreaks()
    {
        string path = Document("broken.xml", "<a>\n<b></a>");

        IReadOnlyList<XmlValidationIssue> issues = XmlValidator.Validate(File.ReadAllBytes(path), path);

        Assert.AreEqual(1, issues.Count);
        XmlValidationIssue issue = issues[0];
        Assert.IsTrue(issue.IsError);
        Assert.AreEqual(2, issue.Line);
    }

    [TestMethod]
    public void ADocumentThatNamesNoSchema_IsANoteNotAnError()
    {
        IReadOnlyList<XmlValidationIssue> issues = XmlValidator.Validate(Utf8("<plain/>"), null);

        XmlValidationIssue issue = issues.Single();
        Assert.IsFalse(issue.IsError);
        Assert.IsTrue(issue.Message.Contains("nothing to validate"), issue.Message);
    }
}
