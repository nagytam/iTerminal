using System.Xml;
using iConsole.Commands;
using iConsole.Controls;
using iConsole.Controls.Buttons;
using iConsole.Controls.Viewers;
using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Layouts;
using iConsole.Helper;
using iConsole.Model;
using iConsole.Services;
using iConsole.Xml;

namespace iConsole.Dialogs;

/// <summary>
/// The XML viewer: the document as a tree that expands and collapses, and an XPath box whose results are listed below
/// the tree. Enter on a result opens its ancestors and moves the cursor to it. F4 switches to the file's text, the same
/// text the plain viewer shows. A file that is not well-formed opens as its text, with the error on the status line.
/// </summary>
public class XmlViewerDialog : Dialog
{
    private const string CloseActionView = " [X]";

    private readonly IContentModelService _contentModelService;
    private readonly IFileToTextConverterService _fileToTextConverterService;
    private readonly TextBox _xpathBox;
    private readonly TreeView _treeView;
    private readonly ListBox<XmlXPathResult> _resultsBox;
    private readonly ListBoxModel<XmlXPathResult> _resultsModel;
    private readonly TextViewerControl _textViewerControl;
    private readonly Label _statusLabel;
    private XmlTree? _tree;
    private bool _textMode;
    private bool _textLoaded;
    private Task<XmlXPathOutcome>? _query;
    private Task<IReadOnlyList<XmlValidationIssue>>? _validation;
    private byte[]? _content;

    public XmlViewerDialog(
        IContentModelService contentModelService,
        IFileToTextConverterService fileToTextConverterService,
        IBeepService beepService,
        IDateTimeProviderService dateTimeProviderService,
        IThemeService themeService)
        : base(nameof(XmlViewerDialog), themeService)
    {
        _contentModelService = contentModelService;
        _fileToTextConverterService = fileToTextConverterService;
        _xpathBox = new TextBox(beepService, dateTimeProviderService, themeService)
        {
            Label = "XPath",
            Placeholder = "Enter a query, e.g. //book[price > 10]/title",
        };
        _treeView = new TreeView(new HierarchicalItemModel(null, string.Empty), themeService);
        _resultsModel = new ListBoxModel<XmlXPathResult>(new ConcurrentObservableCollection<XmlXPathResult>());
        _resultsBox = new ListBox<XmlXPathResult>(_resultsModel, themeService) { EmptyText = "No results" };
        _textViewerControl = new TextViewerControl(false, themeService);
        _statusLabel = new Label(string.Empty, themeService);
        // The tree is added first: a dialog gives the keyboard to its first focusable control, and the tree is where it starts.
        AddControl(_treeView);
        AddControl(_xpathBox);
        AddControl(_resultsBox);
        AddControl(_textViewerControl);
        AddControl(_statusLabel);
    }

    /// <summary>The file shown.</summary>
    public Url? Url { get; set; }

    /// <summary>The parsed document, or null while it loads or when it is not well-formed.</summary>
    public XmlTree? Tree => _tree;

    /// <summary>True while the file's text is shown instead of the tree (F4 toggles).</summary>
    public bool IsTextMode => _textMode;

    /// <summary>The results of the last query, in document order.</summary>
    public IReadOnlyList<XmlXPathResult> Results => _resultsModel.Items;

    /// <summary>What the status line says: the match count, the error of a bad query, or the parse error.</summary>
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

        try
        {
            XmlTree tree = await Task.Run(() => XmlTree.Build(XmlTree.Parse(content)));
            Attach(tree);
        }
        catch (XmlException exception)
        {
            // Not well-formed: the file's text is the useful thing to show, with the position of the error.
            _statusLabel.Text = $"Not well-formed XML: {exception.Message}";
            await ShowTextAsync();
        }
    }

    /// <summary>Puts a parsed tree in the view, with its document and the first two levels open.</summary>
    private void Attach(XmlTree tree)
    {
        _tree = tree;
        tree.Root.IsOpened = true;
        foreach (IHierarchicalItemModel child in tree.Root.Children.AsSnapshot())
        {
            child.IsOpened = true;
        }

        tree.Root.IsFocused = true;
        _treeView.Root = tree.Root;
        _statusLabel.Text = $"{XmlTree.Tag(tree.Document.Root!)} · {tree.Document.Root!.DescendantNodes().Count():N0} nodes";
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
        _xpathBox.IsVisible = !textMode;
        _treeView.IsVisible = !textMode;
        _resultsBox.IsVisible = !textMode;
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
        (Url?.Shrink(Math.Max(4, (int)ViewSize.Width - 14 - CloseActionView.Length)) ?? "XML")
        + (_textMode ? " [TEXT]" : "  F2:Validate  F4:Text  /:XPath  *:Expand all");

    /// <summary>
    /// Starts the query in the background; its results are applied on the next idle pass, on the thread that owns the
    /// controls. A query started while another is running replaces it.
    /// </summary>
    public Task StartQueryAsync(string expression)
    {
        if (_tree is null)
        {
            return Task.CompletedTask;
        }

        XmlTree tree = _tree;
        _query = Task.Run(() => XmlXPathQuery.Run(tree, expression));
        _statusLabel.Text = "Running the query…";
        return _query;
    }

    /// <summary>
    /// Starts validating the document against the schema or DTD it names. It runs in the background; the issues are listed
    /// on the next idle pass, and Enter on one opens the element it is about. Validation works in the text view too, and
    /// switches to the tree to show its results.
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
        string? path = Url?.Path;
        _validation = Task.Run(() => XmlValidator.Validate(content, path));
        _statusLabel.Text = "Validating…";
        return _validation;
    }

    /// <summary>
    /// Applies the finished work on the thread that owns the controls: a query's results, or a validation's issues, each
    /// with its status line. This runs before the children's idle pass, so the list picks the results up in this same pass.
    /// </summary>
    public override void OnIdle()
    {
        if (_query is { IsCompleted: true } query)
        {
            _query = null;
            XmlXPathOutcome outcome = query.Result;
            _resultsModel.Items.ReplaceAll([.. outcome.Results]);
            _statusLabel.Text = outcome.Error is not null
                ? $"XPath error: {outcome.Error}"
                : outcome.Total == 0 ? "No matches"
                : outcome.Total == outcome.Results.Count ? $"{outcome.Total:N0} match(es)"
                : $"Showing {outcome.Results.Count:N0} of {outcome.Total:N0} matches";
            StateHasChanged();
        }

        if (_validation is { IsCompleted: true } validation)
        {
            _validation = null;
            ShowIssues(validation.Result);
            StateHasChanged();
        }

        base.OnIdle();
    }

    private void ShowIssues(IReadOnlyList<XmlValidationIssue> issues)
    {
        _resultsModel.Items.ReplaceAll([.. issues.Select(issue => new XmlXPathResult(
            issue.Line > 0 ? Tree?.ItemAtLine(issue.Line) ?? Tree?.Root : Tree?.Root,
            issue.Line > 0
                ? $"{(issue.IsError ? "error" : "warning")} at line {issue.Line}, column {issue.Column}"
                : "note",
            issue.Message))]);

        int errors = issues.Count(i => i.IsError);
        int warnings = issues.Count(i => !i.IsError && i.Line > 0);
        _statusLabel.Text = issues.Count == 0 ? "Valid"
            : issues is [{ Line: 0, IsError: false } note] ? note.Message
            : $"{errors:N0} error(s), {warnings:N0} warning(s)";
    }

    /// <summary>Opens the ancestors of a node, moves the cursor to it and gives the tree the keyboard.</summary>
    public void Reveal(XmlNodeItem node)
    {
        if (_tree is null)
        {
            return;
        }

        for (IHierarchicalItemModel? ancestor = node.Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            ancestor.IsOpened = true;
        }

        ClearFocus(_tree.Root);
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

    /// <summary>The keys this dialog adds to the tree's and the XPath box's, for the help screen.</summary>
    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        yield return ("F2", "Validate against the schema or DTD the document names");
        yield return ("F4", "Show the file's text, or the tree again");
        yield return ("/", "Move to the XPath box");
        yield return ("*", "Expand every node");
        yield return ("Enter", "Run the XPath query, or open the selected result in the tree");
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

        if (keyEvent.ConsoleKey == ConsoleKey.Enter && !_textMode && _xpathBox.IsFocused && !keyEvent.Control && !keyEvent.Alt)
        {
            await StartQueryAsync(_xpathBox.Text);
            return true;
        }

        if (keyEvent.ConsoleKey == ConsoleKey.Enter && !_textMode && _resultsBox.IsFocused && !keyEvent.Control && !keyEvent.Alt)
        {
            if (_resultsBox.SelectedItem?.Item.Node is { } node)
            {
                Reveal(node);
            }

            return true;
        }

        if (!_textMode && _treeView.IsFocused && !keyEvent.Control && !keyEvent.Alt)
        {
            if (keyEvent.KeyChar == '/')
            {
                _xpathBox.SetFocus();
                return true;
            }

            if (keyEvent.KeyChar == '*' && _tree is not null)
            {
                _treeView.ExpandAll();
                StateHasChanged();
                return true;
            }
        }

        return await base.HandleKeyEventAsync(keyEvent);
    }

    private sealed class CloseSelfCommand(XmlViewerDialog owner) : Command
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

        // Rows inside the border: the XPath box, the tree, the results, and the status line on the last row. A
        // terminal too small for the rows gets empty ones, not negative sizes.
        int width = Math.Max(0, (int)ViewSize.Width - 2);
        int content = Math.Max(3, (int)ViewSize.Height - 4);
        int resultsRows = Math.Clamp(content / 3, 2, 6);
        int treeRows = Math.Max(1, content - resultsRows);
        int statusRow = Math.Max(0, (int)ViewSize.Height - 2);

        _xpathBox.Position = new Position(1, 1);
        _xpathBox.ViewSize = new Size(width, 1);

        _treeView.Position = new Position(1, 2);
        _treeView.ViewSize = new Size(width, treeRows);

        _resultsBox.Position = new Position(1, 2 + treeRows);
        _resultsBox.ViewSize = new Size(width, resultsRows);

        _textViewerControl.Position = new Position(1, 2);
        _textViewerControl.ViewSize = new Size(width, content);

        _statusLabel.Position = new Position(1, statusRow);
        _statusLabel.ViewSize = new Size(width, 1);

        base.OnInitializeLayout();

        Layout.Center(this);
    }
}
