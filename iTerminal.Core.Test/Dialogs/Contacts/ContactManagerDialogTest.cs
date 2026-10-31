using iTerminal.Core.Dialogs.Contacts;
using iTerminal.Core.Model.Contacts;
using iTerminal.Core.Services;
using Moq;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Test.Dialogs.Contacts;

// Some tests put text on the ambient clipboard and paste it back, so the class does not run beside others that do.
[DoNotParallelize]
[TestClass]
public class ContactManagerDialogTest
{
    private static (ContactManagerDialog Dialog, ContactStore Store) Create()
    {
        var store = new ContactStore(new Mock<ISettingsService>().Object);
        return (new ContactManagerDialog(store, TestTheme()), store);
    }

    private static Contact Make(string name, string email = "") => new(Guid.NewGuid(), name, email, "", "", "");

    private static async Task TypeAsync(ContactManagerDialog dialog, string text)
    {
        foreach (var c in text)
        {
            await dialog.HandleKeyEventAsync(Char(c));
        }
    }

    private static Task EnterAsync(ContactManagerDialog dialog) => dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter, '\r'));

    private static async Task EnterTimesAsync(ContactManagerDialog dialog, int times)
    {
        for (var i = 0; i < times; i++)
        {
            await EnterAsync(dialog);
        }
    }

    [TestMethod]
    public async Task AContactTypedFieldByFieldIsAddedAndHighlighted()
    {
        var (dialog, store) = Create();
        await ShowAsync(dialog);

        await dialog.HandleKeyEventAsync(Char('n'));
        await TypeAsync(dialog, "Ada Lovelace");
        await EnterAsync(dialog);
        await TypeAsync(dialog, "ada@example.com");
        await EnterAsync(dialog);
        await TypeAsync(dialog, "+44 20 7946 0958");
        await EnterAsync(dialog);
        await TypeAsync(dialog, "Analytical Engines");
        await EnterAsync(dialog);
        await TypeAsync(dialog, "met at the fair");
        await EnterAsync(dialog);

        var saved = store.Find().Single();
        Assert.AreEqual(("Ada Lovelace", "ada@example.com", "+44 20 7946 0958", "Analytical Engines", "met at the fair"),
            (saved.Name, saved.Email, saved.Phone, saved.Company, saved.Notes));
        Assert.AreEqual(saved.Id, dialog.SelectedForTest!.Id);
        Assert.IsFalse(dialog.IsFormOpenForTest);
        AssertScreenContains(Render(dialog), "ada@example.com");
    }

    [TestMethod]
    public async Task AnEmptyNameIsRefusedAndTheFormStaysOpen()
    {
        var (dialog, store) = Create();
        await ShowAsync(dialog);

        await dialog.HandleKeyEventAsync(Char('n'));
        await EnterTimesAsync(dialog, 5);

        Assert.IsTrue(dialog.IsFormOpenForTest);
        StringAssert.Contains(dialog.ErrorForTest, "name");
        Assert.AreEqual(0, store.Count);
        AssertScreenContains(Render(dialog), "A contact needs a name");
    }

    [TestMethod]
    public async Task ABadEmailIsRefusedAndTheErrorClearsOnTheNextKey()
    {
        var (dialog, store) = Create();
        await ShowAsync(dialog);

        await dialog.HandleKeyEventAsync(Char('n'));
        await TypeAsync(dialog, "Ada");
        await EnterAsync(dialog);
        await TypeAsync(dialog, "not-an-email");
        await EnterTimesAsync(dialog, 4);

        Assert.IsTrue(dialog.IsFormOpenForTest);
        StringAssert.Contains(dialog.ErrorForTest, "email");
        Assert.AreEqual(0, store.Count);

        await TypeAsync(dialog, "x");
        Assert.IsNull(dialog.ErrorForTest);
    }

    [TestMethod]
    public async Task EscInTheFormDiscardsItWithoutClosingTheDialog()
    {
        var (dialog, store) = Create();
        await ShowAsync(dialog);

        await dialog.HandleKeyEventAsync(Char('n'));
        await TypeAsync(dialog, "Ada");
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Escape));

        Assert.IsFalse(dialog.IsFormOpenForTest);
        Assert.AreEqual(0, store.Count);
        Assert.IsTrue(dialog.IsVisible);
    }

    [TestMethod]
    public async Task EChangesTheHighlightedContactAndKeepsItsOtherFields()
    {
        var (dialog, store) = Create();
        var ada = new Contact(Guid.NewGuid(), "Ada", "ada@example.com", "123", "Co", "note");
        store.Add(ada);
        await ShowAsync(dialog);

        await dialog.HandleKeyEventAsync(Char('e'));
        await TypeAsync(dialog, " Lovelace");
        await EnterTimesAsync(dialog, 5);

        var changed = store.Find().Single();
        Assert.AreEqual(ada.Id, changed.Id);
        Assert.AreEqual(("Ada Lovelace", "ada@example.com", "123", "Co", "note"),
            (changed.Name, changed.Email, changed.Phone, changed.Company, changed.Notes));
    }

    [TestMethod]
    public async Task ShiftTabGoesBackAField()
    {
        var (dialog, store) = Create();
        await ShowAsync(dialog);

        await dialog.HandleKeyEventAsync(Char('n'));
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Tab, '\t'));
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Tab, '\t', shift: true));
        await TypeAsync(dialog, "Ada");
        await EnterTimesAsync(dialog, 5);

        Assert.AreEqual("Ada", store.Find().Single().Name);
    }

    [TestMethod]
    public async Task DeleteAsksFirstAndOnlyYRemovesTheContact()
    {
        var (dialog, store) = Create();
        store.Add(Make("Alice"));
        store.Add(Make("Bob"));
        await ShowAsync(dialog);

        await dialog.HandleKeyEventAsync(Char('d'));
        Assert.IsTrue(dialog.ConfirmingDeleteForTest);
        AssertScreenContains(Render(dialog), "Delete \"Alice\"? (y/n)");
        await dialog.HandleKeyEventAsync(Char('x'));
        Assert.AreEqual(2, store.Count);

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Delete));
        await dialog.HandleKeyEventAsync(Char('y'));

        Assert.AreEqual("Bob", store.Find().Single().Name);
        Assert.AreEqual("Bob", dialog.SelectedForTest!.Name);
    }

    [TestMethod]
    public async Task TheSlashFiltersTheListAsItIsTyped()
    {
        var (dialog, store) = Create();
        store.Add(Make("Alice", "alice@example.com"));
        store.Add(Make("Bob", "bob@work.org"));
        await ShowAsync(dialog);

        await dialog.HandleKeyEventAsync(Char('/'));
        await TypeAsync(dialog, "work");

        Assert.IsTrue(dialog.IsFilteringForTest);
        Assert.AreEqual("Bob", dialog.SelectedForTest!.Name);
        var screen = Render(dialog);
        // The words do not fit beside the last column title at this width, so the short form stands in for them.
        AssertScreenContains(screen, "1/2");
        AssertScreenContains(screen, "Company");
        Assert.IsFalse(screen.ToString()!.Contains("alice@example.com"));
    }

    [TestMethod]
    public async Task EscClearsTheFilterBeforeItClosesTheDialog()
    {
        var (dialog, store) = Create();
        store.Add(Make("Alice"));
        store.Add(Make("Bob"));
        await ShowAsync(dialog);
        await dialog.HandleKeyEventAsync(Char('/'));
        await TypeAsync(dialog, "bob");
        await EnterAsync(dialog);
        Assert.IsFalse(dialog.IsFilteringForTest);
        Assert.AreEqual("bob", dialog.FilterForTest);

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Escape));

        Assert.AreEqual(string.Empty, dialog.FilterForTest);
        Assert.IsTrue(dialog.IsVisible);

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Escape));
        Assert.IsFalse(dialog.IsVisible);
    }

    [TestMethod]
    public async Task AContactAddedUnderAFilterThatHidesItClearsTheFilter()
    {
        var (dialog, store) = Create();
        store.Add(Make("Alice"));
        await ShowAsync(dialog);
        await dialog.HandleKeyEventAsync(Char('/'));
        await TypeAsync(dialog, "alice");
        await EnterAsync(dialog);

        await dialog.HandleKeyEventAsync(Char('n'));
        await TypeAsync(dialog, "Zed");
        await EnterTimesAsync(dialog, 5);

        Assert.AreEqual("Zed", dialog.SelectedForTest!.Name);
        Assert.AreEqual(string.Empty, dialog.FilterForTest);
    }

    [TestMethod]
    public async Task TheListIsEmptyWithAHintOrANoMatchMessage()
    {
        var (dialog, store) = Create();
        await ShowAsync(dialog);
        AssertScreenContains(Render(dialog), "No contacts. Press N to add one.");

        store.Add(Make("Alice"));
        await dialog.HandleKeyEventAsync(Char('/'));
        await TypeAsync(dialog, "zzz");
        AssertScreenContains(Render(dialog), "No contact matches \"zzz\".");
    }

    [TestMethod]
    public async Task ArrowKeysMoveTheHighlightAndHomeEndJump()
    {
        var (dialog, store) = Create();
        foreach (var name in new[] { "A", "B", "C" })
        {
            store.Add(Make(name));
        }

        await ShowAsync(dialog);

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.DownArrow));
        Assert.AreEqual("B", dialog.SelectedForTest!.Name);
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.End));
        Assert.AreEqual("C", dialog.SelectedForTest!.Name);
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Home));
        Assert.AreEqual("A", dialog.SelectedForTest!.Name);
    }

    [TestMethod]
    public async Task TheDialogDrawsEveryFieldOfTheHighlightedContact()
    {
        var (dialog, store) = Create();
        store.Add(new Contact(Guid.NewGuid(), "Ada", "ada@example.com", "123", "Analytical Engines", "met at the fair"));
        await ShowAsync(dialog);

        var screen = Render(dialog);

        AssertScreenContains(screen, "Company:");
        AssertScreenContains(screen, "met at the fair");
    }

    [TestMethod]
    public async Task TheKeyHintsNameTheKeysOfTheCurrentMode()
    {
        var (dialog, store) = Create();
        store.Add(Make("Alice"));
        await ShowAsync(dialog);

        CollectionAssert.IsSubsetOf(new[] { "Insert/N", "Enter/F2/E", "Delete/D", "/" }, dialog.GetKeyHints().Select(h => h.Key).ToArray());

        await dialog.HandleKeyEventAsync(Char('n'));
        CollectionAssert.IsSubsetOf(new[] { "Enter/Tab", "Shift+Tab", "Esc" }, dialog.GetKeyHints().Select(h => h.Key).ToArray());
    }

    [TestMethod]
    public async Task TheContactCount_DoesNotPaintOverTheLastColumnTitle()
    {
        var (dialog, _) = Create();
        await ShowAsync(dialog);

        var screen = Render(dialog);

        // The title is the whole word; the count took its place before ("Com0 contacts").
        AssertScreenContains(screen, "Company");
        AssertScreenDoesNotContain(screen, "Com0");
    }

}
