using System.Text.Json.Nodes;
using iConsole.Commands;
using iConsole.Controls;
using iConsole.Controls.Buttons;
using iConsole.Controls.Viewers;
using iConsole.Data;
using iConsole.Helper;
using iConsole.Interfaces;
using iConsole.Json;
using iConsole.Layouts;
using iConsole.Localization;
using iConsole.Model;
using iConsole.Services;
using iConsole.Structured;

namespace iConsole.Dialogs;

/// <summary>
/// The shared body of the structured-data viewers (YAML and TOML): the file as a tree that expands and collapses, the rows
/// read as the JSON viewer reads them. F4 switches to the file's text; a file the parser rejects opens as its text, with the
/// parser's message on the status line. A subclass names the format and parses the text.
/// </summary>
public abstract class DataTreeViewerDialog : Dialog
{
    private const string CloseActionView = " [X]";

    private readonly IContentModelService _contentModelService;
    private readonly IFileToTextConverterService _fileToTextConverterService;
    private readonly TreeView _treeView;
    private readonly TextViewerControl _textViewerControl;
    private readonly Label _statusLabel;
    private bool _textMode;
    private bool _textLoaded;

    protected DataTreeViewerDialog(
        string name,
        IContentModelService contentModelService,
        IFileToTextConverterService fileToTextConverterService,
        IThemeService themeService)
        : base(name, themeService)
    {
        _contentModelService = contentModelService;
        _fileToTextConverterService = fileToTextConverterService;
        // The tree is added first: a dialog gives the keyboard to its first focusable control, and the tree is where it starts.
        _treeView = new TreeView(new HierarchicalItemModel(null, string.Empty), themeService);
        _textViewerControl = new TextViewerControl(false, themeService);
        _statusLabel = new Label(string.Empty, themeService);
        AddControl(_treeView);
        AddControl(_textViewerControl);
        AddControl(_statusLabel);
    }

    /// <summary>The file shown.</summary>
    public Url? Url { get; set; }

    /// <summary>The tree of the document, or null while it loads or when the parser rejects it.</summary>
    public JsonNodeItem? Root { get; private set; }

    /// <summary>True while the file's text is shown instead of the tree (F4 toggles).</summary>
    public bool IsTextMode => _textMode;

    /// <summary>What the status line says: the number of top-level items, or why the file could not be shown as a tree.</summary>
    public string Status => _statusLabel.Text;

    /// <summary>The format's name, as the title and the status line name it: <c>YAML</c>, <c>TOML</c>.</summary>
    protected abstract string FormatName { get; }

    /// <summary>Reads the text into a JSON-shaped tree. Runs on a background task; throws when the parser rejects the text.</summary>
    protected abstract JsonNode? ParseText(string text);

    public override async Task OnInitializeAsync()
    {
        await base.OnInitializeAsync();
        IFileSystem? fileSystem = _contentModelService.GetFocusedModel() as IFileSystem;
        if (Url is null || fileSystem is null)
        {
            _statusLabel.Text = UiText.Get("Viewer.NothingToShow", "Nothing to show");
            return;
        }

        byte[]? content = (await fileSystem.ReadFileAsync(Url))?.Content;
        if (content is null)
        {
            _statusLabel.Text = UiText.Get("Viewer.CouldNotRead", "Could not read the file");
            return;
        }

        string text = TextEncodings.Decode(content, out _);
        try
        {
            // The parser may reject any text, whatever exception it throws, and the viewer shows the message rather than failing.
            JsonNode? node = await Task.Run(() => ParseText(text));
            Attach(StructuredTree.ToDocument(node));
        }
        catch (Exception exception)
        {
            _statusLabel.Text = string.Format(UiText.Get("Viewer.NotValidFormat", "Not valid {0}: {1}"), FormatName, exception.Message);
            await ShowTextAsync();
        }
    }

    /// <summary>Puts a parsed document in the view, with the root and its first level open.</summary>
    private void Attach(System.Text.Json.JsonDocument document)
    {
        using System.Text.Json.JsonDocument owned = document;
        JsonNodeItem root = JsonTree.Build(owned);
        Root = root;
        root.IsOpened = true;
        foreach (IHierarchicalItemModel child in root.Children.AsSnapshot())
        {
            child.IsOpened = true;
        }

        root.IsFocused = true;
        _treeView.Root = root;
        _statusLabel.Text = string.Format(UiText.Get("Json.TopLevelItems", "{0:N0} top-level item(s)"), root.Children.Count);
        StateHasChanged();
    }

    public override async Task ShowAsync()
    {
        await base.ShowAsync();
        // ShowAsync makes every child visible; the text view is on demand.
        _textMode = false;
        _textViewerControl.IsVisible = false;
        _treeView.SetFocus();
    }

    /// <summary>Switches between the tree and the file's text.</summary>
    public async Task ToggleTextModeAsync()
    {
        if (!_textMode)
        {
            await ShowTextAsync();
        }
        else
        {
            SetMode(textMode: false);
        }
    }

    private async Task ShowTextAsync()
    {
        if (!_textLoaded)
        {
            IFileSystem? fileSystem = _contentModelService.GetFocusedModel() as IFileSystem;
            await _fileToTextConverterService.ReadFileAsync(Url, fileSystem!, _textViewerControl.TextModel);
            _textLoaded = true;
        }

        SetMode(textMode: true);
    }

    private void SetMode(bool textMode)
    {
        _textMode = textMode;
        _treeView.IsVisible = !textMode;
        _textViewerControl.IsVisible = textMode;
        if (textMode)
        {
            _textViewerControl.SetFocus();
        }
        else
        {
            _treeView.SetFocus();
        }

        Title = TitleText();
        StateHasChanged();
    }

    private string TitleText() =>
        (Url?.Shrink(Math.Max(4, (int)ViewSize.Width - 14 - CloseActionView.Length)) ?? FormatName)
        + (_textMode ? " [TEXT]" : "  F4:Text  *:Expand all");

    public override void OnInitializeActionButtons()
    {
        AddActionButton(new ActionButton(UiText.Get("Common.Close", "Close"), CloseActionView, new CloseSelfCommand(this), ThemeService));
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        if (keyEvent.KeyPressType != KeyPressType.Down)
        {
            return await base.HandleKeyEventAsync(keyEvent);
        }

        if (keyEvent.ConsoleKey == ConsoleKey.Escape && !keyEvent.Shift && !keyEvent.Control && !keyEvent.Alt)
        {
            Close();
            return true;
        }

        if (keyEvent.ConsoleKey == ConsoleKey.F4 && !keyEvent.Shift && !keyEvent.Control && !keyEvent.Alt)
        {
            await ToggleTextModeAsync();
            return true;
        }

        if (!_textMode && _treeView.IsFocused && !keyEvent.Control && !keyEvent.Alt
            && keyEvent.KeyChar == '*' && Root is not null)
        {
            _treeView.ExpandAll();
            StateHasChanged();
            return true;
        }

        return await base.HandleKeyEventAsync(keyEvent);
    }

    /// <summary>The keys this dialog adds to the tree's, for the help screen.</summary>
    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        yield return ("F4", UiText.Get("Viewer.HintToggleText", "Show the file's text, or the tree again"));
        yield return ("*", UiText.Get("Viewer.HintExpandAll", "Expand every node"));
    }

    private sealed class CloseSelfCommand(DataTreeViewerDialog owner) : Command
    {
        public override Task ExecuteAsync(object? arg)
        {
            owner.Close();
            return Task.CompletedTask;
        }
    }

    public override void OnInitializeLayout()
    {
        if (Parent != null)
        {
            ViewSize = Parent.ViewSize;
            Position = new Position(0, 0);
            Title = TitleText();
        }

        // The rows inside the border, with the status line on the row above the bottom border. A terminal too small for
        // them gets empty rows, not negative sizes.
        int width = Math.Max(0, (int)ViewSize.Width - 2);
        int content = Math.Max(1, (int)ViewSize.Height - 3);
        int statusRow = Math.Max(0, (int)ViewSize.Height - 2);

        _treeView.Position = new Position(1, 1);
        _treeView.ViewSize = new Size(width, content);

        _textViewerControl.Position = new Position(1, 1);
        _textViewerControl.ViewSize = new Size(width, content);

        _statusLabel.Position = new Position(1, statusRow);
        _statusLabel.ViewSize = new Size(width, 1);

        base.OnInitializeLayout();

        Layout.Center(this);
    }
}

/// <summary>The YAML viewer: F4 on a <c>.yaml</c> or <c>.yml</c> file, from any drive.</summary>
public class YamlViewerDialog(
    IContentModelService contentModelService,
    IFileToTextConverterService fileToTextConverterService,
    IThemeService themeService)
    : DataTreeViewerDialog(nameof(YamlViewerDialog), contentModelService, fileToTextConverterService, themeService)
{
    protected override string FormatName => "YAML";

    protected override JsonNode? ParseText(string text) => iConsole.Yaml.YamlDocumentReader.Read(text);
}

/// <summary>The TOML viewer: F4 on a <c>.toml</c> file, from any drive.</summary>
public class TomlViewerDialog(
    IContentModelService contentModelService,
    IFileToTextConverterService fileToTextConverterService,
    IThemeService themeService)
    : DataTreeViewerDialog(nameof(TomlViewerDialog), contentModelService, fileToTextConverterService, themeService)
{
    protected override string FormatName => "TOML";

    protected override JsonNode? ParseText(string text) => iConsole.Toml.TomlDocumentReader.Read(text);
}
