using Microsoft.VisualStudio.TestTools.UnitTesting;
using iConsole.Commands;
using iConsole.Model;

namespace iConsole.Commands.Test;

/// <summary>Tab in a text box with suggestions: complete from the matches for what is typed now.</summary>
[TestClass]
public class AutoCompleteCommandTest
{
    [TestMethod]
    public async Task CompletesToTheFirstMatch()
    {
        var text = new TextModel("cry");
        var completions = new CompletionModel([@"C:\projects\crypton", @"C:\other"]);
        completions.UpdateMatchingValues("C:\\projects");

        await new AutoCompleteCommand(text, completions).ExecuteAsync(null);

        Assert.AreEqual(@"C:\projects\crypton", text.Lines[0]);
    }

    [TestMethod]
    public async Task WithNoMatchTheTextIsLeftAlone()
    {
        var text = new TextModel("zzz");
        var completions = new CompletionModel(["abc"]);
        completions.UpdateMatchingValues("zzz");

        await new AutoCompleteCommand(text, completions).ExecuteAsync(null);

        Assert.AreEqual("zzz", text.Lines[0]);
    }

    [TestMethod]
    public async Task ATabPressedBeforeTheMatchUpdateLandsWaitsForIt()
    {
        // Matches are computed on a background task per keystroke; a quick Tab used to read the
        // previous keystroke's matches — here, none — and do nothing.
        var text = new TextModel("cry");
        var completions = new CompletionModel([@"C:\projects\crypton"]);
        completions.UpdateMatchingValues("no match yet");
        completions.UpdateTask = Task.Run(async () =>
        {
            await Task.Delay(40);
            completions.UpdateMatchingValues(@"C:\projects");
        });

        await new AutoCompleteCommand(text, completions).ExecuteAsync(null);

        Assert.AreEqual(@"C:\projects\crypton", text.Lines[0]);
    }

    [TestMethod]
    public async Task AStuckUpdateDoesNotHoldTheKey()
    {
        var text = new TextModel("cry");
        var completions = new CompletionModel(["x"]);
        completions.UpdateTask = new TaskCompletionSource().Task;

        var started = DateTime.UtcNow;
        await new AutoCompleteCommand(text, completions).ExecuteAsync(null);

        Assert.IsTrue(DateTime.UtcNow - started < TimeSpan.FromSeconds(1), "bounded well inside the key budget");
    }

    [TestMethod]
    public void TabIsTheKey()
    {
        var key = new AutoCompleteCommand(new TextModel(), new CompletionModel([])).Key;

        Assert.AreEqual(ConsoleKey.Tab, key!.ConsoleKey);
        Assert.IsFalse(key.Shift || key.Alt || key.Control);
    }
}
