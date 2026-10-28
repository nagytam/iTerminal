using System.Text.Json;
using iConsole.Commands;
using iConsole.Controls;
using iConsole.Controls.Buttons;
using iConsole.Controls.Viewers;
using iConsole.Data;
using iConsole.Helper;
using iConsole.Interfaces;
using iConsole.Json;
using iConsole.Layouts;
using iConsole.Model;
using iConsole.Services;

namespace iConsole.Dialogs;

/// <summary>
/// The JSON viewer: the document as a tree that expands and collapses. F2 checks that the file is valid JSON and says where
/// it is not; F4 switches to the file's text, the same text the plain viewer shows. A file that is not valid JSON opens as
/// its text, with the error on the status line.
/// </summary>
public class JsonViewerDialog : Dialog
{
    private const string CloseActionView = " [X]";

    private readonly IContentModelService _contentModelService;
    private readonly IFileToTextConverterService _fileToTextConverterService;
    private readonly TreeView _treeView;
    private readonly TextViewerControl _textViewerControl;
    private readonly Label _statusLabel;
    private byte[]? _content;
    private bool _textMode;
    private bool _textLoaded;
    private Task<JsonValidationIssue?>? _validation;

    public JsonViewerDialog(
        IContentModelService contentModelService,
        IFileToTextConverterService fileToTextConverterService,
        IThemeService themeService)
        : base(nameof(JsonViewerDialog), themeService)
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

    /// <summary>The tree of the document, or null while it loads or when it is not valid JSON.</summary>
    public JsonNodeItem? Root { get; private set; }

    /// <summary>True while the file's text is shown instead of the tree (F4 toggles).</summary>
    public bool IsTextMode => _textMode;

    /// <summary>What the status line says: the document's size, the validation result, or the error that kept it from loading.</summary>
    public string Status => _statusLabel.Text;

    public override async Task OnInitializeAsync()
    {
        await base.OnInitializeAsync();
        IFileSystem? fileSystem = _contentModelService.GetFocusedModel() as IFileSystem;
        if (Url is null || fileSystem is null)
        {
            _statusLabel.Text = "Nothing to show";
            return;
        }

        byte[]? content = (await fileSystem.ReadFileAsync(Url))?.Content;
        if (content is null)
        {
            _statusLabel.Text = "Could not read the file";
            return;
        }

        _content = content;
        JsonValidationIssue? issue = await Task.Run(() => JsonValidator.Validate(content));
        if (issue is not null)
        {
            _statusLabel.Text = $"Not valid JSON: {issue}";
            await ShowTextAsync();
            return;
        }

        JsonNodeItem root = await Task.Run(() => Parse(content));
        Attach(root);
    }

    private static JsonNodeItem Parse(byte[] content)
    {
        using JsonDocument document = JsonDocument.Parse(content, new JsonDocumentOptions { MaxDepth = JsonTree.MaxDepth });
        return JsonTree.Build(document);
    }

    /// <summary>Puts a parsed document in the view, with the root and its first level open.</summary>
    private void Attach(JsonNodeItem root)
    {
        Root = root;
        root.IsOpened = true;
        foreach (IHierarchicalItemModel child in root.Children.AsSnapshot())
        {
            child.IsOpened = true;
        }

        root.IsFocused = true;
        _treeView.Root = root;
        _statusLabel.Text = $"{root.Children.Count:N0} top-level item(s)";
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
        (Url?.Shrink(Math.Max(4, (int)ViewSize.Width - 14 - CloseActionView.Length)) ?? "JSON")
        + (_textMode ? " [TEXT]" : "  F2:Validate  F4:Text  *:Expand all");

    /// <summary>
    /// Starts checking the file is valid JSON, in the background; the result is shown on the status line on the next idle
    /// pass. Works in the text view too, and switches to the tree to show the result.
    /// </summary>
    public Task StartValidationAsync()
    {
        if (_content is null)
        {
            return Task.CompletedTask;
        }

        if (_textMode)
        {
            SetMode(textMode: false);
        }

        byte[] content = _content;
        _validation = Task.Run(() => JsonValidator.Validate(content));
        _statusLabel.Text = "Validating…";
        return _validation;
    }

    /// <summary>
    /// Shows the finished validation on the thread that owns the controls. This runs before the children's idle pass, so
    /// the status line is current in this same pass.
    /// </summary>
    public override void OnIdle()
    {
        if (_validation is { IsCompleted: true } validation)
        {
            _validation = null;
            JsonValidationIssue? issue = validation.Result;
            _statusLabel.Text = issue is null ? "Valid JSON" : $"Not valid JSON: {issue}";
            StateHasChanged();
        }

        base.OnIdle();
    }

    /// <summary>Opens the ancestors of a node and moves the cursor to it, the way a result is shown in the XML viewer.</summary>
    public void Reveal(JsonNodeItem node)
    {
        if (Root is null)
        {
            return;
        }

        for (IHierarchicalItemModel? ancestor = node.Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            ancestor.IsOpened = true;
        }

        ClearFocus(Root);
        node.IsFocused = true;
        SetMode(textMode: false);
    }

    private static void ClearFocus(IHierarchicalItemModel item)
    {
        item.IsFocused = false;
        foreach (IHierarchicalItemModel child in item.Children.AsSnapshot())
        {
            ClearFocus(child);
        }
    }

    public override void OnInitializeActionButtons()
    {
        AddActionButton(new ActionButton(iConsole.Localization.UiText.Get("Common.Close", "Close"), CloseActionView, new CloseSelfCommand(this), ThemeService));
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

        if (keyEvent.ConsoleKey == ConsoleKey.F2 && !keyEvent.Shift && !keyEvent.Control && !keyEvent.Alt)
        {
            await StartValidationAsync();
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
        yield return ("F2", "Check the file is valid JSON");
        yield return ("F4", "Show the file's text, or the tree again");
        yield return ("*", "Expand every node");
    }

    private sealed class CloseSelfCommand(JsonViewerDialog owner) : Command
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
