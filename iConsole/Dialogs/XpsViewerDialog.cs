using System.Globalization;
using iConsole.Commands;
using iConsole.Controls;
using iConsole.Controls.Buttons;
using iConsole.Controls.Viewers;
using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Layouts;
using iConsole.Localization;
using iConsole.Model;
using iConsole.Services;
using iConsole.Xps;

namespace iConsole.Dialogs;

/// <summary>
/// The XPS viewer: the pages of an XPS or OpenXPS package in a picker, with the selected page's text below it. A page is
/// read by <see cref="XpsDocument"/>, so an OpenXPS package reads the same as an XPS one, and a package that cannot be
/// read says why on the status line instead of opening as an empty list.
///
/// <para>The package is read on a worker thread; the text model is changed on the thread that owns the controls. The
/// picker keeps the focus so Up and Down step through pages, and Ctrl+Left and Ctrl+Right step them from the text view.</para>
/// </summary>
public class XpsViewerDialog : Dialog
{
    private const string CloseActionView = " [X]";

    private readonly IContentModelService _contentModelService;
    private readonly ComboBox<XpsPage> _pagePicker;
    private readonly TextViewerControl _textViewerControl;
    private readonly Label _statusLabel;
    private XpsDocument? _document;

    public XpsViewerDialog(IContentModelService contentModelService, IThemeService themeService)
        : base(nameof(XpsViewerDialog), themeService)
    {
        _contentModelService = contentModelService;
        // The picker is added first: a dialog gives the keyboard to its first focusable child, and choosing a page is where a reader starts.
        _pagePicker = new ComboBox<XpsPage>(themeService)
        {
            Label = UiText.Get("Xps.Picker.Label", "Page"),
            DisplaySelector = PageLabel,
        };
        _pagePicker.SelectionChanged += (_, _) => ShowSelectedPage();
        _textViewerControl = new TextViewerControl(false, themeService);
        _statusLabel = new Label(string.Empty, themeService);
        AddControl(_pagePicker);
        AddControl(_textViewerControl);
        AddControl(_statusLabel);
    }

    /// <summary>The package shown.</summary>
    public Url? Url { get; set; }

    /// <summary>The package that was read, or null while it loads or after a read that returned nothing.</summary>
    public XpsDocument? Document => _document;

    /// <summary>The page on screen, or null before the package lands or when it has no pages.</summary>
    public XpsPage? CurrentPage => _pagePicker.SelectedItem;

    /// <summary>The page picker, for a test or a caller to choose a page.</summary>
    public ComboBox<XpsPage> PagePicker => _pagePicker;

    /// <summary>What the status line says: the page and its size, the package title, or why nothing is shown.</summary>
    public string Status => _statusLabel.Text;

    /// <summary>The text on screen: the selected page's lines, or the note that the page has no text.</summary>
    public IList<string> Lines => _textViewerControl.TextModel.Lines;

    public override async Task OnInitializeAsync()
    {
        await base.OnInitializeAsync();
        // The dialog is reused for every package a panel views, so nothing of the last one may survive.
        _document = null;
        _pagePicker.SetItems([]);
        _textViewerControl.TextModel.Lines.ReplaceAll([]);
        _statusLabel.Text = string.Empty;
        if (Url is null)
        {
            _statusLabel.Text = UiText.Get("Viewer.NothingToShow", "Nothing to show");
            return;
        }

        IFileSystem? fileSystem = (_contentModelService.GetFocusedModel() ?? _contentModelService.GetLastContentModel()) as IFileSystem;
        if (fileSystem is null)
        {
            _statusLabel.Text = UiText.Get("Viewer.CouldNotRead", "Could not read the file");
            return;
        }

        byte[]? content = (await fileSystem.ReadFileAsync(Url))?.Content;
        if (content is null)
        {
            _statusLabel.Text = UiText.Get("Viewer.CouldNotRead", "Could not read the file");
            return;
        }

        // Parsing a package is CPU and zip work with nothing to await, so it stays off the thread that owns the controls.
        XpsDocument document = await Task.Run(() => XpsDocument.Read(content));
        _document = document;
        if (document.Error is not null)
        {
            _statusLabel.Text = document.Error;
            return;
        }

        if (document.Pages.Count == 0)
        {
            _statusLabel.Text = UiText.Get("Xps.NoPages", "This document has no pages");
            return;
        }

        _pagePicker.SetItems(document.Pages);
        ShowSelectedPage();
    }

    public override async Task ShowAsync()
    {
        await base.ShowAsync();
        _pagePicker.SetFocus();
    }

    /// <summary>Puts the selected page's text on screen and the page detail on the status line.</summary>
    private void ShowSelectedPage()
    {
        XpsPage? page = _pagePicker.SelectedItem;
        if (page is null)
        {
            _textViewerControl.TextModel.Lines.ReplaceAll([]);
            return;
        }

        _textViewerControl.TextModel.Lines.ReplaceAll(page.Lines.Count == 0
            ? [UiText.Get("Xps.Page.NoText", "This page has no text.")]
            : page.Lines);
        _statusLabel.Text = StatusFor(page);
        StateHasChanged();
    }

    private string StatusFor(XpsPage page)
    {
        string status = string.Format(
            CultureInfo.CurrentCulture,
            UiText.Get("Xps.Status.Page", "Page {0} of {1}"),
            page.Number,
            _document?.Pages.Count ?? 0);
        if (page.Width > 0 && page.Height > 0)
        {
            status += string.Format(
                CultureInfo.CurrentCulture,
                UiText.Get("Xps.Status.Size", " · {0} × {1}"),
                page.Width.ToString("0.#", CultureInfo.CurrentCulture),
                page.Height.ToString("0.#", CultureInfo.CurrentCulture));
        }

        return _document?.Title is { Length: > 0 } title ? $"{status} · {title}" : status;
    }

    private string PageLabel(XpsPage page) =>
        HasSeveralDocuments()
            ? string.Format(CultureInfo.CurrentCulture, UiText.Get("Xps.Page.MultiDocument", "Document {0} · Page {1}"), page.DocumentNumber, page.PageInDocument)
            : string.Format(CultureInfo.CurrentCulture, UiText.Get("Xps.Page.Label", "Page {0}"), page.Number);

    private bool HasSeveralDocuments() =>
        _document is not null && _document.Pages.Select(page => page.DocumentNumber).Distinct().Skip(1).Any();

    /// <summary>Moves the picker <paramref name="delta"/> pages, clamped to the package.</summary>
    public void StepPage(int delta)
    {
        if (_document is null || _document.Pages.Count == 0)
        {
            return;
        }

        _pagePicker.SelectedIndex = Math.Clamp(_pagePicker.SelectedIndex + delta, 0, _document.Pages.Count - 1);
    }

    private string TitleText()
    {
        string suffix = UiText.Get("Xps.Title.Hint", "  Ctrl+←/→:page");
        int budget = Math.Max(4, (int)ViewSize.Width - 14 - CloseActionView.Length - suffix.Length);
        return (Url?.Shrink(budget) ?? UiText.Get("Xps.Title", "XPS")) + suffix;
    }

    public override void OnInitializeActionButtons()
    {
        AddActionButton(new ActionButton(UiText.Get("Common.Close", "Close"), CloseActionView, new CloseSelfCommand(this), ThemeService));
    }

    /// <summary>The keys this dialog adds to the picker's and the text view's, for the help screen.</summary>
    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        yield return ("Ctrl+← / Ctrl+→", UiText.Get("Xps.Hint.Page", "Show the previous or next page"));
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        if (keyEvent.KeyPressType == KeyPressType.Down
            && keyEvent.ConsoleKey == ConsoleKey.Escape
            && !keyEvent.Shift && !keyEvent.Control && !keyEvent.Alt)
        {
            Close();
            return true;
        }

        if (keyEvent.KeyPressType == KeyPressType.Down
            && keyEvent.Control && !keyEvent.Shift && !keyEvent.Alt
            && keyEvent.ConsoleKey is ConsoleKey.LeftArrow or ConsoleKey.RightArrow)
        {
            StepPage(keyEvent.ConsoleKey == ConsoleKey.RightArrow ? 1 : -1);
            return true;
        }

        return await base.HandleKeyEventAsync(keyEvent);
    }

    private sealed class CloseSelfCommand(XpsViewerDialog owner) : Command
    {
        public override Task ExecuteAsync(object? arg)
        {
            owner.Close();
            return Task.CompletedTask;
        }
    }

    public override void OnInitializeLayout()
    {
        if (Parent is not null)
        {
            ViewSize = Parent.ViewSize;
            Position = new Position(0, 0);
            Title = TitleText();
        }

        // Inside the border: the page picker, the page's text, and the status line on the last row. A terminal too
        // small for the rows gets empty ones, not negative sizes.
        int width = Math.Max(0, (int)ViewSize.Width - 2);
        int content = Math.Max(1, (int)ViewSize.Height - 4);
        int statusRow = Math.Max(0, (int)ViewSize.Height - 2);

        _pagePicker.Position = new Position(1, 1);
        _pagePicker.ViewSize = new Size(width, 1);

        _textViewerControl.Position = new Position(1, 2);
        _textViewerControl.ViewSize = new Size(width, content);

        _statusLabel.Position = new Position(1, statusRow);
        _statusLabel.ViewSize = new Size(width, 1);

        base.OnInitializeLayout();

        Layout.Center(this);
    }
}
