using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Model;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;
using System.ComponentModel;
using System.Text;

namespace iConsole.Test.Controls;

[TestClass]
public class AutoCompletionTest
{
    [Timeout(60_000)]
    [TestMethod]
    public async Task AutoCompletion_InitializesMatchesAndVisibility()
    {
        var mockThemeService = CreateThemeService();
        var textModel = new TextModel(string.Empty);
        var completionModel = new CompletionModel(["Germany", "Haiti", "Honduras", "Hungary", "Switzerland"]);

        var autoCompletion = new AutoCompletion(textModel, completionModel, mockThemeService.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(20, 5),
            IsVisible = false
        };

        await autoCompletion.OnInitializeAsync();

        Assert.IsTrue(autoCompletion.IsVisible);
        CollectionAssert.AreEqual(
            new[] { "Germany", "Haiti", "Honduras", "Hungary", "Switzerland" },
            autoCompletion.CompletionModel.MatchingValues.ToList());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AutoCompletion_UpdatesVisibilityOnTextChanges()
    {
        var mockThemeService = CreateThemeService();
        var textModel = new TextModel(string.Empty);
        var completionModel = new CompletionModel(["Germany", "Haiti", "Honduras", "Hungary", "Switzerland"]);

        var autoCompletion = new AutoCompletion(textModel, completionModel, mockThemeService.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(20, 5)
        };

        await autoCompletion.OnInitializeAsync();

        textModel.Lines[0] = "Hu";
        textModel.TriggerPropertyChanged();
        await autoCompletion.CompletionModel.UpdateTask!;

        Assert.IsTrue(autoCompletion.IsVisible);
        CollectionAssert.AreEqual(new string[] { "Hungary" }, autoCompletion.CompletionModel.MatchingValues.ToArray());

        textModel.Lines[0] = "ZZZ";
        textModel.TriggerPropertyChanged();
        await autoCompletion.CompletionModel.UpdateTask!;

        Assert.IsFalse(autoCompletion.IsVisible);
        Assert.AreEqual(0, autoCompletion.CompletionModel.MatchingValues.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AutoCompletion_IgnoresUnrelatedTextModelPropertyChanges()
    {
        var mockThemeService = CreateThemeService();
        var textModel = new ObservableTextModel(string.Empty);
        var completionModel = new CompletionModel(["Germany", "Haiti", "Hungary"]);
        var autoCompletion = new AutoCompletion(textModel, completionModel, mockThemeService.Object)
        {
            ViewSize = new Size(20, 5)
        };

        await autoCompletion.OnInitializeAsync();
        CollectionAssert.AreEqual(new string[] { "Germany", "Haiti", "Hungary" }, autoCompletion.CompletionModel.MatchingValues.ToArray());

        textModel.Lines[0] = "Hu";
        textModel.RaisePropertyChanged(nameof(ITextModel.Encoding));

        Assert.IsNull(autoCompletion.CompletionModel.UpdateTask);
        CollectionAssert.AreEqual(new string[] { "Germany", "Haiti", "Hungary" }, autoCompletion.CompletionModel.MatchingValues.ToArray());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AutoCompletion_RendersListInAnAnchoredPopup()
    {
        var mockThemeService = CreateThemeService();
        var textModel = new TextModel(string.Empty);
        var completionModel = new CompletionModel(["Hungary"]);

        var root = new Window(mockThemeService.Object) { ViewSize = new Size(30, 8) };
        var owner = new Window(mockThemeService.Object) { Position = new Position(0, 0), ViewSize = new Size(30, 1) };
        root.AddControl(owner);

        var autoCompletion = new AutoCompletion(textModel, completionModel, mockThemeService.Object)
        {
            ViewSize = new Size(20, 5)
        };
        owner.AddControl(autoCompletion);

        await autoCompletion.OnInitializeAsync();
        // The list belongs to the box being typed in: it opens only while its owner has focus.
        owner.CanFocus = true;
        owner.SetFocus();
        autoCompletion.Owner = owner;
        await autoCompletion.ShowPopupAsync();

        var popup = autoCompletion.ListPopup;
        Assert.IsTrue(popup.IsOpen);
        Assert.AreEqual(30, (int)popup.ViewSize.Width); // the popup spans the owner's width
        Assert.AreEqual(2, popup.Controls.Count);

        var listBox = AssertControl<ListBox<string>>(popup.Controls[0]);
        var scrollBar = AssertControl<global::iConsole.Controls.ScrollBar.ScrollBar>(popup.Controls[1]);

        Assert.AreEqual(new Position(1, 1), listBox.Position);
        Assert.AreEqual(new Size(28, 3), listBox.ViewSize);
        Assert.AreEqual(new Position(28, 1), scrollBar.Position);
        Assert.AreEqual(new Size(1, 3), scrollBar.ViewSize);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AutoCompletion_PopupDrawsBorderAndItems()
    {
        var mockThemeService = CreateThemeService();
        var textModel = new TextModel(string.Empty);
        var completionModel = new CompletionModel(["Germany", "Haiti", "Honduras", "Hungary", "Switzerland"]);

        var root = new Window(mockThemeService.Object) { ViewSize = new Size(30, 10) };
        var owner = new Window(mockThemeService.Object) { Position = new Position(0, 0), ViewSize = new Size(20, 1) };
        root.AddControl(owner);

        var autoCompletion = new AutoCompletion(textModel, completionModel, mockThemeService.Object)
        {
            ViewSize = new Size(20, 5)
        };
        owner.AddControl(autoCompletion);

        await autoCompletion.OnInitializeAsync();
        // The list belongs to the box being typed in: it opens only while its owner has focus.
        owner.CanFocus = true;
        owner.SetFocus();
        autoCompletion.Owner = owner;
        await autoCompletion.ShowPopupAsync();

        var buffer = new MockBuffer(new Size(25, 10));
        autoCompletion.ListPopup.Draw(buffer);

        var content = buffer.ContentAsString;
        Assert.IsTrue(content.Contains("╔"));        // popup top border
        Assert.IsTrue(content.Contains("Germany"));
        Assert.IsTrue(content.Contains("Haiti"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AutoCompletion_DismissedWhileAMatchUpdateIsInFlight_StaysDismissed()
    {
        // Matching runs on a background task, so a dismissal — Esc, a page switch, a completed Tab —
        // regularly lands between a keystroke and the update that keystroke started. Read naively that
        // update then calls ApplyVisibility(true) a few milliseconds later and puts the list the user
        // just dismissed straight back on the screen.
        var mockThemeService = CreateThemeService();
        var textModel = new TextModel(string.Empty);
        var completionModel = new CompletionModel(["Hungary", "Honduras"]);

        var root = new Window(mockThemeService.Object) { ViewSize = new Size(30, 8) };
        var owner = new Window(mockThemeService.Object) { Position = new Position(0, 0), ViewSize = new Size(30, 1) };
        root.AddControl(owner);
        var autoCompletion = new AutoCompletion(textModel, completionModel, mockThemeService.Object)
        {
            ViewSize = new Size(20, 5)
        };
        owner.AddControl(autoCompletion);
        await autoCompletion.OnInitializeAsync();
        // The list belongs to the box being typed in: it opens only while its owner has focus.
        owner.CanFocus = true;
        owner.SetFocus();
        autoCompletion.Owner = owner;
        await autoCompletion.ShowPopupAsync();
        Assert.IsTrue(autoCompletion.IsPopupOpen, "Precondition: the list is up.");

        // The keystroke starts the update; the dismissal happens before it is awaited.
        textModel.Lines[0] = "H";
        textModel.TriggerPropertyChanged();
        autoCompletion.CloseOverlay();
        await completionModel.UpdateTask!;

        Assert.IsFalse(autoCompletion.IsPopupOpen, "A stale match update must not re-open a dismissed list.");
        Assert.IsFalse(autoCompletion.IsVisible);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AutoCompletion_DismissedThenTypedInto_ComesBack()
    {
        // The other half: dismissing must cost nothing, so the *next* keystroke's update — which starts
        // after the dismissal — is still allowed to show the list.
        var mockThemeService = CreateThemeService();
        var textModel = new TextModel(string.Empty);
        var completionModel = new CompletionModel(["Hungary", "Honduras"]);

        var root = new Window(mockThemeService.Object) { ViewSize = new Size(30, 8) };
        var owner = new Window(mockThemeService.Object) { Position = new Position(0, 0), ViewSize = new Size(30, 1) };
        root.AddControl(owner);
        var autoCompletion = new AutoCompletion(textModel, completionModel, mockThemeService.Object)
        {
            ViewSize = new Size(20, 5)
        };
        owner.AddControl(autoCompletion);
        await autoCompletion.OnInitializeAsync();
        // The list belongs to the box being typed in: it opens only while its owner has focus.
        owner.CanFocus = true;
        owner.SetFocus();
        autoCompletion.Owner = owner;
        await autoCompletion.ShowPopupAsync();
        autoCompletion.CloseOverlay();
        Assert.IsFalse(autoCompletion.IsPopupOpen);

        textModel.Lines[0] = "H";
        textModel.TriggerPropertyChanged();
        await completionModel.UpdateTask!;

        Assert.IsTrue(autoCompletion.IsVisible, "Typing after a dismissal should bring the suggestions back.");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AutoCompletion_TabCompletesAndHidesControl()
    {
        var mockThemeService = CreateThemeService();
        var textModel = new TextModel("Hu");
        var completionModel = new CompletionModel(["Germany", "Haiti", "Hungary"]);
        var autoCompletion = new AutoCompletion(textModel, completionModel, mockThemeService.Object)
        {
            ViewSize = new Size(20, 5)
        };

        await autoCompletion.OnInitializeAsync();
        Assert.IsTrue(autoCompletion.IsVisible);
        CollectionAssert.AreEqual(new string[] { "Hungary" }, autoCompletion.CompletionModel.MatchingValues.ToArray());

        var handled = await autoCompletion.HandleKeyEventAsync(new KeyEvent(' ', ConsoleKey.Tab, false, false, false));
        await autoCompletion.CompletionModel.UpdateTask!;

        Assert.IsTrue(handled);
        Assert.AreEqual("Hungary", textModel.Lines[0]);
        Assert.IsFalse(autoCompletion.IsVisible);
        CollectionAssert.AreEqual(new string[] { "Hungary" }, autoCompletion.CompletionModel.MatchingValues.ToArray());
    }

    /// <summary>
    /// A box nobody is typing in does not put its list up. Matches are worked out when the box
    /// initialises, and the list used to open with them — the folder selector's hidden Path page
    /// floated its whole history over the dialog, and over the panels once the dialog had closed.
    /// </summary>
    [Timeout(60_000)]
    [TestMethod]
    public async Task AutoCompletion_ForAnOwnerWithoutFocus_KeepsTheListDown()
    {
        var mockThemeService = CreateThemeService();
        var root = new Window(mockThemeService.Object) { ViewSize = new Size(30, 8) };
        var owner = new Window(mockThemeService.Object) { Position = new Position(0, 0), ViewSize = new Size(30, 1), CanFocus = true };
        root.AddControl(owner);
        var autoCompletion = new AutoCompletion(new TextModel(string.Empty), new CompletionModel(["Hungary"]), mockThemeService.Object)
        {
            ViewSize = new Size(20, 5)
        };
        owner.AddControl(autoCompletion);
        autoCompletion.Owner = owner;

        await autoCompletion.OnInitializeAsync();
        await autoCompletion.ShowPopupAsync();

        Assert.IsFalse(autoCompletion.IsPopupOpen);
        Assert.IsFalse(root.Controls.AsSnapshot().Contains(autoCompletion.ListPopup), "nothing attached to the root");
    }

    private static Mock<IThemeService> CreateThemeService()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        return mockThemeService;
    }

    private static T AssertControl<T>(object value)
        where T : class
    {
        Assert.IsInstanceOfType<T>(value);
        return (value as T)!;
    }

    private sealed class ObservableTextModel(string text) : ITextModel
    {
        public ConcurrentList<string> Lines { get; set; } = [text];
        public Encoding Encoding { get; set; } = Encoding.UTF8;
        public event PropertyChangedEventHandler? PropertyChanged;

        public void TriggerPropertyChanged()
        {
            RaisePropertyChanged(nameof(Lines));
        }

        public void RaisePropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
