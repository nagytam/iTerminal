using iConsole.Commands;
using iConsole.Controls;
using iConsole.Controls.Buttons;
using iConsole.Data;
using iConsole.Dbf;
using iConsole.Helper;
using iConsole.Interfaces;
using iConsole.Layouts;
using iConsole.Localization;
using iConsole.Services;

namespace iConsole.Dialogs;

/// <summary>
/// The dBase table viewer: the records as a grid, with the field names on top and the columns scrolling sideways. The status
/// line gives the record and field counts, the code page the text was read with, how many deleted records are not shown, and
/// why a file that is not a table could not be read. Only the grid's keys apply; a memo's text is in a file this viewer does not read.
/// </summary>
public class DbfViewerDialog : Dialog
{
    private const string CloseActionView = " [X]";

    private readonly IContentModelService _contentModelService;
    private readonly CsvGrid _grid;
    private readonly Label _statusLabel;

    public DbfViewerDialog(IContentModelService contentModelService, IThemeService themeService)
        : base(nameof(DbfViewerDialog), themeService)
    {
        _contentModelService = contentModelService;
        _grid = new CsvGrid(themeService) { EmptyText = UiText.Get("Dbf.Grid.Empty", "The table has no records") };
        _statusLabel = new Label(string.Empty, themeService);
        AddControl(_grid);
        AddControl(_statusLabel);
    }

    /// <summary>The file shown.</summary>
    public Url? Url { get; set; }

    /// <summary>What the status line says: the counts, or why the file could not be read.</summary>
    public string Status => _statusLabel.Text;

    /// <summary>The table as read, or null while it loads or when it is not a dBase table.</summary>
    public DbfDocument? Document { get; private set; }

    public override async Task OnInitializeAsync()
    {
        await base.OnInitializeAsync();
        IFileSystem? fileSystem = (_contentModelService.GetFocusedModel() ?? _contentModelService.GetLastContentModel()) as IFileSystem;
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

        try
        {
            // The parse is in bytes, so a large table runs on a worker and the grid is filled on this thread.
            DbfDocument document = await Task.Run(() => DbfDocument.Parse(content));
            Document = document;
            _grid.SetData(document.Fields.Select(field => field.Name).ToList(), document.Rows);
            _statusLabel.Text = StatusText(document);
        }
        catch (FormatException exception)
        {
            _statusLabel.Text = string.Format(UiText.Get("Dbf.NotDbase", "Not a dBase table: {0}"), exception.Message);
        }

        StateHasChanged();
    }

    private static string StatusText(DbfDocument document)
    {
        var parts = new List<string>
        {
            string.Format(UiText.Get("Dbf.Status", "{0:N0} record(s), {1:N0} field(s), {2}"), document.Rows.Count, document.Fields.Count, DisplayName(document.CodePage)),
        };
        if (document.LastUpdated is { } updated)
        {
            parts.Add(string.Format(UiText.Get("Dbf.Updated", "updated {0:yyyy-MM-dd}"), updated));
        }

        if (document.DeletedCount > 0)
        {
            parts.Add(string.Format(UiText.Get("Dbf.Deleted", "{0:N0} deleted not shown"), document.DeletedCount));
        }

        if (document.Truncated)
        {
            parts.Add(string.Format(UiText.Get("Dbf.Truncated", "only the first {0:N0} records are shown"), DbfDocument.MaxRows));
        }

        return string.Join(", ", parts);
    }

    private static string DisplayName(System.Text.Encoding encoding) => TextEncodings.DisplayName(encoding);

    private string TitleText() =>
        Url?.Shrink(Math.Max(4, (int)ViewSize.Width - 14 - CloseActionView.Length)) ?? "dBase";

    public override void OnInitializeActionButtons()
    {
        AddActionButton(new ActionButton(UiText.Get("Common.Close", "Close"), CloseActionView, new CloseSelfCommand(this), ThemeService));
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        if (keyEvent.KeyPressType == KeyPressType.Down
            && keyEvent.ConsoleKey == ConsoleKey.Escape && !keyEvent.Shift && !keyEvent.Control && !keyEvent.Alt)
        {
            Close();
            return true;
        }

        return await base.HandleKeyEventAsync(keyEvent);
    }

    /// <summary>The key this dialog adds to the grid's, for the help screen.</summary>
    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        yield return ("Esc", UiText.Get("Hint.CloseViewer", "Close the viewer"));
    }

    private sealed class CloseSelfCommand(DbfViewerDialog owner) : Command
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

        // The grid inside the border, with the status line on the row above the bottom border.
        int width = Math.Max(0, (int)ViewSize.Width - 2);
        int content = Math.Max(1, (int)ViewSize.Height - 3);
        int statusRow = Math.Max(0, (int)ViewSize.Height - 2);

        _grid.Position = new Position(1, 1);
        _grid.ViewSize = new Size(width, content);

        _statusLabel.Position = new Position(1, statusRow);
        _statusLabel.ViewSize = new Size(width, 1);

        base.OnInitializeLayout();

        Layout.Center(this);
    }
}
