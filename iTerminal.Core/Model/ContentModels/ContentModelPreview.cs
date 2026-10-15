using iConsole.Data;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Preview;

namespace iTerminal.Core.Model.ContentModels;

/// <summary>
/// The <c>preview://</c> drive: a panel that shows the file or folder under the <i>other</i> panel's
/// cursor, and follows that cursor (Ctrl+F3, the classic two-panel quick view).
///
/// <para>It listens to <see cref="IContentModelService.ModelChanged"/>, which fires on every cursor
/// move and listing change of either panel, and rebuilds only when the entry under the other
/// cursor — or the width it is drawn at — is not the one already shown. The build itself runs on a
/// background task after a short pause (<see cref="Debounce"/>): holding an arrow key over a
/// folder of PDFs must not start a render per row, and none of it may run on the key handler that
/// moved the cursor. A generation counter drops a build that a newer cursor move has overtaken.</para>
///
/// <para>Each row is a <see cref="PreviewLineEntry"/>, so the panel scrolls like any other when it
/// has focus, and Alt+text highlights matching lines through the panel's own filter. Nothing in it
/// exists as a file: F3, F5 and F8 find nothing to act on.</para>
/// </summary>
public sealed class ContentModelPreview : ContentModelBase, IDisposable
{
    /// <summary>How long the other cursor has to rest before its entry is previewed.</summary>
    public static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(80);

    private const int FallbackWidth = 80;

    private readonly ISettingsService _settingsService;
    private readonly IContentModelService _contentModelService;
    private readonly IFilePreviewService _previewService;
    private readonly Lock _lock = new();

    private string? _shownKey;
    private int _generation;
    private CancellationTokenSource? _buildCancellation;
    private string _title = "Preview";
    private string _summary = string.Empty;
    private bool _isDisposed;

    public ContentModelPreview(
        string name,
        ISettingsService? settingsService,
        IContentModelService contentModelService,
        IFilePreviewService previewService,
        IFrequentExecutionGuardService frequentExecutionGuardService)
        : base(name, frequentExecutionGuardService)
    {
        _settingsService = settingsService ?? NullSettingsService.Instance;
        _contentModelService = contentModelService;
        _previewService = previewService;
        _contentModelService.ModelChanged += OnModelChanged;
    }

    public override bool IsReadOnly => true;

    /// <summary>The rows are lines of a file, not entries.</summary>
    public override bool CanMark => false;

    /// <summary>The rows are the lines of a file.</summary>
    public override bool CanSort => false;

    /// <summary>Where Ctrl+F3 takes this panel back to.</summary>
    public Url ReturnUrl => ContentModelAsyncDrive.GetReturnUrl(_settingsService, Name);

    /// <summary>The build started last. Nothing waits on it; it exists so a test can.</summary>
    public Task BuildTask { get; private set; } = Task.CompletedTask;

    /// <summary>The panel whose cursor this one follows.</summary>
    private IContentModel? Source =>
        _contentModelService.ContentModels.FirstOrDefault(m => m.Name != Name && m is not ContentModelPreview);

    private int Width => ViewSize.Width > 0 ? (int)ViewSize.Width : FallbackWidth;

    #region Navigation

    public override void ReadUrl() => SetUrl(new Url(Container.PREVIEW));

    public override void SetUrl(Url url)
    {
        _settingsService.Set(Name, url);
        Url = url;
        ResetNavigation();
        Entries.ReplaceAll([]);
        Schedule(force: true);
    }

    public override void Refresh(bool forceRefresh = false) => Schedule(force: true);

    private void OnModelChanged(object? sender, EventArgs e)
    {
        if (!_isDisposed)
        {
            Schedule(force: false);
        }
    }

    /// <summary>
    /// Starts a build for the other panel's current entry unless that entry is the one on screen.
    /// Cheap enough to run on every cursor move: the comparison is one string.
    /// </summary>
    private void Schedule(bool force)
    {
        var source = Source;
        var entry = source?.GetFocusedEntry();
        var width = Width;
        var key = $"{source?.Name}|{entry?.Url.FullUrl}|{width}";

        int generation;
        CancellationTokenSource cancellation;
        lock (_lock)
        {
            if (!force && key == _shownKey)
            {
                return;
            }

            _shownKey = key;
            generation = ++_generation;
            _buildCancellation?.Cancel();
            cancellation = _buildCancellation = new CancellationTokenSource();
        }

        BuildTask = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(Debounce, cancellation.Token);
                var preview = await _previewService.BuildAsync(entry, source, width, cancellation.Token);
                if (generation != Volatile.Read(ref _generation) || _isDisposed)
                {
                    return;
                }

                _title = preview.Title;
                _summary = preview.Summary;
                Entries.ReplaceAll([.. preview.Lines.Select(line => (IEntry)new PreviewLineEntry(line))]);
                ResetNavigation();
                if (preview.StartAtEnd && preview.Lines.Count > 0)
                {
                    var rows = preview.Lines.Count;
                    ScrollPosition.Y = Math.Max(0, rows - (int)ViewSize.Height);
                    CursorPosition.Y = rows - 1;
                }

                Update();
            }
            catch (OperationCanceledException)
            {
                // Overtaken by a newer cursor move.
            }
        });
    }

    #endregion

    #region Chrome

    public override string GetHeader(int width)
    {
        // The width is only known once the panel is laid out, and changes with the window; a preview
        // built for another width is rebuilt rather than drawn cut off or with a ragged edge.
        if (ViewSize.Width > 0 && _shownKey is not null && !_shownKey.EndsWith("|" + Width, StringComparison.Ordinal))
        {
            Schedule(force: false);
        }

        return $"Preview: {_title}".Shrink(width) ?? string.Empty;
    }

    public override string GetFooter()
    {
        var footer = _summary.Length > 0 ? $" {_summary} │ Ctrl+F3: close " : " Ctrl+F3: close ";
        return Filter is null ? footer : $"{footer}Filter: {Filter} ";
    }

    #endregion

    #region Not a file system

    public override bool ExistsFile(Url url) => false;

    public override bool ExistsFolder(Url url) => false;

    public override string GetAvailableDiskSpace() => string.Empty;

    public override Task RunTerminalCommandAsync(ITerminalModel terminalModel) => Task.CompletedTask;

    #endregion

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _contentModelService.ModelChanged -= OnModelChanged;
        lock (_lock)
        {
            _buildCancellation?.Cancel();
        }
    }
}

/// <summary>One row of a preview: a line of text drawn across the whole panel, no columns.</summary>
public sealed record PreviewLineEntry : EntryBase
{
    private static readonly Url LineUrl = new(Container.PREVIEW + "line");

    public PreviewLineEntry(string text)
    {
        Text = text;
        Url = LineUrl;
    }

    public string Text { get; }

    /// <summary>The text itself, so the panel's type-to-search filter matches lines.</summary>
    public override string DisplayName => Text;

    public override string Name => Text;

    public override string GetDisplayName(int width) => string.Empty;

    public override string GetDisplayFileSize(int width) => string.Empty;

    public override ConxelString FormatItemName(int width, bool cursor, ColorScheme colorScheme, VisualState visualState)
    {
        var rowWidth = Math.Max(0, width - 2);
        var text = Text.Length > rowWidth ? Text[..rowWidth] : Text.PadRight(rowWidth);

        // The cursor is only shown while the preview has focus: that is when the arrows scroll it.
        // Unfocused, a highlighted row would look like a selection in a list that has none.
        var showCursor = cursor && visualState == VisualState.Focused;
        var foreground = showCursor ? colorScheme.SelectedFocusedForeground : colorScheme.Foreground;
        var background = showCursor ? colorScheme.SelectedFocusedBackground : colorScheme.Background;
        return new ConxelString(text, foreground, background);
    }
}
