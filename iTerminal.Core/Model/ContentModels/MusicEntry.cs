using iConsole.Data;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Interfaces;
using iTerminal.Core.Data.Music;
using System.Diagnostics;

namespace iTerminal.Core.Model.ContentModels;

/// <summary>What a row in a music panel stands for. Decides what Enter does to it.</summary>
public enum MusicEntryKind
{
    /// <summary>A navigable location: a control folder, an artist, an album, or the <c>..</c> row.</summary>
    Folder,

    /// <summary>A playable file: a playlist entry, or a track in the catalog.</summary>
    Track,

    /// <summary>An audio output endpoint in the device folder.</summary>
    Device,

    /// <summary>A row that performs a transport, volume or library action when activated.</summary>
    Action,
}

/// <summary>
/// One-shot commands a row can carry. Everything the old player dialog bound to a key is here as a
/// row instead, so the panels are usable with nothing but the cursor and Enter.
/// </summary>
public enum MusicAction
{
    None,
    PlayPause,
    Stop,
    Next,
    Previous,
    SeekBackward,
    SeekForward,
    CycleRepeat,
    ToggleShuffle,
    ClearPlaylist,
    SetVolume,
    VolumeUp,
    VolumeDown,
    RescanDevices,
}

/// <summary>
/// A row in either music panel. The four columns a file panel draws are reused rather than fought:
/// the size column carries the value that matters for the row (a duration, a volume percentage, a
/// repeat mode) and the date column carries its context (an album, a backend name), so a music
/// drive looks like every other drive without a special renderer.
/// </summary>
[DebuggerDisplay("{Kind}: {DisplayName}")]
public record MusicEntry : EntryBase, IEntry
{
    private const int AlbumColumnWidth = 10;
    private const int TimeColumnWidth = 5;

    public MusicEntry(MusicEntryKind kind, Url url, string displayName)
    {
        Kind = kind;
        Url = new Url(url, displayName);
        date = string.Empty.PadRight(AlbumColumnWidth);
        time = string.Empty.PadRight(TimeColumnWidth);

        // A folder row with no count would make ItemInformation schedule a recursive file count
        // against a path that does not exist on disk. Every music folder already knows its size.
        if (kind == MusicEntryKind.Folder)
        {
            NumberOfFiles = string.Empty;
        }
    }

    public MusicEntryKind Kind { get; }

    /// <summary>The track behind a <see cref="MusicEntryKind.Track"/> row.</summary>
    public MusicTrack? Track { get; init; }

    /// <summary>Position in the playlist, or -1 for rows that are not playlist entries.</summary>
    public int PlaylistIndex { get; init; } = -1;

    /// <summary>True when this row is the track the player currently has loaded.</summary>
    public bool IsCurrent { get; init; }

    /// <summary>
    /// True when the row is marked for the next operation. The mark itself lives in the model, not
    /// here: these rows are rebuilt from scratch on every refresh — several times a second while the
    /// library is indexing — so anything stored on them would not survive a repaint.
    /// </summary>
    public bool IsSelected { get; init; }

    /// <summary>Backend-specific device id for a <see cref="MusicEntryKind.Device"/> row.</summary>
    public string? DeviceId { get; init; }

    /// <summary>What an <see cref="MusicEntryKind.Action"/> row does when activated.</summary>
    public MusicAction Action { get; init; }

    /// <summary>Argument of <see cref="MusicAction.SetVolume"/>, 0–100.</summary>
    public int VolumeLevel { get; init; } = -1;

    /// <summary>
    /// The library hit an artist or album folder stands for, so adding it to the playlist can go
    /// through <see cref="Services.Music.IMusicLibraryService.ExpandResult"/> instead of re-querying.
    /// </summary>
    public MusicSearchResult? SearchResult { get; init; }

    /// <summary>These rows are always real; nothing here is a stale path from a cache.</summary>
    public override bool Exists => true;

    /// <summary>Right-hand column: the duration, percentage or mode that describes this row.</summary>
    public MusicEntry WithValue(string value)
    {
        FileSize = value ?? string.Empty;
        return this;
    }

    /// <summary>Context column: an album, a backend name, "[default]" — whatever qualifies the row.</summary>
    public MusicEntry WithContext(string context)
    {
        Date = (context ?? string.Empty).Shrink(AlbumColumnWidth)?.PadRight(AlbumColumnWidth)
               ?? string.Empty.PadRight(AlbumColumnWidth);
        return this;
    }

    /// <summary>
    /// The playing track is tinted so it stands out in a long queue, the same cue the old dialog
    /// gave, and a marked row is tinted the colour every file manager marks with. Colouring happens
    /// on top of the standard row rather than instead of it, so the cursor, the filter highlight and
    /// the lenses all keep working.
    ///
    /// <para>A mark wins over "this is playing": the mark is what the user is in the middle of doing,
    /// and the playing row is already named in the panel header.</para>
    /// </summary>
    public override ConxelString FormatItemName(int width, bool cursor, ColorScheme colorScheme, VisualState visualState)
    {
        return ColorPlaybackState(base.FormatItemName(width, cursor, colorScheme, visualState), cursor);
    }

    /// <summary>The Long row gets the same selected / playing colour as the full row.</summary>
    public override ConxelString FormatLongItemName(int width, bool cursor, ColorScheme colorScheme, VisualState visualState)
    {
        return ColorPlaybackState(base.FormatLongItemName(width, cursor, colorScheme, visualState), cursor);
    }

    /// <summary>The Brief cell gets the same selected / playing colour as the full row.</summary>
    public override ConxelString FormatBriefItemName(int width, bool cursor, ColorScheme colorScheme, VisualState visualState)
    {
        return ColorPlaybackState(base.FormatBriefItemName(width, cursor, colorScheme, visualState), cursor);
    }

    private ConxelString ColorPlaybackState(ConxelString conxelString, bool cursor)
    {
        if (cursor || !(IsSelected || IsCurrent))
        {
            return conxelString;
        }

        var color = IsSelected ? Color.Yellow : Color.Green;
        for (var i = 0; i < conxelString.Count; i++)
        {
            var cell = conxelString.String[i];
            cell.ForegroundColor = color;
            conxelString.String[i] = cell;
        }

        return conxelString;
    }
}
