using iTerminal.DevelopmentStudio.Services;

namespace iTerminal.DevelopmentStudio.Test.Services;

[TestClass]
public class BuildDiagnosticParserTest
{
    [TestMethod]
    public void AnMSBuildCompilerErrorGivesItsFilePositionCodeAndMessage()
    {
        var diagnostic = BuildDiagnosticParser.Parse("/src/App/Program.cs(3,5): error CS1002: ; expected [/src/App/App.csproj]");

        Assert.IsNotNull(diagnostic);
        Assert.IsTrue(diagnostic.IsError);
        Assert.AreEqual("/src/App/Program.cs", diagnostic.File);
        Assert.AreEqual(3, diagnostic.Line);
        Assert.AreEqual(5, diagnostic.Column);
        Assert.AreEqual("CS1002: ; expected", diagnostic.Message, "the project in brackets is not part of the message");
    }

    [TestMethod]
    public void AnMSBuildWarningIsNotAnError()
    {
        var diagnostic = BuildDiagnosticParser.Parse("/src/Program.cs(5,9): warning CS0168: The variable 'x' is declared but never used [/src/App.csproj]");

        Assert.IsNotNull(diagnostic);
        Assert.IsFalse(diagnostic.IsError);
        Assert.AreEqual("CS0168: The variable 'x' is declared but never used", diagnostic.Message);
    }

    [TestMethod]
    public void AWindowsPathWithoutAColumnKeepsItsDriveLetter()
    {
        var diagnostic = BuildDiagnosticParser.Parse(@"C:\src\Program.cs(7): error CS1002: ; expected");

        Assert.IsNotNull(diagnostic);
        Assert.AreEqual(@"C:\src\Program.cs", diagnostic.File);
        Assert.AreEqual(7, diagnostic.Line);
        Assert.AreEqual(0, diagnostic.Column, "no column was printed");
    }

    [TestMethod]
    public void AJavacStyleErrorIsRead()
    {
        var diagnostic = BuildDiagnosticParser.Parse("Main.java:3: error: ';' expected");

        Assert.IsNotNull(diagnostic);
        Assert.IsTrue(diagnostic.IsError);
        Assert.AreEqual("Main.java", diagnostic.File);
        Assert.AreEqual(3, diagnostic.Line);
        Assert.AreEqual("';' expected", diagnostic.Message);
    }

    [TestMethod]
    public void AGccStyleErrorWithAColumnAndAFatalErrorAreRead()
    {
        var error = BuildDiagnosticParser.Parse("src/x.c:10:5: error: 'foo' undeclared");
        var fatal = BuildDiagnosticParser.Parse("x.h:1:10: fatal error: missing.h: No such file");

        Assert.IsNotNull(error);
        Assert.AreEqual(5, error.Column);
        Assert.AreEqual("'foo' undeclared", error.Message);
        Assert.IsNotNull(fatal);
        Assert.IsTrue(fatal.IsError);
        Assert.AreEqual("missing.h: No such file", fatal.Message);
    }

    [TestMethod]
    public void AGccStyleWarningIsNotAnError()
    {
        var diagnostic = BuildDiagnosticParser.Parse("lib/a.c:4:2: warning: unused variable");

        Assert.IsNotNull(diagnostic);
        Assert.IsFalse(diagnostic.IsError);
    }

    [TestMethod]
    public void TheTextIsTheLineAsItWasPrintedWithoutItsIndent()
    {
        var diagnostic = BuildDiagnosticParser.Parse("    /src/Program.cs(1,1): error CS0001: boom");

        Assert.IsNotNull(diagnostic);
        Assert.AreEqual("/src/Program.cs(1,1): error CS0001: boom", diagnostic.Text);
    }

    [TestMethod]
    public void LinesThatAreNotDiagnosticsGiveNull()
    {
        string[] lines =
        [
            "Build succeeded.",
            "    0 Error(s)",
            "Time Elapsed 00:00:01.23",
            "Failed App.Test.Foo [12 ms]",
            "Error: something went wrong",
            "$ dotnet build",
            "",
            "   ",
        ];

        foreach (string line in lines)
        {
            Assert.IsNull(BuildDiagnosticParser.Parse(line), $"'{line}' is not a diagnostic");
        }
    }
}
