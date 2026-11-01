using Microsoft.VisualStudio.TestTools.UnitTesting;
using iConsole.Commands;
using iConsole.Controls.Editors;
using iConsole.Data;
using iConsole.Display;
using iConsole.Model;
using iConsole.Services;
using Moq;

namespace iConsole.Commands.Test;

[TestClass]
public class DeleteLineCommandTest
{
    [Timeout(60_000)]
    [TestMethod]
    public async Task ExecuteAsync_ShouldClearLineAndResetScroll()
    {
        var textModel = new TextModel("line1");
        var scrollable = new ScrollableModel();
        scrollable.CursorPosition = new Position(3, 0);
        scrollable.ScrollPosition = new Position(1, 0);

        var cmd = new DeleteLineCommand(textModel, scrollable);
        await cmd.ExecuteAsync(null);

        Assert.AreEqual(string.Empty, textModel.Lines[0]);
        Assert.AreEqual(0, scrollable.CursorPosition.X);
        Assert.AreEqual(0, scrollable.ScrollPosition.X);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ExecuteAsync_OnlyClearsTheCursorLine_AndKeepsTheLinesBelowInPlace()
    {
        var textModel = new TextModel("one");
        textModel.Lines.Add("two");
        textModel.Lines.Add("three");
        var scrollable = new ScrollableModel();
        scrollable.CursorPosition = new Position(2, 1);

        await new DeleteLineCommand(textModel, scrollable).ExecuteAsync(null);

        CollectionAssert.AreEqual(new[] { "one", string.Empty, "three" }, textModel.Lines.AsSnapshot().ToArray());
        Assert.AreEqual(1, scrollable.CursorPosition.Y);
        Assert.AreEqual(0, scrollable.CursorPosition.X);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ExecuteAsync_OnAnEmptyDocument_DoesNothing()
    {
        var textModel = new TextModel();
        textModel.Lines.Clear();

        await new DeleteLineCommand(textModel, new ScrollableModel()).ExecuteAsync(null);

        Assert.AreEqual(0, textModel.Lines.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ExecuteAsync_WithACursorBelowTheLastLine_ClearsTheLastLine()
    {
        var textModel = new TextModel("one");
        textModel.Lines.Add("two");
        var scrollable = new ScrollableModel();
        scrollable.CursorPosition = new Position(0, 9);

        await new DeleteLineCommand(textModel, scrollable).ExecuteAsync(null);

        CollectionAssert.AreEqual(new[] { "one", string.Empty }, textModel.Lines.AsSnapshot().ToArray());
        Assert.AreEqual(1, scrollable.CursorPosition.Y);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ExecuteAsync_MarksTheEditorDirty_OnlyWhenTheLineHadText()
    {
        var theme = new Mock<IThemeService>();
        theme.Setup(t => t.CurrentColorScheme).Returns(new ColorScheme());
        var editor = new TextEditorControl(theme.Object);
        editor.TextModel.Lines.Add(string.Empty);
        var cmd = new DeleteLineCommand(editor.TextModel, editor.ScrollableModel) { Control = editor };

        await cmd.ExecuteAsync(null);
        Assert.IsFalse(editor.IsDirty);

        editor.TextModel.Lines[0] = "text";
        await cmd.ExecuteAsync(null);
        Assert.IsTrue(editor.IsDirty);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void CanExecute_IsFalseInAReadOnlyEditor()
    {
        var theme = new Mock<IThemeService>();
        theme.Setup(t => t.CurrentColorScheme).Returns(new ColorScheme());
        var editor = new TextEditorControl(theme.Object) { IsReadOnly = true };
        var cmd = new DeleteLineCommand(editor.TextModel, editor.ScrollableModel) { Control = editor };

        Assert.IsFalse(cmd.CanExecute());
    }
}
