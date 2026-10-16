using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Extensions;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls;

[TestClass]
public class TextBoxTest
{
    [TestMethod]
    public async Task TextBoxHandlesKeyEvents()
    {
        var mockBeepService = new Mock<IBeepService>();
        var mockDateTimeProviderService = new Mock<IDateTimeProviderService>();
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var textBox = new TextBox(mockBeepService.Object, mockDateTimeProviderService.Object, mockThemeService.Object)
        {
            Text = "Hello, world!",
            ViewSize = new Size(25, 1)
        };
        textBox.ScrollableModel.ScrollableSize = new Size(textBox.TextLength, 1);
        textBox.ScrollableModel.ViewSize = new Size(25, 1);
        await textBox.HandleKeyEventAsync(new KeyEvent('0', ConsoleKey.NumPad0, false, false, false));
        Assert.AreEqual("0Hello, world!", textBox.Text);
        await textBox.HandleKeyEventAsync(new KeyEvent(' ', ConsoleKey.End, false, false, false));
        await textBox.HandleKeyEventAsync(new KeyEvent('1', ConsoleKey.NumPad1, false, false, false));
        Assert.AreEqual("0Hello, world!1", textBox.Text);
        await textBox.HandleKeyEventAsync(new KeyEvent(' ', ConsoleKey.Home, false, false, false));
        await textBox.HandleKeyEventAsync(new KeyEvent(' ', ConsoleKey.RightArrow, false, false, false));
        await textBox.HandleKeyEventAsync(new KeyEvent('2', ConsoleKey.NumPad2, false, false, false));
        Assert.AreEqual("02Hello, world!1", textBox.Text);
        await textBox.HandleKeyEventAsync(new KeyEvent(' ', ConsoleKey.LeftArrow, false, false, false));
        await textBox.HandleKeyEventAsync(new KeyEvent('3', ConsoleKey.NumPad3, false, false, false));
        Assert.AreEqual("032Hello, world!1", textBox.Text);
    }

    [TestMethod]
    public async Task TextBoxDraw()
    {
        var mockBeepService = new Mock<IBeepService>();
        var mockDateTimeProviderService = new Mock<IDateTimeProviderService>();
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var buffer = new MockBuffer(new Size(12, 1));
        var textBox = new TextBox(mockBeepService.Object, mockDateTimeProviderService.Object, mockThemeService.Object)
        {
            Label = "Label",
            Text = "ABC",
            Position = new Position(0, 0),
            ViewSize = new Size(13, 1)
        };
        textBox.ScrollableModel.ScrollableSize = new Size(textBox.TextLength, 1);
        textBox.ScrollableModel.ViewSize = new Size(3, 0);
        await textBox.OnInitializeAsync();
        Assert.AreEqual("ABC", textBox.Text);
        textBox.Draw(buffer);
        Assert.AreEqual("Label: [ABC]" + Environment.NewLine, buffer.ContentAsString);

        await textBox.HandleKeyEventAsync(new KeyEvent(' ', ConsoleKey.End, false, false, false));
        await textBox.HandleKeyEventAsync(new KeyEvent('D', ConsoleKey.D, false, false, false));
        Assert.AreEqual("ABCD", textBox.Text);
        textBox.Draw(buffer);
        Assert.AreEqual("Label: <BCD]" + Environment.NewLine, buffer.ContentAsString);
        Assert.AreEqual(1, (int)textBox.ScrollableModel.ScrollPosition.X);

        await textBox.HandleKeyEventAsync(new KeyEvent(' ', ConsoleKey.Home, false, false, false));
        Assert.AreEqual(0, (int)textBox.ScrollableModel.ScrollPosition.X);
        textBox.Draw(buffer);
        Assert.AreEqual("Label: [ABC>" + Environment.NewLine, buffer.ContentAsString);
    }

    [TestMethod]
    public async Task TextBoxSelection()
    {
        var mockBeepService = new Mock<IBeepService>();
        var mockDateTimeProviderService = new Mock<IDateTimeProviderService>();
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var buffer = new MockBuffer(new Size(12, 1));
        var textBox = new TextBox(mockBeepService.Object, mockDateTimeProviderService.Object, mockThemeService.Object)
        {
            Label = "Label",
            Text = "ABC",
            Position = new Position(0, 0),
            ViewSize = new Size(13, 1)
        };
        textBox.ScrollableModel.ScrollableSize = new Size(5, 1);
        await textBox.OnInitializeAsync();
        Assert.AreEqual("ABC", textBox.Text);
        textBox.Draw(buffer);
        Assert.AreEqual("Label: [ABC]" + Environment.NewLine, buffer.ContentAsString);

        Assert.IsFalse(textBox.SelectionTextModel.IsSelected);
        Assert.IsNull(textBox.SelectionTextModel.Start);
        await textBox.HandleKeyEventAsync(new KeyEvent(' ', ConsoleKey.Home, false, false, false));
        await textBox.HandleKeyEventAsync(new KeyEvent(' ', ConsoleKey.RightArrow, true, false, false));
        Assert.IsTrue(textBox.SelectionTextModel.IsSelected);
        Assert.AreEqual(new Position(0, 0), textBox.SelectionTextModel.Start);
        Assert.AreEqual(new Position(0, 0), textBox.SelectionTextModel.End);
    }

    [TestMethod]
    public async Task TextBoxAutoCompletion()
    {
        ConcurrentList<string> countries = ["Germany", "Haiti", "Honduras", "Hungary", "Switzerland"];

        var mockBeepService = new Mock<IBeepService>();
        var mockDateTimeProviderService = new Mock<IDateTimeProviderService>();
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var buffer = new MockBuffer(new Size(25, 6));
        var textBox = new TextBox(mockBeepService.Object, mockDateTimeProviderService.Object, mockThemeService.Object)
        {
            Label = "Country",
            Text = string.Empty,
            ViewSize = new Size(20, 1)
        }.WithAutoCompletion(countries);
        textBox.ScrollableModel.ScrollableSize = new Size(textBox.TextLength, 1);
        textBox.ScrollableModel.ViewSize = new Size(20, 1);
        var autoCompletion = textBox.AutoCompletion;
        Assert.IsNotNull(autoCompletion);
        Assert.IsTrue(autoCompletion.IsVisible);
        await textBox.OnInitializeAsync();
        Assert.AreEqual(string.Empty, textBox.Text);
        // The text box renders its own line including the ghost completion; the suggestion list now
        // floats in a popup (covered by AutoCompletionTest), so only row 0 is asserted here.
        textBox.Draw(buffer);
        Assert.AreEqual("Country: [Germany ] ", buffer.ContentAsString.Split(Environment.NewLine)[0]);
        Assert.AreEqual(5, autoCompletion.CompletionModel.MatchingValues.Count);

        await textBox.HandleKeyEventAsync(new KeyEvent('H', ConsoleKey.H, false, false, false));
        Assert.AreEqual("H", textBox.Text);
        Task.WaitAll([autoCompletion.CompletionModel.UpdateTask!]);
        Assert.IsTrue(autoCompletion.IsVisible, "typing puts the list up");
        textBox.Draw(buffer);
        Assert.AreEqual("Country: [Haiti   ] ", buffer.ContentAsString.Split(Environment.NewLine)[0]);
        Assert.AreEqual(3, autoCompletion.CompletionModel.MatchingValues.Count);

        await textBox.HandleKeyEventAsync(new KeyEvent('u', ConsoleKey.U, false, false, false));
        Assert.AreEqual("Hu", textBox.Text);
        Task.WaitAll([autoCompletion.CompletionModel.UpdateTask!]);
        Assert.IsTrue(autoCompletion.IsVisible);
        textBox.Draw(buffer);
        Assert.AreEqual("Country: [Hungary ] ", buffer.ContentAsString.Split(Environment.NewLine)[0]);
        Assert.AreEqual(1, autoCompletion.CompletionModel.MatchingValues.Count);

        await textBox.HandleKeyEventAsync(new KeyEvent(' ', ConsoleKey.Tab, false, false, false));
        Assert.AreEqual("Hungary", textBox.Text);
        Task.WaitAll([autoCompletion.CompletionModel.UpdateTask!]);
        Assert.AreEqual(1, autoCompletion.CompletionModel.MatchingValues.Count);
    }

    private static TextBox EmptyTextBox()
    {
        var themeService = new Mock<IThemeService>();
        themeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var textBox = new TextBox(Mock.Of<IBeepService>(), Mock.Of<IDateTimeProviderService>(), themeService.Object)
        {
            ViewSize = new Size(40, 1),
        };
        textBox.ScrollableModel.ViewSize = new Size(40, 1);
        return textBox;
    }

    private static async Task TypeAsync(TextBox textBox, string text)
    {
        foreach (var c in text)
        {
            await textBox.HandleKeyEventAsync(new KeyEvent(c, ConsoleKey.Oem1, false, false, false));
        }
    }

    /// <summary>
    /// Punctuation a file name commonly carries. The box used to accept ASCII letters, digits and six
    /// marks only, so "Photos (2024)" or "Tom's notes" could not be typed into any dialog.
    /// </summary>
    [TestMethod]
    public async Task PunctuationCanBeTyped()
    {
        var textBox = EmptyTextBox();

        await TypeAsync(textBox, "Tom's (2024), #1 & more! [a+b=c]");

        Assert.AreEqual("Tom's (2024), #1 & more! [a+b=c]", textBox.Text);
    }

    [TestMethod]
    public async Task AccentedLettersCanBeTyped()
    {
        var textBox = EmptyTextBox();

        await TypeAsync(textBox, "Árvíztűrő tükörfúrógép");

        Assert.AreEqual("Árvíztűrő tükörfúrógép", textBox.Text);
    }

    /// <summary>Windows reports AltGr as Ctrl+Alt; on a Hungarian layout AltGr+V is the only way to type '@'.</summary>
    [TestMethod]
    public async Task AnAltGrCharacterIsTyped()
    {
        var textBox = EmptyTextBox();

        await textBox.HandleKeyEventAsync(new KeyEvent('@', ConsoleKey.V, false, alt: true, control: true));

        Assert.AreEqual("@", textBox.Text);
    }

    [TestMethod]
    public async Task ChordsAreNotTyped()
    {
        var textBox = EmptyTextBox();

        Assert.IsFalse(await textBox.HandleKeyEventAsync(new KeyEvent('a', ConsoleKey.A, false, alt: true, control: false)));
        Assert.IsFalse(await textBox.HandleKeyEventAsync(new KeyEvent('\u0001', ConsoleKey.A, false, alt: false, control: true)));
        Assert.IsFalse(await textBox.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.A, false, alt: true, control: true)));

        Assert.AreEqual(string.Empty, textBox.Text);
    }
}
