using iConsole.Buffers;
using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Interfaces;
using iConsole.Model;
using iConsole.Services;
using iConsole.Tasks;
using iTerminal.Core.Services;
using iTerminal.Core.Tasks;
using Serilog;
using System.ComponentModel;

namespace iTerminal.Core.Controls.Contents;

public class ItemInformation : iConsole.Controls.Control
{
    private static readonly TimeSpan FolderInformationRefreshDelay = TimeSpan.FromMilliseconds(5000);
    private readonly TaskManager _taskManager;
    private readonly object _folderInformationRefreshLock = new();
    private CancellationTokenSource? _folderInformationRefreshCancellationTokenSource;
    private string? _pendingFolderInformationRefreshUrl;
    private IContentModel? _pendingFolderInformationRefreshModel;
    private string? _lastFocusedInformationUrl;
    private IMemoryCacheService _memoryCacheService;
    private IContentModel? _contentModel;
    public IContentModel? ContentModel 
    { 
        get
        {
            return _contentModel;
        }
        set
        {
            if (_contentModel != value)
            {
                _contentModel = value;
            }
        }
    }

    private IContentModelService _contentModelService { get; set; }
    private ILogger _logger { get; set; }
    public string Text1 { get; set; } = string.Empty;
    public string Text2 { get; set; } = string.Empty;

    public ItemInformation(
        IContentModelService contentModelService,
        TaskManager taskManager,
        IMemoryCacheService memoryCacheService,
        IThemeService themeService,
        ILogger logger) 
        : base(themeService)
    {
        _contentModelService = contentModelService ?? NullContentModelService.Instance;
        _contentModelService.ModelChanged += OnContentModelServiceModelChanged;
        _taskManager = taskManager ?? new TaskManager();
        _memoryCacheService = memoryCacheService;
        _logger = logger ?? new LoggerConfiguration().CreateLogger();
        ViewSize = new Size(0, 1); // single row
        CanFocus = false;
        IsVisible = true;
    }

    private void OnContentModelServiceModelChanged(object? sender, EventArgs e)
    {
        FollowModelSwap();
        RefreshFocusedItemTexts();
    }

    /// <summary>
    /// A navigation that crosses drive kinds replaces the panel's model with a new instance, and
    /// nothing hands that instance to this strip: it went on describing the old model's cursor row
    /// — "Up to: …" for the folder the panel had left — for as long as the panel stayed on the
    /// other drive. The service is asked for the model now registered under this panel's name.
    /// </summary>
    private void FollowModelSwap()
    {
        var current = _contentModel;
        if (current is null)
        {
            return;
        }

        var registered = _contentModelService.ContentModels?.AsSnapshot().FirstOrDefault(model => model is not null && model.Name == current.Name);
        if (registered is null || ReferenceEquals(registered, current))
        {
            return;
        }

        current.PropertyChanged -= OnContentModelPropertyChanged;
        registered.PropertyChanged += OnContentModelPropertyChanged;
        _contentModel = registered;
        _lastFocusedInformationUrl = null;
    }

    private static bool AffectsFocusedItemLine(string? propertyName) =>
        propertyName is null or ""
        or nameof(IScrollableModel.CursorPosition)
        or nameof(IScrollableModel.ViewSize)
        or nameof(IContentModel.Entries)
        or nameof(IContentModel.Filter);

    private void OnContentModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!AffectsFocusedItemLine(e.PropertyName))
        {
            return;
        }

        RefreshFocusedItemTexts();
    }

    private void RefreshFocusedItemTexts()
    {
        var focusedEntry = ContentModel?.GetFocusedEntry();
        if (iTerminal.Core.Commands.FocusedEntry.IsParentEntry(focusedEntry))
        {
            // The ".." row is where the cursor lands after every navigation, and its URL is the
            // parent folder — so it used to start a recursive count of the *parent's* whole tree
            // each time (all of C:\ from any folder one level down) and show a spinner for minutes.
            // It is a way out, not an item; say where it leads.
            CancelPendingFolderInformationRefresh();
            _lastFocusedInformationUrl = null;
            _taskManager.CancelTask(typeof(FileInformationRefreshTask));
            var parentName = Path.GetFileName(focusedEntry!.Url.FullName.TrimEnd('\\', '/'));
            Text1 = string.IsNullOrEmpty(parentName) ? "Up to the parent folder" : $"Up to: {parentName}";
            Text2 = string.Empty;
        }
        else if (focusedEntry != null)
        {
            ScheduleFolderInformationRefresh(focusedEntry);
            Text1 = focusedEntry.GetDisplayName((int)ViewSize.Width);
            Text2 = focusedEntry.GetDisplayFileSize((int)ViewSize.Width);
        }
        else
        {
            CancelPendingFolderInformationRefresh();
            _lastFocusedInformationUrl = null;
            _taskManager.CancelTask(typeof(FileInformationRefreshTask));
            Text1 = string.Empty;
            Text2 = string.Empty;
        }
        StateHasChanged();
    }

    private void CancelPendingFolderInformationRefresh()
    {
        lock (_folderInformationRefreshLock)
        {
            _folderInformationRefreshCancellationTokenSource?.Cancel();
            _folderInformationRefreshCancellationTokenSource = null;
            _pendingFolderInformationRefreshUrl = null;
            _pendingFolderInformationRefreshModel = null;
        }
    }

    private void ScheduleFolderInformationRefresh(IEntry focusedEntry)
    {
        var focusedUrl = focusedEntry.Url.FullUrl;

        if (_lastFocusedInformationUrl != focusedUrl)
        {
            _lastFocusedInformationUrl = focusedUrl;
            _taskManager.CancelTask(typeof(FileInformationRefreshTask));
        }

        if (ContentModel is null
            || focusedEntry.Url.PathType != PathType.Folder
            || focusedEntry.NumberOfFiles is not null)
        {
            CancelPendingFolderInformationRefresh();
            return;
        }

        var currentModel = ContentModel;
        lock (_folderInformationRefreshLock)
        {
            if (_folderInformationRefreshCancellationTokenSource is not null
                && !_folderInformationRefreshCancellationTokenSource.IsCancellationRequested
                && _pendingFolderInformationRefreshUrl == focusedUrl
                && ReferenceEquals(_pendingFolderInformationRefreshModel, currentModel))
            {
                return;
            }
        }

        CancelPendingFolderInformationRefresh();

        CancellationTokenSource cancellationTokenSource;
        lock (_folderInformationRefreshLock)
        {
            _folderInformationRefreshCancellationTokenSource = new CancellationTokenSource();
            _pendingFolderInformationRefreshUrl = focusedUrl;
            _pendingFolderInformationRefreshModel = currentModel;
            cancellationTokenSource = _folderInformationRefreshCancellationTokenSource;
        }

        var expectedUrl = focusedUrl;
        var expectedModel = currentModel;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(FolderInformationRefreshDelay, cancellationTokenSource.Token);
                if (cancellationTokenSource.Token.IsCancellationRequested
                    || ContentModel is null
                    || !ReferenceEquals(ContentModel, expectedModel)
                    || ContentModel.GetFocusedEntry()?.Url.FullUrl != expectedUrl)
                {
                    return;
                }

                _ = _taskManager.Add(new FileInformationRefreshTask(focusedEntry, ContentModel, _memoryCacheService, _logger));
            }
            catch (TaskCanceledException)
            {
                // expected cancellation — nothing to do
            }
            catch (Exception ex)
            {
                try
                {
                    _logger?.Error(ex, "Folder information refresh failed for {Url}", expectedUrl);
                }
                catch
                {
                    // swallow logger failures to avoid secondary exceptions
                }
            }
            finally
            {
                lock (_folderInformationRefreshLock)
                {
                    if (ReferenceEquals(_folderInformationRefreshCancellationTokenSource, cancellationTokenSource))
                    {
                        _folderInformationRefreshCancellationTokenSource = null;
                        _pendingFolderInformationRefreshUrl = null;
                        _pendingFolderInformationRefreshModel = null;
                    }
                }

                cancellationTokenSource.Dispose();
            }
        });
    }

    public override void OnInitializeCommands()
    {
        ArgumentNullException.ThrowIfNull(ContentModel);
        base.OnInitializeCommands();
        // -= first: this can run again, and a second subscription would refresh the line twice.
        ContentModel.PropertyChanged -= OnContentModelPropertyChanged;
        ContentModel.PropertyChanged += OnContentModelPropertyChanged;
    }

    public override void OnInitializeLayout()
    {
        base.OnInitializeLayout();
    }

    public override void Draw(IBuffer buffer)
    {
        var colorScheme = ThemeService.CurrentColorScheme;
        // One column kept free on the right: the two panels' strips sit side by side, and a detail
        // right-aligned to the very edge ran straight into the next strip ("files: 12Folder: …").
        var width = (int)ViewSize.Width;
        var line = width > 1 ? ComposeLine(Text1, Text2, width - 1) + " " : ComposeLine(Text1, Text2, width);
        var text = new ConxelString(line, colorScheme.Foreground, colorScheme.Background);
        buffer.Write(ZOrder, AbsolutePosition, ViewSize, text);
    }

    /// <summary>
    /// The name on the left, the size (or file count) right-aligned. The entry pads both texts to the
    /// full width, so joined naively the size always started past the right edge and was never seen;
    /// the size is the part worth keeping, so it is the name that gets shrunk when the two do not fit.
    /// </summary>
    internal static string ComposeLine(string? name, string? detail, int width)
    {
        if (width <= 0)
        {
            return string.Empty;
        }

        var left = name?.TrimEnd() ?? string.Empty;
        var right = detail?.TrimEnd() ?? string.Empty;

        if (right.Length == 0)
        {
            return (left.Shrink(width) ?? string.Empty).ExpandRight(width);
        }

        if (right.Length + 2 >= width)
        {
            return (right.Shrink(width) ?? string.Empty).ExpandRight(width);
        }

        var leftWidth = width - right.Length - 2;
        var shrunk = left.Shrink(leftWidth) ?? string.Empty;
        return shrunk.ExpandRight(leftWidth) + "  " + right;
    }
}
