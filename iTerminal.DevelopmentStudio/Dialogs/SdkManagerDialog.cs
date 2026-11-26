using iConsole.Controls;
using iConsole.Controls.Buttons;
using iConsole.Controls.ScrollBar;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Localization;
using iConsole.Model;
using iConsole.Services;
using iTerminal.Core.Controls;
using iTerminal.Core.Model.Dotnet;
using iTerminal.Core.Services.Sdk;

namespace iTerminal.DevelopmentStudio.Dialogs;

/// <summary>
/// The SDK manager: the .NET SDKs and JDKs installed under the data folder, and the ones that can be downloaded for this
/// computer, newest first (the newest of each channel is marked latest). Enter installs the one under the cursor, or, on an
/// installed JDK, makes it the one Java builds and runs use. Del removes an installed one after a y/n, F5 reads the lists again
/// and Esc closes. The studio reads the choices again when the dialog closes (<see cref="Closed"/>).
///
/// <list type="bullet">
/// <item>Listing and every install or removal run on a background task. A worker only stores its answer under a lock;
/// <see cref="OnIdle"/> applies it on the UI thread, and a listing that was overtaken by a newer one is dropped.</item>
/// <item>Closing cancels a download in progress; the partial archive is deleted and nothing is left half installed.</item>
/// <item>The dialog has no text box, so the letter keys are commands, never typed text.</item>
/// </list>
/// </summary>
public class SdkManagerDialog : Dialog
{
    private const int DetailRows = 2;

    private readonly ISdkManager _manager;
    private readonly ConcurrentObservableCollection<SdkRow> _rows = [];
    private readonly ListBox<SdkRow> _listBox;
    private readonly ScrollBar _scrollBar;
    private readonly InfoLines _details;
    private readonly CloseButton _closeButton;

    // Written by background tasks under _gate, read by OnIdle.
    private readonly object _gate = new();
    private long _latestRequest;
    private (long Request, IReadOnlyList<SdkRow> Rows, string? Error)? _listed;
    private (string Message, bool IsError)? _finished;
    private string? _progress;

    // Touched on the UI thread only.
    private bool _loaded;
    private string _listError = string.Empty;
    private string _status = string.Empty;
    private bool _statusIsError;
    private bool _isBusy;
    private SdkRow? _awaitingRemoval;

    /// <summary>Cancelled when the dialog closes: a listing or a download in progress stops with it.</summary>
    private readonly CancellationTokenSource _lifetime = new();

    public SdkManagerDialog(ISdkManager manager, IThemeService themeService)
        : base(nameof(SdkManagerDialog), themeService)
    {
        _manager = manager;
        Title = UiText.Get("SdkManager.Title", "SDK manager");

        var listModel = new ListBoxModel<SdkRow>(_rows)
        {
            TransformItem = TransformItem,
        };
        _listBox = new ListBox<SdkRow>(listModel, themeService)
        {
            EmptyText = ReadingText,
        };
        _scrollBar = new ScrollBar(themeService)
        {
            ScrollableModel = _listBox.ScrollableModel,
        };
        _details = new InfoLines(themeService);
        _details.Lines = [new InfoLines.Line(UiText.Get("SdkManager.Details", "Installed SDKs and JDKs are kept in iTerminal's data folder. Enter on an installed JDK makes it the Java that builds and runs use."))];
        _closeButton = new CloseButton(themeService);

        AddControl(_listBox);
        AddControl(_scrollBar);
        AddControl(_details);
        AddControl(_closeButton);
    }

    /// <summary>Raised when the dialog closes, so the studio reads the installed SDKs and the chosen JDK again.</summary>
    public Action? Closed { get; set; }

    private static string ReadingText => UiText.Get("SdkManager.Reading", " Reading the SDKs…");

    /// <summary>The row under the cursor, or null when the list is empty.</summary>
    public SdkRow? SelectedRow
    {
        get
        {
            var listItems = _listBox.ListModel.ListItems;
            var index = (int)_listBox.ScrollableModel.CursorPosition.Y;
            return index >= 0 && index < listItems.Count ? listItems[index].Item : null;
        }
    }

    /// <summary>The last status line.</summary>
    public string Status => _status;

    /// <summary>The rows the list shows, in order, for a test to read.</summary>
    public IReadOnlyList<SdkRow> ListedRows() => _rows.ToList();

    /// <summary>The line of text the dialog shows under the list, for a test to read.</summary>
    public string DetailsText => _details.Text;

    public override async Task ShowAsync()
    {
        await base.ShowAsync();
        RequestListing();
    }

    public override void OnIdle()
    {
        ApplyFinished();
        ApplyListing();
        if (_isBusy && _progress is { } progress && _awaitingRemoval is null)
        {
            SetStatus(progress, isError: false);
        }

        if (_listError.Length > 0) _listBox.EmptyText = " " + _listError;
        else if (_loaded) _listBox.EmptyText = UiText.Get("SdkManager.None", " No SDKs found.");
        else _listBox.EmptyText = ReadingText;
        base.OnIdle();
    }

    /// <summary>Esc closes the dialog. A download in progress is cancelled, and the studio is told the dialog closed.</summary>
    public override void Close()
    {
        _lifetime.Cancel();
        base.Close();
        Closed?.Invoke();
    }

    public override void OnInitializeLayout()
    {
        if (Parent != null)
        {
            CenterInParent(Parent.ViewSize * 0.8);
        }

        var listHeight = Math.Max(1, ViewSize.Height - 6);
        _listBox.Position = new Position(1, 1);
        _listBox.ViewSize = new Size(ViewSize.Width - 2, listHeight);
        _listBox.ScrollableModel.ViewSize = _listBox.ViewSize;

        _scrollBar.Position = new Position(ViewSize.Width - 1, 1);
        _scrollBar.ViewSize = new Size(1, listHeight);

        _details.Position = new Position(2, 1 + listHeight);
        _details.ViewSize = new Size(Math.Max(1, ViewSize.Width - 4), DetailRows);

        _closeButton.Position = new Position(1, ViewSize.Height - 2);
        _closeButton.ViewSize = new Size(ViewSize.Width - 2, 1);
        base.OnInitializeLayout();
    }

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        yield return ("Enter", UiText.Get("SdkManager.HintInstall", "Install the SDK, or use the installed JDK for Java"));
        yield return ("Del", UiText.Get("SdkManager.HintRemove", "Remove an installed SDK or JDK"));
        yield return ("F5", UiText.Get("SdkManager.HintRefresh", "Read the lists again"));
        yield return ("Esc", UiText.Get("SdkManager.HintClose", "Close"));
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        if (_awaitingRemoval is { } removal && keyEvent.ConsoleKey != ConsoleKey.Escape)
        {
            _awaitingRemoval = null;
            if (!keyEvent.Alt && !keyEvent.Control && char.ToUpperInvariant(keyEvent.KeyChar) == 'Y')
            {
                Remove(removal);
            }
            else
            {
                SetStatus(UiText.Get("SdkManager.Cancelled", "Cancelled."), isError: false);
            }

            return true;
        }

        // Esc is claimed by the Dialog before this runs, so it still closes the dialog.
        if (keyEvent.ConsoleKey == ConsoleKey.F5 && !keyEvent.Alt && !keyEvent.Control && !keyEvent.Shift)
        {
            SetStatus(UiText.Get("SdkManager.Refreshing", "Refreshing…"), isError: false);
            RequestListing();
            return true;
        }

        if (keyEvent.ConsoleKey == ConsoleKey.Enter && !keyEvent.Alt && !keyEvent.Control && !keyEvent.Shift)
        {
            if (SelectedRow is { } row) Enter(row);
            return true;
        }

        if (keyEvent.ConsoleKey == ConsoleKey.Delete && !keyEvent.Alt && !keyEvent.Control && !keyEvent.Shift)
        {
            if (SelectedRow is { Folder: not null } installed)
            {
                _awaitingRemoval = installed;
                SetStatus(string.Format(UiText.Get("SdkManager.ConfirmRemove", "Remove {0} {1} from iTerminal? (y/n)"), KindLabel(installed.Kind), installed.Version), isError: false);
            }
            else
            {
                SetStatus(UiText.Get("SdkManager.NotInstalled", "That one is not installed, so there is nothing to remove."), isError: true);
            }

            return true;
        }

        return await base.HandleKeyEventAsync(keyEvent);
    }

    private ListItem<SdkRow> TransformItem(SdkRow row, CancellationToken cancellationToken) =>
        new(row)
        {
            Value = $"{KindLabel(row.Kind),-10} {row.Version,-30} {row.State}",
            IsEnabled = true,
        };

    private static string KindLabel(SdkKind kind) => kind == SdkKind.DotNet
        ? UiText.Get("SdkManager.KindDotNet", ".NET SDK")
        : UiText.Get("SdkManager.KindJdk", "Java JDK");

    // ---- listing --------------------------------------------------------------------------------

    /// <summary>
    /// Starts a background read of the installed and the downloadable SDKs. A read still running is not waited for: the
    /// newest request wins, so an older answer that lands late is dropped, and a read started after an install always shows it.
    /// </summary>
    private void RequestListing()
    {
        long request;
        lock (_gate)
        {
            request = ++_latestRequest;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var rows = await ReadRowsAsync(_lifetime.Token);
                StoreListing(request, rows.Rows, rows.Error);
            }
            catch (Exception exception)
            {
                StoreListing(request, [], exception.Message);
            }
        }, _lifetime.Token);
    }

    /// <summary>Keeps a listing unless a newer one is already waiting: a slow older read that lands late must not overwrite it.</summary>
    private void StoreListing(long request, IReadOnlyList<SdkRow> rows, string? error)
    {
        lock (_gate)
        {
            if (_listed is not { } waiting || request > waiting.Request)
            {
                _listed = (request, rows, error);
            }
        }
    }

    /// <summary>The installed SDKs first and then the downloadable ones, for each kind. A publisher that cannot be reached leaves its error, not the installed rows.</summary>
    private async Task<(IReadOnlyList<SdkRow> Rows, string? Error)> ReadRowsAsync(CancellationToken cancellationToken)
    {
        var rows = new List<SdkRow>();
        string? error = null;
        string javaHome = _manager.ActiveJavaHome ?? string.Empty;

        var installedDotNet = _manager.InstalledDotNetSdks();
        rows.AddRange(installedDotNet.Select(sdk => new SdkRow(SdkKind.DotNet, sdk.Version, UiText.Get("SdkManager.Installed", "Installed"), sdk.Root, null, null)));
        var installedJdks = _manager.InstalledJdks();
        rows.AddRange(installedJdks.Select(jdk => new SdkRow(
            SdkKind.Jdk,
            jdk.Version,
            jdk.Home == javaHome ? UiText.Get("SdkManager.ActiveJava", "Installed, used for Java") : UiText.Get("SdkManager.Installed", "Installed"),
            jdk.Folder,
            jdk.Home,
            null)));

        foreach (SdkKind kind in new[] { SdkKind.DotNet, SdkKind.Jdk })
        {
            try
            {
                var installed = rows.Where(row => row.Kind == kind && row.Folder is not null).Select(row => row.Version).ToHashSet(StringComparer.Ordinal);
                foreach (AvailableSdk sdk in await _manager.ListAvailableAsync(kind, cancellationToken))
                {
                    if (installed.Contains(sdk.Version)) continue;
                    rows.Add(new SdkRow(kind, sdk.Version, StateOf(sdk), null, null, sdk));
                }
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                // Any failure of the publisher (offline, a feed that changed shape) leaves the installed rows listed.
                error = string.Format(UiText.Get("SdkManager.ListFailed", "Could not reach the publisher: {0}"), exception.Message);
            }
        }

        return (rows, error);
    }

    /// <summary>What a download's row says: a .NET preview (its version has a label), the newest of its channel, or plainly available.</summary>
    private static string StateOf(AvailableSdk sdk)
    {
        if (sdk.Kind == SdkKind.DotNet && sdk.Version.Contains('-')) return UiText.Get("SdkManager.Preview", "Available, a preview");
        if (sdk.IsLatest) return UiText.Get("SdkManager.Latest", "Available, the latest");
        return UiText.Get("SdkManager.Available", "Available");
    }

    private void ApplyListing()
    {
        (long Request, IReadOnlyList<SdkRow> Rows, string? Error)? listed;
        lock (_gate)
        {
            listed = _listed;
            _listed = null;
        }

        if (listed is not { } answer || answer.Request != _latestRequest)
        {
            return;
        }

        _loaded = true;
        _listError = answer.Error ?? string.Empty;
        var selected = SelectedRow;
        _rows.ReplaceAll(answer.Rows.ToList());
        _listBox.UpdateModel();
        RestoreCursor(selected);
    }

    /// <summary>Puts the cursor back on the row it was on, so a refresh does not move it.</summary>
    private void RestoreCursor(SdkRow? previous)
    {
        if (previous is null)
        {
            return;
        }

        var index = _listBox.ListModel.ListItems.ToList().FindIndex(item => item.Item.Kind == previous.Kind && item.Item.Version == previous.Version);
        if (index >= 0)
        {
            _listBox.ScrollableModel.CursorPosition = new Position(0, index);
        }
    }

    // ---- actions --------------------------------------------------------------------------------

    private void Enter(SdkRow row)
    {
        if (row.Available is { } available)
        {
            Install(row, available);
            return;
        }

        if (row is { Kind: SdkKind.Jdk, Folder: not null, Home: { } home })
        {
            if (_manager.ActiveJavaHome == home)
            {
                SetStatus(string.Format(UiText.Get("SdkManager.AlreadyActive", "{0} is already the Java in use."), row.Version), isError: false);
                return;
            }

            UseJava(row, home);
            return;
        }

        SetStatus(UiText.Get("SdkManager.DotNetUsed", ".NET SDKs installed here join the V cycle of the studio."), isError: false);
    }

    private void Install(SdkRow row, AvailableSdk available)
    {
        if (_isBusy)
        {
            SetStatus(UiText.Get("SdkManager.Busy", "Still working on the last request."), isError: true);
            return;
        }

        _isBusy = true;
        SetStatus(string.Format(UiText.Get("SdkManager.Downloading", "Downloading {0}…"), row.Version), isError: false);
        var progress = new Progress<string>(text =>
        {
            lock (_gate)
            {
                _progress = text;
            }
        });

        _ = Task.Run(async () =>
        {
            try
            {
                await _manager.InstallAsync(available, progress, _lifetime.Token);
                lock (_gate)
                {
                    _finished = (string.Format(UiText.Get("SdkManager.Done", "{0} is installed."), row.Version), false);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                lock (_gate)
                {
                    _finished = (string.Format(UiText.Get("SdkManager.Failed", "Could not install {0}: {1}"), row.Version, exception.Message), true);
                }
            }
            catch (OperationCanceledException)
            {
                lock (_gate)
                {
                    _finished = (UiText.Get("SdkManager.Cancelled", "Cancelled."), false);
                }
            }
        });
    }

    private void Remove(SdkRow row)
    {
        if (_isBusy || row.Folder is null)
        {
            SetStatus(UiText.Get("SdkManager.Busy", "Still working on the last request."), isError: true);
            return;
        }

        _isBusy = true;
        string folder = row.Folder;
        _ = Task.Run(() =>
        {
            try
            {
                _manager.Uninstall(folder);
                lock (_gate)
                {
                    _finished = (string.Format(UiText.Get("SdkManager.Removed", "{0} was removed."), row.Version), false);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                lock (_gate)
                {
                    _finished = (exception.Message, true);
                }
            }
        });
    }

    private void UseJava(SdkRow row, string home)
    {
        if (_isBusy)
        {
            SetStatus(UiText.Get("SdkManager.Busy", "Still working on the last request."), isError: true);
            return;
        }

        _isBusy = true;
        _ = Task.Run(() =>
        {
            try
            {
                _manager.UseJavaHome(home);
                lock (_gate)
                {
                    _finished = (string.Format(UiText.Get("SdkManager.JavaChosen", "Java builds and runs use {0}."), row.Version), false);
                }
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException)
            {
                lock (_gate)
                {
                    _finished = (exception.Message, true);
                }
            }
        });
    }

    private void ApplyFinished()
    {
        (string Message, bool IsError)? finished;
        lock (_gate)
        {
            finished = _finished;
            _finished = null;
            if (finished is not null)
            {
                _progress = null;
            }
        }

        if (finished is not { } answer)
        {
            return;
        }

        _isBusy = false;
        SetStatus(answer.Message, answer.IsError);
        RequestListing();
    }

    private void SetStatus(string message, bool isError)
    {
        _status = message;
        _statusIsError = isError;
    }
}

/// <summary>One line of the SDK manager: an installed SDK or JDK (its folder and, for a JDK, its home), or one that can be downloaded.</summary>
/// <param name="Kind">.NET SDK or JDK.</param>
/// <param name="Version">The version or release name.</param>
/// <param name="State">What the row says about it (installed, active for Java, the latest available, available).</param>
/// <param name="Folder">The install's version folder; null for one that is not installed.</param>
/// <param name="Home">The JDK home of an installed JDK (what the Java setting holds); null otherwise.</param>
/// <param name="Available">The download, for one that is not installed; null otherwise.</param>
public sealed record SdkRow(SdkKind Kind, string Version, string State, string? Folder, string? Home, AvailableSdk? Available);
