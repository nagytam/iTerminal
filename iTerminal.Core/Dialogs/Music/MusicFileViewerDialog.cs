using iConsole.Buffers;
using iConsole.Controls;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Model;
using iConsole.Services;
using iTerminal.Core.Data.Music;
using iTerminal.Core.Services.Music;

namespace iTerminal.Core.Dialogs.Music;

/// <summary>
/// F3 on an audio file: what the file says about itself (artist, title, album, every tag it carries
/// and the shape of the audio) with a transport for playing it without leaving the viewer.
///
/// <para>Playback goes through the one <see cref="IMusicPlayerService"/> singleton rather than a
/// private decoder, so viewing a track cannot start a second stream over whatever the playlist drive
/// is already playing, and the viewer's controls and the music panels always agree about what is
/// playing. Starting a file that is not queued yet appends it to the queue — the same thing choosing
/// it in a panel would do — and closing the viewer leaves it playing, exactly as closing a panel
/// does.</para>
///
/// <para>Reading tags touches the disk, so it runs on a background task and the dialog draws
/// "Reading tags…" until it lands; the key handler never waits for it.</para>
/// </summary>
public class MusicFileViewerDialog : Dialog
{
    private static readonly TimeSpan SeekStep = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan LongSeekStep = TimeSpan.FromSeconds(30);

    private const int VolumeStep = 5;

    /// <summary>Rows above the tag list: path, artist, title, album, format, and the section band.</summary>
    private const int HeaderRows = 6;

    /// <summary>Rows below the tag list: the section band, the transport line, the levels line and the hints.</summary>
    private const int PlayerRows = 4;

    private readonly IMusicPlayerService _player;
    private readonly IAudioDeviceService _deviceService;
    private readonly IMusicMetadataService _metadataService;
    private readonly IBeepService _beepService;
    private readonly IDateTimeProviderService _dateTimeProviderService;

    private readonly ConcurrentObservableCollection<string> _tagRows = new();
    private readonly ListBox<string> _tagListBox;

    private MusicFileMetadata _metadata = MusicFileMetadata.Empty;
    private CancellationTokenSource? _loadCts;
    private Task _loadTask = Task.CompletedTask;
    private bool _isLoading;
    private string _status = string.Empty;

    /// <summary>Absolute path of the file to view. Set by the caller before the dialog is shown.</summary>
    public string FilePath { get; set; } = string.Empty;

    public MusicFileViewerDialog(
        IMusicPlayerService player,
        IAudioDeviceService deviceService,
        IMusicMetadataService metadataService,
        IBeepService beepService,
        IDateTimeProviderService dateTimeProviderService,
        IThemeService themeService)
        : base(nameof(MusicFileViewerDialog), themeService)
    {
        _player = player;
        _deviceService = deviceService;
        _metadataService = metadataService;
        _beepService = beepService;
        _dateTimeProviderService = dateTimeProviderService;
        Title = "Music";

        _tagListBox = new ListBox<string>(
            new ListBoxModel<string>(_tagRows) { TransformItem = TransformTagRow },
            themeService);
        AddControl(_tagListBox);
    }

    /// <summary>The metadata currently on screen. Exposed for tests and for nothing else.</summary>
    public MusicFileMetadata Metadata => _metadata;

    /// <summary>True while the background read is still running.</summary>
    public bool IsLoading => _isLoading;

    /// <summary>The in-flight tag read, so a test can await it instead of polling. Never awaited by the UI.</summary>
    public Task LoadTask => _loadTask;

    private static ListItem<string> TransformTagRow(string row, CancellationToken cancellationToken) =>
        new(row) { Value = row, IsEnabled = true };

    #region Loading

    public override async Task OnInitializeAsync()
    {
        await base.OnInitializeAsync();

        _status = string.Empty;
        _metadata = MusicFileMetadata.Empty with { Path = FilePath };
        _tagRows.ReplaceAll([]);
        _tagListBox.UpdateModel();

        StartLoading();
        StateHasChanged();
    }

    /// <summary>
    /// E: edits the file's tags in the tag editor. Only MP3 and FLAC files can be written; the tags are shown again once a
    /// save has gone through.
    /// </summary>
    private async Task EditTagsAsync()
    {
        string path = FilePath;
        if (!AudioTagWriter.CanWrite(path))
        {
            _status = "Tags can be written to MP3 and FLAC files.";
            StateHasChanged();
            return;
        }

        MusicTagSet current = await Task.Run(() => MusicTagReader.ReadAll(path));
        var editor = new MusicTagEditorDialog(path, current, _beepService, _dateTimeProviderService, ThemeService, saved: () =>
        {
            if (FilePath == path)
            {
                StartLoading();
            }
        });
        await ShowDialogAsync(editor);
    }

    /// <summary>
    /// Reads the tags off the UI thread. The result is dropped when <see cref="FilePath"/> moved on
    /// in the meantime — the dialog instance is reused for every file, so a slow read of one file
    /// must not repaint the viewer of the next.
    /// </summary>
    private void StartLoading()
    {
        var path = FilePath;

        _loadCts?.Cancel();
        var cts = new CancellationTokenSource();
        _loadCts = cts;
        _isLoading = true;

        _loadTask = Task.Run(async () =>
        {
            MusicFileMetadata metadata;
            try
            {
                metadata = await _metadataService.ReadAsync(path, cts.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception)
            {
                metadata = MusicFileMetadata.Empty with { Path = path, Error = exception.Message };
            }

            if (cts.IsCancellationRequested || !string.Equals(path, FilePath, StringComparison.Ordinal))
            {
                return;
            }

            _metadata = metadata;
            _isLoading = false;
            RebuildTagRows();
            StateHasChanged();
        }, cts.Token);
    }

    /// <summary>
    /// Lays the tags out as an aligned two-column list. The name column is sized to the longest name
    /// but capped, so one long user-defined field cannot push every value off the right edge.
    /// </summary>
    private void RebuildTagRows()
    {
        var tags = _metadata.TagSet.Tags;
        if (tags.Count == 0)
        {
            _tagRows.ReplaceAll([
                string.IsNullOrEmpty(_metadata.Error)
                    ? "  This file carries no tags."
                    : $"  {_metadata.Error}",
            ]);
            _tagListBox.UpdateModel();
            return;
        }

        var nameWidth = Math.Min(20, tags.Max(tag => tag.Name.Length));
        _tagRows.ReplaceAll([.. tags.Select(tag => $"  {tag.Name.PadRight(nameWidth)}  {tag.Value}")]);
        _tagListBox.UpdateModel();
    }

    #endregion

    #region Playback

    /// <summary>True when the player is loaded with this very file, which is what makes the transport apply to it.</summary>
    private bool IsThisFileCued =>
        !string.IsNullOrEmpty(FilePath)
        && string.Equals(_player.CurrentTrack?.Path, FilePath, StringComparison.OrdinalIgnoreCase);

    /// <summary>Named apart from <see cref="iConsole.Controls.Control.Position"/>, which is the control's own coordinates.</summary>
    private TimeSpan PlaybackPosition => IsThisFileCued ? _player.Position : TimeSpan.Zero;

    private TimeSpan TrackDuration =>
        IsThisFileCued && _player.Duration > TimeSpan.Zero ? _player.Duration : _metadata.Duration;

    private PlaybackState CurrentState => IsThisFileCued ? _player.State : PlaybackState.Stopped;

    /// <summary>Plays this file, queueing it first when it is not in the playlist yet.</summary>
    private void Play()
    {
        if (string.IsNullOrEmpty(FilePath))
        {
            return;
        }

        // Queueing before the read lands would queue a track with no title and an unknown length.
        if (_isLoading)
        {
            _status = "Still reading the file…";
            return;
        }

        if (!_metadata.CanPlay)
        {
            _status = _metadataService.IsMusicFile(FilePath)
                ? $"Playing {_metadata.Format} files needs ffmpeg on PATH."
                : "This file is not an audio format the player knows.";
            return;
        }

        var index = IndexInPlaylist();
        if (index < 0)
        {
            _player.Playlist.Add(_metadata.ToTrack());
            index = IndexInPlaylist();
            _status = "Added to the playlist";
        }

        if (index < 0)
        {
            _status = "The file could not be queued.";
            return;
        }

        _player.PlayIndex(index);
    }

    private int IndexInPlaylist()
    {
        IReadOnlyList<MusicTrack> tracks = _player.Playlist.Tracks;
        for (var index = 0; index < tracks.Count; index++)
        {
            if (string.Equals(tracks[index].Path, FilePath, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    private void TogglePlayPause()
    {
        if (IsThisFileCued)
        {
            _player.TogglePlayPause();
            return;
        }

        Play();
    }

    private void Seek(TimeSpan offset)
    {
        if (!IsThisFileCued)
        {
            // Said rather than ignored: the arrows seek only the track this viewer is playing.
            _status = "Seeking needs this file playing — Space plays it.";
            return;
        }

        _player.SeekRelative(offset);
    }

    private void ChangeVolume(int delta)
    {
        _player.Volume += delta;
        _status = $"Volume {_player.Volume}%";
    }

    #endregion

    #region Input

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        yield return ("Space", "Play or pause");
        yield return ("Enter", "Play from the start");
        yield return ("Left/Right", "Seek back or forward (Shift: a long step)");
        yield return ("+/-", "Raise or lower the volume");
        yield return ("S", "Stop this file");
        yield return ("E", "Edit the tags");
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        if (keyEvent.KeyPressType != KeyPressType.Down || keyEvent.Control || keyEvent.Alt)
        {
            return await base.HandleKeyEventAsync(keyEvent);
        }

        if (keyEvent.KeyChar is 'e' or 'E')
        {
            await EditTagsAsync();
            return true;
        }

        switch (keyEvent.ConsoleKey)
        {
            case ConsoleKey.Escape:
                Close();
                return true;

            case ConsoleKey.Spacebar:
                TogglePlayPause();
                StateHasChanged();
                return true;

            case ConsoleKey.Enter:
                Play();
                StateHasChanged();
                return true;

            case ConsoleKey.LeftArrow:
                Seek(keyEvent.Shift ? -LongSeekStep : -SeekStep);
                StateHasChanged();
                return true;

            case ConsoleKey.RightArrow:
                Seek(keyEvent.Shift ? LongSeekStep : SeekStep);
                StateHasChanged();
                return true;

            case ConsoleKey.Add or ConsoleKey.OemPlus:
                ChangeVolume(VolumeStep);
                StateHasChanged();
                return true;

            case ConsoleKey.Subtract or ConsoleKey.OemMinus:
                ChangeVolume(-VolumeStep);
                StateHasChanged();
                return true;
        }

        switch (keyEvent.KeyChar)
        {
            case '+':
                ChangeVolume(VolumeStep);
                StateHasChanged();
                return true;

            case '-':
                ChangeVolume(-VolumeStep);
                StateHasChanged();
                return true;

            case 's' or 'S':
                // Only this file's playback. The player is shared with the music panels, and a
                // viewer opened over some other file used to stop whatever they were playing.
                if (IsThisFileCued)
                {
                    _player.Stop();
                }
                else
                {
                    _status = "This file is not playing.";
                }

                StateHasChanged();
                return true;
        }

        // Everything else — the arrows the tag list scrolls with, Tab — belongs to the children.
        return await base.HandleKeyEventAsync(keyEvent);
    }

    #endregion

    #region Layout

    public override void OnInitializeLayout()
    {
        if (Parent is not null)
        {
            CenterInParent(Parent.ViewSize * 0.9);
        }

        var iw = (int)ViewSize.Width - 2;
        var ih = (int)ViewSize.Height - 2;
        var listHeight = ih - HeaderRows - PlayerRows;

        _tagListBox.Position = new Position(1, 1 + HeaderRows);
        _tagListBox.ViewSize = iw > 0 && listHeight > 0
            ? new Size(iw, listHeight)
            : new Size(0, 0);

        // Children must be sized before base recurses into them: a ListBox sizes its scrollable
        // model from its own ViewSize in OnInitializeLayout.
        base.OnInitializeLayout();
    }

    #endregion

    #region Draw

    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);

        if (ViewSize.IsEmpty)
        {
            return;
        }

        ColorScheme colorScheme = ThemeService.CurrentColorScheme;
        var ix = (int)AbsolutePosition.X + 1;
        var iy = (int)AbsolutePosition.Y + 1;
        var iw = (int)ViewSize.Width - 2;
        var ih = (int)ViewSize.Height - 2;
        if (iw <= 0 || ih <= HeaderRows + PlayerRows)
        {
            return;
        }

        DrawMetadata(buffer, ix, iy, iw, colorScheme);
        DrawPlayer(buffer, ix, iy + ih - PlayerRows, iw, colorScheme);
    }

    private void DrawMetadata(IBuffer buffer, int x, int y, int w, ColorScheme colorScheme)
    {
        var size = _metadata.Size > 0 ? $"   {FormatSize(_metadata.Size)}" : string.Empty;
        WriteLine(buffer, x, y, w, $" {FilePath}{size}", Color.Cyan, colorScheme.Background);

        WriteField(buffer, x, y + 1, w, "Artist", string.IsNullOrEmpty(_metadata.Artist) ? "—" : _metadata.Artist, colorScheme);
        WriteField(buffer, x, y + 2, w, "Title", _isLoading ? "Reading tags…" : _metadata.Title, colorScheme);
        WriteField(buffer, x, y + 3, w, "Album", string.IsNullOrEmpty(_metadata.Album) ? "—" : _metadata.Album, colorScheme);

        var format = _metadata.FormatSummary;
        var duration = _metadata.Duration > TimeSpan.Zero ? $"   {FormatTime(_metadata.Duration)}" : string.Empty;
        var playable = _metadata.CanPlay || string.IsNullOrEmpty(_metadata.Format)
            ? string.Empty
            : "   (needs ffmpeg)";
        WriteField(buffer, x, y + 4, w, "Format", $"{format}{duration}{playable}", colorScheme);

        var source = string.IsNullOrEmpty(_metadata.TagSet.Source)
            ? "Tags"
            : $"Tags ({_metadata.TagSet.Source})";
        DrawBand(buffer, x, y + 5, w, source, colorScheme);
    }

    private void DrawPlayer(IBuffer buffer, int x, int y, int w, ColorScheme colorScheme)
    {
        DrawBand(buffer, x, y, w, "Player", colorScheme);

        var times = $"{FormatTime(PlaybackPosition)} / {FormatTime(TrackDuration)}";
        var transport = $" {StateGlyph(CurrentState)}  {times}  ";
        var barWidth = w - transport.Length - 1;
        if (barWidth >= 8)
        {
            var duration = TrackDuration;
            var fraction = duration > TimeSpan.Zero
                ? Math.Clamp(PlaybackPosition.TotalSeconds / duration.TotalSeconds, 0, 1)
                : 0;
            var filled = (int)(barWidth * fraction);
            transport += StringHelper.Repeat('█', filled) + StringHelper.Repeat('░', barWidth - filled);
        }

        WriteLine(
            buffer,
            x,
            y + 1,
            w,
            transport,
            CurrentState == PlaybackState.Playing ? Color.Green : colorScheme.Foreground,
            colorScheme.Background);

        var levels = $" Volume {_player.Volume,3}% {VolumeBar(_player.Volume)}   Output: {CurrentDeviceName()}";
        var foreground = colorScheme.Foreground;
        var note = StatusNote(out var isError);
        if (!string.IsNullOrEmpty(note))
        {
            levels += $"   {note}";
            foreground = isError ? colorScheme.Error : foreground;
        }

        WriteLine(buffer, x, y + 2, w, levels, foreground, colorScheme.Background);

        WriteLine(
            buffer,
            x,
            y + 3,
            w,
            KeyHints,
            colorScheme.Foreground,
            colorScheme.Background);
    }

    internal const string KeyHints =
        " Space:play/pause Enter:from start s:stop ←/→:seek Shift+←/→:seek further +/-:volume e:tags Esc:close";

    internal string StatusForTest => _status;

    /// <summary>The engine's own trouble outranks the viewer's chatter — a missing codec matters more than "Volume 75%".</summary>
    private string StatusNote(out bool isError)
    {
        if (!string.IsNullOrEmpty(_player.StatusMessage))
        {
            isError = _player.StatusIsError;
            return _player.StatusMessage;
        }

        isError = false;
        return _status;
    }

    private string CurrentDeviceName()
    {
        var currentId = _player.DeviceId ?? string.Empty;
        AudioDevice? device = _deviceService.GetDevices()
            .FirstOrDefault(d => string.Equals(d.Id, currentId, StringComparison.Ordinal));
        return device?.Name ?? "Default output";
    }

    private void WriteField(IBuffer buffer, int x, int y, int w, string label, string value, ColorScheme colorScheme)
    {
        WriteLine(buffer, x, y, w, $" {label,-8}{value}", colorScheme.Foreground, colorScheme.Background);
    }

    private void DrawBand(IBuffer buffer, int x, int y, int w, string title, ColorScheme colorScheme)
    {
        WriteLine(buffer, x, y, w, " " + title, colorScheme.Foreground, colorScheme.ButtonBackground);
    }

    /// <summary>Writes a single padded/truncated line of styled text.</summary>
    private void WriteLine(IBuffer buffer, int x, int y, int w, string text, Color foreground, Color background)
    {
        if (w <= 0)
        {
            return;
        }

        text ??= string.Empty;
        text = text.Length > w ? text[..w] : text.PadRight(w);
        buffer.Write(ZOrder, new Position(x, y), new Size(w, 1), new ConxelString(text, foreground, background));
    }

    private static string StateGlyph(PlaybackState state) => state switch
    {
        PlaybackState.Playing => "▶",
        PlaybackState.Paused => "‖",
        _ => "■",
    };

    private static string VolumeBar(int volume)
    {
        const int width = 10;
        var filled = (int)Math.Round(width * (Math.Clamp(volume, 0, 100) / 100.0));
        return StringHelper.Repeat('█', filled) + StringHelper.Repeat('░', width - filled);
    }

    private static string FormatTime(TimeSpan value)
    {
        if (value <= TimeSpan.Zero)
        {
            return "--:--";
        }

        return value.TotalHours >= 1
            ? $"{(int)value.TotalHours}:{value.Minutes:00}:{value.Seconds:00}"
            : $"{value.Minutes}:{value.Seconds:00}";
    }

    private static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double size = bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return unit == 0 ? $"{bytes} B" : $"{size:0.#} {units[unit]}";
    }

    #endregion
}
