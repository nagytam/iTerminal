using iConsole.Data;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Data.Music;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Music;

namespace iTerminal.Core.Model.ContentModels;

/// <summary>
/// Shared behaviour of the two music drives. Both are virtual, read-only listings that live entirely
/// in memory, so the whole file-system half of <see cref="IContentModel"/> collapses to "no" and the
/// interesting part is what <see cref="Activate"/> does to a row.
///
/// <para>Refreshing is synchronous on purpose, unlike the file-system model: a playlist is a list the
/// process already holds and a library search runs against an in-memory copy of the index, so
/// neither can approach the 200 ms key-handler budget and neither needs a background task.</para>
/// </summary>
public abstract class ContentModelMusicBase : ContentModelBase, IMusicContentModel, IDisposable
{
    private static readonly TimeSpan SeekStep = TimeSpan.FromSeconds(5);

    private readonly FolderHistoryService? _folderHistoryService;
    private readonly ISettingsService _settingsService;

    private bool _isDisposed;

    protected ContentModelMusicBase(
        string name,
        ISettingsService? settingsService,
        IMusicPlayerService player,
        FolderHistoryService? folderHistoryService,
        IFrequentExecutionGuardService frequentExecutionGuardService)
        : base(name, frequentExecutionGuardService)
    {
        _settingsService = settingsService ?? NullSettingsService.Instance;
        _folderHistoryService = folderHistoryService;
        Player = player;
    }

    /// <summary>The engine both drives are a view over. Never owned here — it outlives every panel.</summary>
    protected IMusicPlayerService Player { get; }

    /// <summary>The scheme this drive lives under, e.g. <c>musicplaylist://</c>.</summary>
    protected abstract string UrlScheme { get; }

    /// <summary>Nothing here can be written to, renamed or deleted through the file operations.</summary>
    public override bool IsReadOnly => true;

    /// <summary>The music drives mark with their own Insert (the catalog) or not at all (the queue).</summary>
    public override bool CanMark => false;

    /// <summary>A queue's order is the queue, and the catalog's is its own to decide.</summary>
    public override bool CanSort => false;

    /// <summary>
    /// One-line note about the last thing that happened ("Added 12 tracks", "Output: Speakers").
    /// Shown in the footer, which is the only place a panel has for feedback.
    /// </summary>
    public string StatusMessage { get; private set; } = string.Empty;

    /// <summary>Performs the row's action. Navigation into folders is the command's job, not this one's.</summary>
    public abstract void Activate(IEntry entry);

    /// <summary>
    /// Runs a transport or volume action that has no row behind it — the direct keys the panels bind
    /// so the common controls do not require a trip into a control folder. Implemented here rather
    /// than per-drive because pausing from the catalog panel has to mean the same thing as pausing
    /// from the playlist panel.
    /// </summary>
    public virtual void Perform(MusicAction action)
    {
        switch (action)
        {
            case MusicAction.PlayPause:
                Player.TogglePlayPause();
                break;

            case MusicAction.Stop:
                Player.Stop();
                break;

            case MusicAction.Next:
                Player.Next();
                break;

            case MusicAction.Previous:
                Player.Previous();
                break;

            case MusicAction.SeekBackward:
                Player.SeekRelative(-SeekStep);
                break;

            case MusicAction.SeekForward:
                Player.SeekRelative(SeekStep);
                break;

            case MusicAction.CycleRepeat:
                Player.RepeatMode = Player.RepeatMode switch
                {
                    RepeatMode.Off => RepeatMode.All,
                    RepeatMode.All => RepeatMode.One,
                    _ => RepeatMode.Off,
                };
                SetStatus($"Repeat {Player.RepeatMode.ToString().ToLowerInvariant()}");
                return;

            case MusicAction.ToggleShuffle:
                Player.IsShuffled = !Player.IsShuffled;
                SetStatus($"Shuffle {(Player.IsShuffled ? "on" : "off")}");
                return;

            case MusicAction.VolumeUp:
                Player.Volume += 5;
                SetStatus($"Volume {Player.Volume}%");
                return;

            case MusicAction.VolumeDown:
                Player.Volume -= 5;
                SetStatus($"Volume {Player.Volume}%");
                return;
        }

        Update();
    }

    protected void SetStatus(string message)
    {
        StatusMessage = message ?? string.Empty;
        Update();
    }

    public override void ReadUrl()
    {
        var stored = _settingsService.Get(Name);
        SetUrl(new Url(string.IsNullOrWhiteSpace(stored) ? UrlScheme : stored));
    }

    public override void SetUrl(Url url)
    {
        _settingsService.Set(Name, url);
        _folderHistoryService?.Add(url);
        StatusMessage = string.Empty;

        // Going somewhere ends the search that led there — the same thing a file panel does. On
        // these drives it is not cosmetic: both models build their listing *from* the filter, so a
        // filter left set would have the panel draw the old hits under the new URL.
        Filter = null;

        Entries.Clear();
        Url = url;
        ResetNavigation();
        Refresh();
    }

    /// <summary>
    /// Path inside the drive, without the scheme and without the leading or trailing slash, split
    /// into its percent-decoded segments. <c>musiccatalog://The%20Beatles/Abbey%20Road/</c> becomes
    /// <c>["The Beatles", "Abbey Road"]</c>.
    /// </summary>
    protected string[] PathSegments()
    {
        var path = Url?.Path ?? string.Empty;
        return [.. path
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Uri.UnescapeDataString)];
    }

    /// <summary>
    /// Builds a child URL from already-decoded segments. Escaping is not cosmetic here: artist and
    /// album names routinely contain slashes ("AC/DC"), which would otherwise split into two levels.
    /// </summary>
    protected Url ChildUrl(params string[] segments) =>
        new(UrlScheme + string.Join('/', segments.Select(Uri.EscapeDataString)) + "/");

    /// <summary>Adds the <c>..</c> row. Only called below the root, where there is somewhere to go.</summary>
    protected void AddParentEntry(List<IEntry> entries)
    {
        entries.Add(new MusicEntry(MusicEntryKind.Folder, Url.ParentUrl, "..").WithValue("Up"));
    }

    protected static string FormatDuration(TimeSpan value)
    {
        if (value <= TimeSpan.Zero)
        {
            return "--:--";
        }

        return value.TotalHours >= 1
            ? $"{(int)value.TotalHours}:{value.Minutes:00}:{value.Seconds:00}"
            : $"{value.Minutes}:{value.Seconds:00}";
    }

    /// <summary>Appends the status note to a footer when there is one, so nothing is lost either way.</summary>
    protected string WithStatus(string footer)
    {
        var text = footer;
        if (!string.IsNullOrEmpty(StatusMessage))
        {
            text += $"{Border.Horizontal}{StatusMessage}";
        }

        if (Filter is not null)
        {
            text += $"{Border.Horizontal}Filter: {Filter}";
        }

        return text;
    }

    #region Virtual drive: no file system behind it

    public override bool ExistsFile(Url url) => false;

    public override bool ExistsFolder(Url url) => url.PathType == PathType.Folder;

    public override string GetAvailableDiskSpace() => string.Empty;

    public override Task RunTerminalCommandAsync(ITerminalModel terminalModel) => Task.CompletedTask;

    #endregion

    #region IDisposable

    /// <summary>
    /// A music model is the only content model that subscribes to a long-lived singleton, so it is
    /// also the only one that has to be let go of: <see cref="ContentModelService.AddContentModel"/>
    /// disposes the model it replaces, otherwise every drive switch would leave another dead model
    /// wired to the player's <c>StateChanged</c>.
    /// </summary>
    protected virtual void Dispose(bool disposing)
    {
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    #endregion
}
