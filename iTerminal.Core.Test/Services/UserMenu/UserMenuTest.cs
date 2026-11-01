using iTerminal.Core.Services.UserMenu;

namespace iTerminal.Core.Test.Services.UserMenu;

[TestClass]
public class UserMenuTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void Parse_ReadsKeysTitlesScriptsAndPatterns()
    {
        var items = UserMenuParser.Parse(
            "# comment\n" +
            "a   First\n" +
            "    echo one\n" +
            "    echo two\n" +
            "\n" +
            "+ f *.c;*.h\n" +
            "b   Only C\n" +
            "    gcc %f\n");

        Assert.AreEqual(2, items.Count);
        Assert.AreEqual('a', items[0].Hotkey);
        Assert.AreEqual("First", items[0].Title);
        CollectionAssert.AreEqual(new[] { "echo one", "echo two" }, items[0].Lines.ToArray());
        Assert.AreEqual("*", items[0].Patterns);
        Assert.AreEqual("*.c;*.h", items[1].Patterns);
        Assert.IsTrue(items[0].AppliesTo("x.txt"));
        Assert.IsTrue(items[1].AppliesTo("main.c"));
        Assert.IsFalse(items[1].AppliesTo("main.txt"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Expand_QuotesNamesAndKnowsTheMarks()
    {
        var context = new UserMenuContext("my file.txt", "C:\\src", "C:\\other", ["a.txt", "b c.txt"]);

        var quoted = UserMenuMacros.Quote("my file.txt");
        Assert.AreEqual(quoted + " 100%", UserMenuMacros.Expand("%f 100%%", context));
        Assert.AreEqual("txt", UserMenuMacros.Expand("%x", context));
        Assert.AreEqual(
            UserMenuMacros.Quote("a.txt") + " " + UserMenuMacros.Quote("b c.txt"),
            UserMenuMacros.Expand("%s", context));
        Assert.AreEqual(quoted, UserMenuMacros.Expand("%s", context with { MarkedFiles = [] }));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void GetItems_WritesTheDefaultMenuTheFirstTime()
    {
        var folder = Path.Combine(Path.GetTempPath(), "iterminal-usermenu-" + Guid.NewGuid().ToString("N"));
        try
        {
            var service = new UserMenuService(
                Path.Combine(folder, UserMenuService.FileName),
                new Moq.Mock<iTerminal.Core.Services.Shell.IInteractiveShellService>().Object,
                new Moq.Mock<iConsole.Interfaces.IContentModelService>().Object);

            var items = service.GetItems("notes.txt");

            Assert.IsTrue(File.Exists(service.FilePath));
            Assert.IsTrue(items.Count >= 2);
            Assert.IsFalse(items.Any(i => i.Title == "Unpack with tar"), "a .tar.gz-only entry must not be offered for a .txt");
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }
        }
    }
}
