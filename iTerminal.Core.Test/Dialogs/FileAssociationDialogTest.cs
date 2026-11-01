using iConsole.Controls;
using iConsole.Services;
using iConsole.Test.Core;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Services.FileAssociations;
using Moq;
using StringAssert = Microsoft.VisualStudio.TestTools.UnitTesting.StringAssert;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Test.Dialogs;

/// <summary>The form that adds or changes a program in the "Open with" list: what it asks, what it refuses and what it saves.</summary>
[TestClass]
public class FileAssociationDialogTest
{
    private sealed class Rig
    {
        public required FileAssociationDialog Dialog { get; init; }

        public required Control Root { get; init; }

        public required List<FileAssociation> Saved { get; init; }
    }

    private static async Task<Rig> OpenAsync(Action<FileAssociationDialog> load)
    {
        List<FileAssociation> saved = [];
        Mock<IFileAssociationService> service = new();
        service.Setup(s => s.Save(It.IsAny<FileAssociation>())).Callback<FileAssociation>(saved.Add);
        FileAssociationDialog dialog = new(service.Object, Mock.Of<IBeepService>(), Mock.Of<IDateTimeProviderService>(), TestTheme());
        load(dialog);
        Control root = await ShowAsync(dialog);
        return new Rig { Dialog = dialog, Root = root, Saved = saved };
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ANewProgram_StartsEmpty_WithTheFilesItWasOpenedFor()
    {
        Rig rig = await OpenAsync(d => d.LoadNew(FileAssociationKind.Open, "*.md"));

        Assert.IsNull(rig.Dialog.Existing);
        Assert.AreEqual("*.md", rig.Dialog.Patterns);
        Assert.AreEqual(string.Empty, rig.Dialog.Command);
        Assert.IsFalse(rig.Dialog.InTerminal);
        ScreenBuffer screen = Render(rig.Root);
        AssertScreenContains(screen, "Add a program to open with");
        AssertScreenContains(screen, "e.g. code %f");
        AssertScreenContains(screen, "%f file  %d folder  %n name");
        AssertScreenContains(screen, "Runs in the terminal");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ForEditing_TheTitleSaysEdit()
    {
        Rig rig = await OpenAsync(d => d.LoadNew(FileAssociationKind.Edit, "*"));

        AssertScreenContains(Render(rig.Root), "Add a program to edit with");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AnExistingProgram_ShowsItsFields_AndSaysItIsBeingChanged()
    {
        FileAssociation existing = new("id1", "VS Code", "*.cs;*.md", "code %f", FileAssociationKind.Edit, true);
        Rig rig = await OpenAsync(d => d.LoadExisting(existing));

        Assert.AreEqual(existing, rig.Dialog.Existing);
        Assert.AreEqual("VS Code", rig.Dialog.AssociationName);
        Assert.AreEqual("code %f", rig.Dialog.Command);
        Assert.IsTrue(rig.Dialog.InTerminal);
        ScreenBuffer screen = Render(rig.Root);
        AssertScreenContains(screen, "Change the program to edit with");
        AssertScreenContains(screen, "VS Code");
        AssertScreenContains(screen, "*.cs;*.md");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task WithoutACommand_SaveSaysWhatIsMissing_AndKeepsTheForm()
    {
        Rig rig = await OpenAsync(d => d.LoadNew(FileAssociationKind.Open, "*.md"));

        Assert.IsFalse(rig.Dialog.Save());

        StringAssert.Contains(rig.Dialog.Error, "Type the command");
        AssertScreenContains(Render(rig.Root), "Type the command");
        Assert.AreEqual(0, rig.Saved.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task WithoutPatterns_SaveSaysWhichFilesAreNeeded()
    {
        Rig rig = await OpenAsync(d => d.LoadNew(FileAssociationKind.Open, string.Empty));
        rig.Dialog.Command = "code %f";

        Assert.IsFalse(rig.Dialog.Save());

        StringAssert.Contains(rig.Dialog.Error, "Say which files");
        Assert.AreEqual(0, rig.Saved.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ABlankCommand_CountsAsNone()
    {
        Rig rig = await OpenAsync(d => d.LoadNew(FileAssociationKind.Open, "*"));
        rig.Dialog.Command = "   ";

        Assert.IsFalse(rig.Dialog.Save());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ANewProgramWithoutAName_TakesTheProgramsName()
    {
        Rig rig = await OpenAsync(d => d.LoadNew(FileAssociationKind.Open, " *.md "));
        string command = "\"" + Path.Combine(Path.GetTempPath(), "Tools", "typora.exe") + "\" %f";
        rig.Dialog.Command = command;

        Assert.IsTrue(rig.Dialog.Save());

        FileAssociation saved = rig.Saved.Single();
        Assert.AreEqual("typora", saved.Name);
        Assert.AreEqual("*.md", saved.Patterns);
        Assert.AreEqual(command, saved.Command);
        Assert.AreEqual(FileAssociationKind.Open, saved.Kind);
        Assert.IsFalse(string.IsNullOrEmpty(saved.Id));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AChangedProgram_KeepsItsId_AndTheNewValues()
    {
        FileAssociation existing = new("id1", "Old", "*.txt", "notepad", FileAssociationKind.Open, false);
        Rig rig = await OpenAsync(d => d.LoadExisting(existing));
        rig.Dialog.AssociationName = "  New  ";
        rig.Dialog.InTerminal = true;

        Assert.IsTrue(rig.Dialog.Save());

        FileAssociation saved = rig.Saved.Single();
        Assert.AreEqual("id1", saved.Id);
        Assert.AreEqual("New", saved.Name);
        Assert.IsTrue(saved.InTerminal);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Enter_SavesAndCloses_WhenComplete()
    {
        Rig rig = await OpenAsync(d => d.LoadNew(FileAssociationKind.Open, "*.txt"));
        rig.Dialog.Command = "notepad";

        await rig.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter));

        Assert.AreEqual(1, rig.Saved.Count);
        Assert.IsFalse(rig.Dialog.IsVisible);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Enter_OnAnIncompleteForm_StaysOpenSayingWhy()
    {
        Rig rig = await OpenAsync(d => d.LoadNew(FileAssociationKind.Open, "*.txt"));

        await rig.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter));

        Assert.IsTrue(rig.Dialog.IsVisible);
        Assert.AreEqual(0, rig.Saved.Count);
        AssertScreenContains(Render(rig.Root), "Type the command");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Escape_ClosesWithoutSaving()
    {
        Rig rig = await OpenAsync(d => d.LoadNew(FileAssociationKind.Open, "*.txt"));
        rig.Dialog.Command = "notepad";

        await rig.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Escape));

        Assert.IsFalse(rig.Dialog.IsVisible);
        Assert.AreEqual(0, rig.Saved.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ReopeningForAnotherProgram_ClearsTheLastError()
    {
        Rig rig = await OpenAsync(d => d.LoadNew(FileAssociationKind.Open, "*.txt"));
        rig.Dialog.Save();
        Assert.AreNotEqual(string.Empty, rig.Dialog.Error);

        await rig.Dialog.OnInitializeAsync();

        Assert.AreEqual(string.Empty, rig.Dialog.Error);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TypingAfterAnError_ClearsIt_ButEnterKeepsIt()
    {
        Rig rig = await OpenAsync(d => d.LoadNew(FileAssociationKind.Open, "*.txt"));
        await rig.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter));
        StringAssert.Contains(rig.Dialog.Error, "Type the command");

        await rig.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Tab, '\t'));
        StringAssert.Contains(rig.Dialog.Error, "Type the command");

        await rig.Dialog.HandleKeyEventAsync(Char('n'));

        Assert.AreEqual(string.Empty, rig.Dialog.Error);
        Assert.AreEqual("n", rig.Dialog.AssociationName, "Tab moved on to the name box, which got the letter");
    }
}
