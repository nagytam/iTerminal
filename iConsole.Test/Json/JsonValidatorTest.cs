using System.Text;
using iConsole.Json;

namespace iConsole.Test.Json;

[TestClass]
public class JsonValidatorTest
{
    private static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);

    [TestMethod]
    public void ValidJson_HasNoIssue()
    {
        Assert.IsNull(JsonValidator.Validate(Utf8("""{"a":[1,2,{"b":null}],"c":"d"}""")));
    }

    [TestMethod]
    public void AMissingComma_IsReportedAtItsLineAndColumn()
    {
        JsonValidationIssue? issue = JsonValidator.Validate(Utf8("{\n  \"a\": 1\n  \"b\": 2\n}"));

        Assert.IsNotNull(issue);
        Assert.AreEqual(3, issue.Line);
        Assert.AreEqual(3, issue.Column);
    }

    [TestMethod]
    public void TrailingComma_IsNotValidJson()
    {
        Assert.IsNotNull(JsonValidator.Validate(Utf8("[1, 2,]")));
    }

    [TestMethod]
    public void Comments_AreNotValidJson()
    {
        Assert.IsNotNull(JsonValidator.Validate(Utf8("// settings\n{}")));
    }

    [TestMethod]
    public void AnEmptyFile_IsNotValidJson()
    {
        Assert.IsNotNull(JsonValidator.Validate([]));
    }

    [TestMethod]
    public void ANestingTooDeep_IsReportedNotCrashed()
    {
        string deep = new string('[', JsonTree.MaxDepth + 1) + new string(']', JsonTree.MaxDepth + 1);

        Assert.IsNotNull(JsonValidator.Validate(Utf8(deep)));
    }
}
