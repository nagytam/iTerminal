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
/// The playing queue as a panel drive. The root lists the tracks, preceded by three control folders
/// — transport, volume and output — so everything the player dialog used to bind to a key is
/// reachable with the cursor and Enter, which is what a file panel gives you for free.
///
/// <para>The model is a view over <see cref="IMusicPlayerService"/>, never an owner of it: the
/// player is a singleton with its own pump thread, so navigating the panel away from
/// <c>musicplaylist://</c> does not stop the music any more than closing the old dialog did.</para>
/// </summary>
public sealed class ContentModelMusicPlaylist : ContentModelMusicBase
{
    private const string TransportSection = "transport";
    private const string VolumeSection = "volume";
    private const string DevicesSection = "devices";

    private static readonly int[] VolumePresets = [0, 25, 50, 75, 100];

    private readonly IAudioDeviceService _deviceService;

    /// <summary>
    /// Last rendered snapshot of everything that changes a row: the playlist's own version stamp,
    /// which track is cued, and whether it is playing. The pump moves all three from its own thread,
    /// and rebuilding rows only when one of them actually moved keeps a progressing track from
    /// rebuilding the list many times a second.
    /// </summary>
    private (int Version, int CurrentIndex, PlaybackState State) _renderedState = (-1, -1, PlaybackState.Stopped);

    public ContentModelMusicPlaylist(
        string name,
        ISettingsService? settingsService,
        IMusicPlayerService player,
        IAudioDeviceService deviceService,
        FolderHistoryService? folderHistoryService,
        IFrequentExecutionGuardService frequentExecutionGuardService,
        IMusicSessionRestoreService? sessionRestoreService = null)
        : base(name, settingsService, player, folderHistoryService, frequentExecutionGuardService)
    {
        _deviceService = deviceService;

        // Before the first listing is built, and before the session job has necessarily started: a
        // panel restored from the last session is created while the app is still coming up, and
        // waiting for a background job to push the queue in is what made it draw an empty list first
        // and the saved queue a moment later. Restoring here is idempotent — whichever of the two
        // gets there first does it, the other finds it done.
        sessionRestoreService?.RestoreOnce();

        Player.StateChanged += OnPlayerStateChanged;
    }

    protected override string UrlScheme => Container.MUSIC_PLAYLIST;

    /// <summary>True while the listing is narrowed to the tracks matching <see cref="Filter"/>.</summary>
    public bool IsFiltering => Filter is not null && Section.Length == 0;

    /// <summary>
    /// Typing narrows the queue to the tracks that match, rather than tinting the ones that do not.
    /// A queue is a list to find something in — a few hundred rows of an evening's music — and the
    /// row wanted is as likely to be off-screen as on it, which is precisely what dimming cannot
    /// help with. The catalog's search works the same way for the same reason.
    ///
    /// <para>Only the track rows are filtered. The three control folders are how the panel is
    /// operated at all, so they stay put while a filter is on.</para>
    /// </summary>
    public override string? Filter
    {
        get => base.Filter;
        set
        {
            var normalized = string.IsNullOrEmpty(value) ? null : value;
            if (base.Filter == normalized)
            {
                return;
            }

            base.Filter = normalized;
            Refresh();
        }
    }

    /// <summary>Everything listed is a hit, so nothing is dimmed and the cursor lands on row 0.</summary>
    public override bool IsEntryMatch(IEntry? entry) => true;

    private string Section
    {
        get
        {
            var segments = PathSegments();
            return segments.Length == 0 ? string.Empty : segments[0].ToLowerInvariant();
        }
    }

    /// <summary>
    /// Repaints on a transport change, and rebuilds the rows only when the list or the cursor moved.
    /// Runs on the pump thread; <see cref="ContentModelBase.Update"/> is throttled, and
    /// <c>Entries</c> is a concurrent collection, so both are safe from here.
    /// </summary>
    private void OnPlayerStateChanged(object? sender, EventArgs e)
    {
        if (HasRowStateMoved())
        {
            Refresh();
        }
        else
        {
            Update();
        }
    }

    private bool HasRowStateMoved()
    {
        var state = (Player.Playlist.Version, Player.Playlist.CurrentIndex, Player.State);
        if (state == _renderedState)
        {
            return false;
        }

        _renderedState = state;
        return true;
    }

    public override void Refresh(bool forceRefresh = false)
    {
        _renderedState = (Player.Playlist.Version, Player.Playlist.CurrentIndex, Player.State);

        if (forceRefresh && Section == DevicesSection)
        {
            // Ctrl+R in the output folder means "look for devices again", which is the one refresh
            // on this drive that costs a native probe.
            _deviceService.Refresh();
        }

        List<IEntry> entries = [];
        switch (Section)
        {
            case TransportSection:
                BuildTransportEntries(entries);
                break;
            case VolumeSection:
                BuildVolumeEntries(entries);
                break;
            case DevicesSection:
                BuildDeviceEntries(entries);
                break;
            default:
                BuildRootEntries(entries);
                break;
        }

        Entries.ReplaceAll(entries);
        Update();
    }

    private void BuildRootEntries(List<IEntry> entries)
    {
        entries.Add(new MusicEntry(MusicEntryKind.Folder, ChildUrl(TransportSection), "[Transport]")
            .WithValue(StateLabel(Player.State))
            .WithContext(Player.CurrentTrack?.DisplayTitle ?? string.Empty));

        entries.Add(new MusicEntry(MusicEntryKind.Folder, ChildUrl(VolumeSection), "[Volume]")
            .WithValue($"{Player.Volume}%")
            .WithContext(VolumeBar(Player.Volume)));

        entries.Add(new MusicEntry(MusicEntryKind.Folder, ChildUrl(DevicesSection), "[Output]")
            .WithValue(_deviceService.HasAudioOutput ? _deviceService.ActiveBackendName : "none")
            .WithContext(CurrentDeviceName()));

        var tracks = Player.Playlist.Tracks;
        var currentIndex = Player.Playlist.CurrentIndex;
        var tokens = FilterTokens();
        for (var index = 0; index < tracks.Count; index++)
        {
            var track = tracks[index];
            if (!Matches(track, tokens))
            {
                continue;
            }

            var isCurrent = index == currentIndex;
            var indicator = isCurrent ? StateGlyph(Player.State) : " ";

            entries.Add(new MusicEntry(
                MusicEntryKind.Track,
                new Url(UrlScheme + Uri.EscapeDataString(track.Path)),
                $"{indicator} {index + 1,3}. {track.DisplayName}")
            {
                Track = track,
                PlaylistIndex = index,
                IsCurrent = isCurrent,
            }
                .WithValue(FormatDuration(track.Duration))
                .WithContext(track.Album));
        }
    }

    /// <summary>
    /// The filter split into words, or empty when nothing is being filtered. Every word has to
    /// appear somewhere in the track for it to be listed, so "beat sgt" finds the same row as
    /// "sgt beat" — a queue is searched by half-remembered fragments, not by prefix.
    /// </summary>
    private string[] FilterTokens() =>
        string.IsNullOrWhiteSpace(Filter)
            ? []
            : Filter.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool Matches(MusicTrack track, string[] tokens)
    {
        if (tokens.Length == 0)
        {
            return true;
        }

        // The file name is in the haystack because an untagged track has nothing else to be found by.
        var haystack = $"{track.Artist} {track.Album} {track.Title} {System.IO.Path.GetFileNameWithoutExtension(track.Path)}";
        foreach (var token in tokens)
        {
            if (!haystack.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private void BuildTransportEntries(List<IEntry> entries)
    {
        AddParentEntry(entries);

        AddAction(entries, MusicAction.PlayPause, "Play / Pause", StateLabel(Player.State), Player.CurrentTrack?.DisplayName ?? string.Empty);
        AddAction(entries, MusicAction.Stop, "Stop", string.Empty, string.Empty);
        AddAction(entries, MusicAction.Next, "Next track", string.Empty, string.Empty);
        AddAction(entries, MusicAction.Previous, "Previous track", string.Empty, string.Empty);
        AddAction(entries, MusicAction.SeekBackward, "Rewind 5s", FormatDuration(Player.Position), string.Empty);
        AddAction(entries, MusicAction.SeekForward, "Forward 5s", FormatDuration(Player.Duration), string.Empty);
        AddAction(entries, MusicAction.CycleRepeat, "Repeat mode", RepeatLabel(Player.RepeatMode), string.Empty);
        AddAction(entries, MusicAction.ToggleShuffle, "Shuffle", Player.IsShuffled ? "on" : "off", string.Empty);
        AddAction(entries, MusicAction.ClearPlaylist, "Clear the playlist", $"{Player.Playlist.Count}", string.Empty);
    }

    private void BuildVolumeEntries(List<IEntry> entries)
    {
        AddParentEntry(entries);

        AddAction(entries, MusicAction.VolumeUp, "Louder (+5)", $"{Math.Min(100, Player.Volume + 5)}%", string.Empty);
        AddAction(entries, MusicAction.VolumeDown, "Quieter (-5)", $"{Math.Max(0, Player.Volume - 5)}%", string.Empty);

        foreach (var level in VolumePresets)
        {
            var isCurrent = level == Player.Volume;
            entries.Add(new MusicEntry(
                MusicEntryKind.Action,
                ActionUrl(MusicAction.SetVolume, level.ToString()),
                $"{(isCurrent ? "•" : " ")} {(level == 0 ? "Mute" : $"Set to {level}%")}")
            {
                Action = MusicAction.SetVolume,
                VolumeLevel = level,
                IsCurrent = isCurrent,
            }
                .WithValue($"{level}%")
                .WithContext(VolumeBar(level)));
        }
    }

    private void BuildDeviceEntries(List<IEntry> entries)
    {
        AddParentEntry(entries);

        AddAction(entries, MusicAction.RescanDevices, "[Rescan devices]", _deviceService.ActiveBackendName, string.Empty);

        var currentId = Player.DeviceId ?? string.Empty;
        foreach (var device in _deviceService.GetDevices())
        {
            var isCurrent = string.Equals(device.Id, currentId, StringComparison.Ordinal);
            entries.Add(new MusicEntry(
                MusicEntryKind.Device,
                new Url(UrlScheme + DevicesSection + "/" + Uri.EscapeDataString(device.Id)),
                $"{(isCurrent ? "•" : " ")} {device.Name}")
            {
                DeviceId = device.Id,
                IsCurrent = isCurrent,
            }
                .WithValue(device.Backend)
                .WithContext(device.IsDefault ? "[default]" : string.Empty));
        }

        if (!_deviceService.HasAudioOutput)
        {
            entries.Add(new MusicEntry(
                MusicEntryKind.Action,
                ActionUrl(MusicAction.None, "no-output"),
                "  No audio backend is available on this machine")
            {
                Action = MusicAction.None,
            });
        }
    }

    private void AddAction(List<IEntry> entries, MusicAction action, string label, string value, string context)
    {
        entries.Add(new MusicEntry(MusicEntryKind.Action, ActionUrl(action, action.ToString()), "  " + label)
        {
            Action = action,
        }
            .WithValue(value)
            .WithContext(context));
    }

    private Url ActionUrl(MusicAction action, string discriminator) =>
        new(UrlScheme + Section + "/" + Uri.EscapeDataString($"{action}-{discriminator}"));

    public override void Activate(IEntry entry)
    {
        if (entry is not MusicEntry musicEntry)
        {
            return;
        }

        switch (musicEntry.Kind)
        {
            case MusicEntryKind.Track when musicEntry.PlaylistIndex >= 0:
                Player.PlayIndex(musicEntry.PlaylistIndex);
                SetStatus($"Playing {musicEntry.Track?.DisplayName}");
                break;

            case MusicEntryKind.Device:
                Player.SetDevice(musicEntry.DeviceId);
                SetStatus($"Output: {musicEntry.DisplayName.Trim(' ', '•')}");
                Refresh();
                break;

            case MusicEntryKind.Action:
                RunAction(musicEntry);
                break;
        }
    }

    private void RunAction(MusicEntry entry)
    {
        switch (entry.Action)
        {
            case MusicAction.ClearPlaylist:
                Player.Stop();
                Player.Playlist.Clear();
                SetStatus("Playlist cleared");
                break;

            case MusicAction.SetVolume when entry.VolumeLevel >= 0:
                Player.Volume = entry.VolumeLevel;
                SetStatus($"Volume {Player.Volume}%");
                break;

            case MusicAction.RescanDevices:
                _deviceService.Refresh();
                SetStatus($"Devices rescanned ({_deviceService.ActiveBackendName})");
                break;

            default:
                Perform(entry.Action);
                break;
        }

        Refresh();
    }

    /// <summary>Removes the row under the cursor from the queue. Only meaningful on a track row.</summary>
    public bool RemoveFromPlaylist(IEntry? entry)
    {
        if (entry is not MusicEntry { Kind: MusicEntryKind.Track, PlaylistIndex: >= 0 } musicEntry
            || !Player.Playlist.RemoveAt(musicEntry.PlaylistIndex))
        {
            return false;
        }

        SetStatus($"Removed {musicEntry.Track?.DisplayName}");
        Refresh();
        return true;
    }

    /// <summary>Appends tracks, skipping ones already queued. Returns how many were actually added.</summary>
    public int AddTracks(IReadOnlyList<MusicTrack> tracks)
    {
        var added = Player.Playlist.AddRange(tracks);
        if (added > 0)
        {
            Refresh();
        }

        return added;
    }

    /// <summary>
    /// Now playing plus a progress bar, sized to whatever the border leaves. The bar is dropped
    /// before the title is, because a truncated title still says what is playing and a two-cell bar
    /// says nothing.
    /// </summary>
    public override string GetHeader(int width)
    {
        var header = IsFiltering
            ? $"Playlist \"{Filter}\" ({MatchCount()} of {Player.Playlist.Count})"
            : $"Playlist ({Player.Playlist.Count})";
        var track = Player.CurrentTrack;
        if (track is null)
        {
            // Just the count. With no track loaded there is nothing to say about playback, and the
            // absent glyph, times and progress bar already say it.
            return header.Shrink(width) ?? string.Empty;
        }

        var times = $"{FormatDuration(Player.Position)}/{FormatDuration(Player.Duration)}";
        header = $"{StateGlyph(Player.State)} {track.DisplayName} [{times}] {header}";

        var barWidth = width - header.Length - 3;
        if (barWidth >= 8)
        {
            var fraction = Player.Duration > TimeSpan.Zero
                ? Math.Clamp(Player.Position.TotalSeconds / Player.Duration.TotalSeconds, 0, 1)
                : 0;
            var filled = (int)(barWidth * fraction);
            header += " " + StringHelper.Repeat('█', filled) + StringHelper.Repeat('░', barWidth - filled);
        }

        return header.Shrink(width) ?? string.Empty;
    }

    /// <summary>How many queued tracks the filter lets through.</summary>
    private int MatchCount()
    {
        var tokens = FilterTokens();
        var matches = 0;
        foreach (var track in Player.Playlist.Tracks)
        {
            if (Matches(track, tokens))
            {
                matches++;
            }
        }

        return matches;
    }

    public override string GetFooter()
    {
        var footer = string.Join(
            Border.Horizontal,
            IsFiltering ? $"{MatchCount()} of {Player.Playlist.Count} tracks" : $"{Player.Playlist.Count} tracks",
            $"Vol {Player.Volume}%",
            $"Repeat {RepeatLabel(Player.RepeatMode)}",
            $"Shuffle {(Player.IsShuffled ? "on" : "off")}",
            CurrentDeviceName());

        // The key that starts a filter, and the one that ends it — a panel that narrows itself has
        // to say how to get everything back.
        footer += IsFiltering
            ? $"{Border.Horizontal}Esc clears the filter"
            : $"{Border.Horizontal}Alt+<text> filters the queue";

        // The engine's own trouble (a missing codec, a dead device) outranks the panel's chatter.
        if (!string.IsNullOrEmpty(Player.StatusMessage))
        {
            footer += $"{Border.Horizontal}{Player.StatusMessage}";
        }

        // WithStatus spells the filter out again, and the header and the counts have said it twice
        // already by this point.
        if (!IsFiltering)
        {
            return WithStatus(footer);
        }

        return string.IsNullOrEmpty(StatusMessage) ? footer : $"{footer}{Border.Horizontal}{StatusMessage}";
    }

    private string CurrentDeviceName()
    {
        var currentId = Player.DeviceId ?? string.Empty;
        var device = _deviceService.GetDevices().FirstOrDefault(d => string.Equals(d.Id, currentId, StringComparison.Ordinal));
        return device?.Name ?? "Default output";
    }

    private static string StateGlyph(PlaybackState state) => state switch
    {
        PlaybackState.Playing => "▶",
        PlaybackState.Paused => "‖",
        _ => "■",
    };

    private static string StateLabel(PlaybackState state) => state switch
    {
        PlaybackState.Playing => "playing",
        PlaybackState.Paused => "paused",
        _ => "stopped",
    };

    private static string RepeatLabel(RepeatMode mode) => mode switch
    {
        RepeatMode.All => "all",
        RepeatMode.One => "one",
        _ => "off",
    };

    private static string VolumeBar(int volume)
    {
        const int width = 8;
        var filled = (int)Math.Round(width * (Math.Clamp(volume, 0, 100) / 100.0));
        return StringHelper.Repeat('█', filled) + StringHelper.Repeat('░', width - filled);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Player.StateChanged -= OnPlayerStateChanged;
        }

        base.Dispose(disposing);
    }
}
