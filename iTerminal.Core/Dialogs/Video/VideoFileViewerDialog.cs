using iConsole.Buffers;
using iConsole.Controls;
using iConsole.Controls.Buttons;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Services;
using iTerminal.Core.Video;

namespace iTerminal.Core.Dialogs.Video;

/// <summary>
/// F3 on a video file: every frame is drawn as ASCII art, with a row of transport buttons under it.
///
/// <para>Decoding is ffmpeg's job and happens on the <see cref="VideoPlayer"/>'s own thread, which
/// scales each frame down to exactly the character grid the picture will be drawn on. The dialog only
/// converts the latest frame to characters and writes it: nothing in a key handler or in
/// <see cref="Draw"/> waits on a process. Opening the file (a probe that starts ffprobe) runs on a
/// background task, and the dialog says "Opening…" until it lands.</para>
///
/// <para>The picture is fitted to the room the dialog has, allowing for terminal cells being about
/// twice as tall as wide, and is re-made at the new size when the terminal is resized. The viewer is
/// silent: there is no audio path to keep a clock against.</para>
/// </summary>
public class VideoFileViewerDialog : Dialog
{
    private static readonly TimeSpan SeekStep = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan LongSeekStep = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ButtonSeekStep = TimeSpan.FromSeconds(10);

    /// <summary>Rows above the picture: the file and what is known about it.</summary>
    private const int HeaderRows = 1;

    /// <summary>Rows below the picture: the progress bar, the buttons and the key hints.</summary>
    private const int FooterRows = 3;

    private const int ButtonGap = 1;

    internal const string KeyHints =
        " Space:play/pause  ←/→:seek  Shift+←/→:seek further  Home:restart  s:stop  c:colour  Esc:close";

    private readonly IVideoDecoderFactory _decoderFactory;
    private readonly Button[] _buttons;

    private VideoPlayer? _player;
    private Task _openTask = Task.CompletedTask;
    private int _generation;
    private bool _isOpening;
    private string _openError = string.Empty;

    private AsciiColorMode _colorMode = AsciiColorMode.Mono;
    private AsciiFrame? _ascii;
    private VideoFrame? _asciiSource;
    private AsciiColorMode _asciiMode;

    // Where the progress bar was drawn, for turning a click into a position. Absolute coordinates.
    private int _barX;
    private int _barY;
    private int _barWidth;

    /// <summary>Absolute path of the file to view. Set by the caller before the dialog is shown.</summary>
    public string FilePath { get; set; } = string.Empty;

    public VideoFileViewerDialog(IVideoDecoderFactory decoderFactory, IThemeService themeService)
        : base(nameof(VideoFileViewerDialog), themeService)
    {
        _decoderFactory = decoderFactory;
        Title = "Video";

        _buttons =
        [
            CreateButton(() => "|< Start", "Back to the start and play", Restart),
            CreateButton(() => "<< 10s", "Back ten seconds", () => SeekBy(-ButtonSeekStep)),
            CreateButton(() => State == VideoPlaybackState.Playing ? "Pause" : "Play", "Play or pause", TogglePlayPause),
            CreateButton(() => "Stop", "Stop and go back to the first picture", Stop),
            CreateButton(() => "10s >>", "Forward ten seconds", () => SeekBy(ButtonSeekStep)),
            CreateButton(() => _colorMode == AsciiColorMode.Color ? "Colour: on" : "Colour: off", "Switch colour on or off", ToggleColor),
        ];
    }

    /// <summary>The player behind the dialog, once the file has opened. Exposed for tests and for nothing else.</summary>
    internal VideoPlayer? Player => _player;

    /// <summary>The in-flight open, so a test can await it instead of polling. Never awaited by the UI.</summary>
    public Task OpenTask => _openTask;

    public AsciiColorMode ColorMode => _colorMode;

    public VideoPlaybackState State => _player?.State ?? VideoPlaybackState.Stopped;

    public IReadOnlyList<Button> TransportButtons => _buttons;

    private Button CreateButton(Func<string> title, string description, Action action)
    {
        var button = new Button(new VideoActionCommand(title, description, () =>
        {
            action();
            StateHasChanged();
        }), ThemeService)
        {
            // Mouse only. The keys have their own bindings on the dialog, and a focusable button
            // would make Space mean "press whichever button has focus" instead of play/pause.
            CanFocus = false,
        };
        AddControl(button);
        return button;
    }

    #region Opening

    public override async Task OnInitializeAsync()
    {
        await base.OnInitializeAsync();

        DisposePlayer();
        _openError = string.Empty;
        _ascii = null;
        _asciiSource = null;
        Title = "Video — " + Path.GetFileName(FilePath);

        StartOpening();
        StateHasChanged();
    }

    /// <summary>
    /// Probes the file and starts the player off the UI thread. The result is dropped when the dialog
    /// was closed or reopened for another file meanwhile — the instance is reused for every file.
    /// </summary>
    private void StartOpening()
    {
        var path = FilePath;
        var generation = Interlocked.Increment(ref _generation);
        _isOpening = true;

        _openTask = Task.Run(() =>
        {
            VideoPlayer? player = null;
            try
            {
                if (!_decoderFactory.IsAvailable)
                {
                    throw new InvalidOperationException(FfmpegVideoDecoderFactory.MissingMessage);
                }

                player = new VideoPlayer(_decoderFactory.Create(path));

                var (columns, rows) = PictureSize(player.Info);
                lock (this)
                {
                    if (generation != Volatile.Read(ref _generation))
                    {
                        player.Dispose();
                        return;
                    }

                    _player = player;
                    _isOpening = false;
                }

                player.Start(columns, rows);
                player.Play();
            }
            catch (Exception exception)
            {
                player?.Dispose();
                if (generation == Volatile.Read(ref _generation))
                {
                    _openError = exception.Message;
                    _isOpening = false;
                }
            }

            StateHasChanged();
        });
    }

    private void DisposePlayer()
    {
        VideoPlayer? player;
        lock (this)
        {
            Interlocked.Increment(ref _generation);
            player = _player;
            _player = null;
            _isOpening = false;
        }

        player?.Dispose();
    }

    public override void Close()
    {
        DisposePlayer();
        base.Close();
    }

    #endregion

    #region Transport

    private void TogglePlayPause() => _player?.TogglePlayPause();

    private void Stop() => _player?.Stop();

    private void SeekBy(TimeSpan offset) => _player?.SeekBy(offset);

    private void Restart()
    {
        _player?.SeekTo(TimeSpan.Zero);
        _player?.Play();
    }

    private void ToggleColor() =>
        _colorMode = _colorMode == AsciiColorMode.Color ? AsciiColorMode.Mono : AsciiColorMode.Color;

    #endregion

    #region Input

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        yield return ("Space", "Play or pause");
        yield return ("Left/Right", "Seek back or forward (Shift: a long step)");
        yield return ("Home", "Back to the start");
        yield return ("S", "Stop");
        yield return ("C", "Switch colour on or off");
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        if (keyEvent.KeyPressType != KeyPressType.Down || keyEvent.Control || keyEvent.Alt)
        {
            return await base.HandleKeyEventAsync(keyEvent);
        }

        switch (keyEvent.ConsoleKey)
        {
            case ConsoleKey.Spacebar:
                TogglePlayPause();
                break;

            case ConsoleKey.LeftArrow:
                SeekBy(keyEvent.Shift ? -LongSeekStep : -SeekStep);
                break;

            case ConsoleKey.RightArrow:
                SeekBy(keyEvent.Shift ? LongSeekStep : SeekStep);
                break;

            case ConsoleKey.Home:
                Restart();
                break;

            default:
                switch (keyEvent.KeyChar)
                {
                    case 's' or 'S':
                        Stop();
                        break;

                    case 'c' or 'C':
                        ToggleColor();
                        break;

                    default:
                        return await base.HandleKeyEventAsync(keyEvent);
                }

                break;
        }

        StateHasChanged();
        return true;
    }

    /// <summary>A click on the progress bar jumps to that point of the video.</summary>
    public override async Task<bool> HandleMouseEventAsync(MouseEvent mouseEvent)
    {
        if (mouseEvent.LeftClicked
            && mouseEvent.WheelDelta == 0
            && _barWidth > 0
            && mouseEvent.Position.Y == _barY
            && mouseEvent.Position.X >= _barX
            && mouseEvent.Position.X < _barX + _barWidth
            && _player is { } player
            && player.Duration > TimeSpan.Zero)
        {
            var fraction = (mouseEvent.Position.X - _barX + 0.5) / _barWidth;
            player.SeekTo(player.Duration * Math.Clamp(fraction, 0, 1));
            StateHasChanged();
            return true;
        }

        return await base.HandleMouseEventAsync(mouseEvent);
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
        var rowY = 1 + ih - FooterRows + 1;

        var total = _buttons.Sum(button => ButtonWidth(button)) + ButtonGap * (_buttons.Length - 1);
        var x = 1 + Math.Max(0, (iw - total) / 2);
        foreach (var button in _buttons)
        {
            var width = ButtonWidth(button);
            button.Position = new Position(x, rowY);
            button.ViewSize = iw >= total && ih > FooterRows + HeaderRows
                ? new Size(width, 1)
                : new Size(0, 0);
            x += width + ButtonGap;
        }

        base.OnInitializeLayout();
    }

    /// <summary>Wide enough for the longest title a button ever shows, so the row does not shift as "Play" becomes "Pause".</summary>
    private static int ButtonWidth(Button button) => button.Title switch
    {
        "Play" or "Pause" => 9,
        "Colour: on" or "Colour: off" => 14,
        var title => title.Length + 4,
    };

    /// <summary>The room for the picture inside the dialog: all of its width, between the header and the footer.</summary>
    private (int Columns, int Rows) Area()
    {
        var iw = (int)ViewSize.Width - 2;
        var ih = (int)ViewSize.Height - 2;
        return (Math.Max(0, iw), Math.Max(0, ih - HeaderRows - FooterRows));
    }

    private (int Columns, int Rows) PictureSize(VideoInfo info)
    {
        var (areaColumns, areaRows) = Area();
        if (areaColumns <= 0 || areaRows <= 0)
        {
            // Not laid out yet. Draw corrects the size on its first pass.
            (areaColumns, areaRows) = (80, 24);
        }

        return AsciiArt.Fit(info.AspectRatio, areaColumns, areaRows);
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
        if (iw <= 0 || ih <= HeaderRows + FooterRows)
        {
            return;
        }

        var player = _player;
        var (areaColumns, areaRows) = Area();

        if (player is not null)
        {
            var (columns, rows) = AsciiArt.Fit(player.Info.AspectRatio, areaColumns, areaRows);
            player.Resize(columns, rows);
        }

        DrawInfo(buffer, ix, iy, iw, player, colorScheme);
        DrawPicture(buffer, ix, iy + HeaderRows, areaColumns, areaRows, player);
        DrawProgress(buffer, ix, iy + ih - FooterRows, iw, player, colorScheme);
        DrawHints(buffer, ix, iy + ih - 1, iw, colorScheme);
    }

    private void DrawInfo(IBuffer buffer, int x, int y, int w, VideoPlayer? player, ColorScheme colorScheme)
    {
        var text = $" {FilePath}";
        if (player is not null)
        {
            var info = player.Info;
            text += $"   {info.Width}×{info.Height}  {info.FrameRate:0.##} fps";
            if (info.Duration > TimeSpan.Zero)
            {
                text += $"  {FormatTime(info.Duration)}";
            }

            if (player.Frame is { } frame)
            {
                text += $"   shown as {frame.Columns}×{frame.Rows} characters";
            }
        }

        WriteLine(buffer, x, y, w, text, Color.Cyan, colorScheme.Background);
    }

    private void DrawPicture(IBuffer buffer, int x, int y, int width, int height, VideoPlayer? player)
    {
        var picture = player?.Frame;
        var message = PictureMessage(player);

        AsciiFrame? ascii = null;
        if (picture is not null)
        {
            ascii = ConvertToAscii(picture);
        }

        var left = ascii is null ? 0 : Math.Max(0, (width - ascii.Columns) / 2);
        var top = ascii is null ? 0 : Math.Max(0, (height - ascii.Rows) / 2);
        var blank = new Conxel(' ', Color.White, Color.Black);

        for (var row = 0; row < height; row++)
        {
            var cells = new Conxel[width];
            Array.Fill(cells, blank);

            var asciiRow = row - top;
            if (ascii is not null && asciiRow >= 0 && asciiRow < ascii.Rows)
            {
                var columns = Math.Min(ascii.Columns, width - left);
                for (var column = 0; column < columns; column++)
                {
                    var index = asciiRow * ascii.Columns + column;
                    cells[left + column] = new Conxel(ascii.Characters[index], ascii.Foregrounds[index], Color.Black);
                }
            }
            else if (ascii is null && !string.IsNullOrEmpty(message) && row == height / 2)
            {
                var text = message.Length > width ? message[..width] : message;
                var start = (width - text.Length) / 2;
                for (var i = 0; i < text.Length; i++)
                {
                    cells[start + i] = new Conxel(text[i], string.IsNullOrEmpty(_openError) && string.IsNullOrEmpty(player?.Error) ? Color.Gray : Color.Red, Color.Black);
                }
            }

            buffer.Write(ZOrder, new Position(x, y + row), new Size(width, 1), new ConxelString(cells));
        }
    }

    private string PictureMessage(VideoPlayer? player)
    {
        if (!string.IsNullOrEmpty(_openError))
        {
            return _openError;
        }

        if (player is null)
        {
            return _isOpening ? "Opening…" : string.Empty;
        }

        return !string.IsNullOrEmpty(player.Error) ? player.Error : player.Frame is null ? "Decoding…" : string.Empty;
    }

    /// <summary>Converts a frame once, not on every repaint: the dialog repaints more often than frames arrive.</summary>
    private AsciiFrame ConvertToAscii(VideoFrame picture)
    {
        if (_ascii is not null && ReferenceEquals(_asciiSource, picture) && _asciiMode == _colorMode)
        {
            return _ascii;
        }

        if (_ascii is null || _ascii.Columns != picture.Columns || _ascii.Rows != picture.Rows)
        {
            _ascii = new AsciiFrame(picture.Columns, picture.Rows);
        }

        AsciiArt.Render(picture.Rgb, _ascii, _colorMode);
        _asciiSource = picture;
        _asciiMode = _colorMode;
        return _ascii;
    }

    private void DrawProgress(IBuffer buffer, int x, int y, int w, VideoPlayer? player, ColorScheme colorScheme)
    {
        var position = player?.Position ?? TimeSpan.Zero;
        var duration = player?.Duration ?? TimeSpan.Zero;
        var state = player?.State ?? VideoPlaybackState.Stopped;

        var transport = $" {StateGlyph(state)}  {FormatTime(position)} / {FormatTime(duration)}  ";
        _barX = 0;
        _barY = 0;
        _barWidth = 0;

        var barWidth = w - transport.Length - 1;
        if (barWidth >= 8)
        {
            var fraction = duration > TimeSpan.Zero
                ? Math.Clamp(position.TotalSeconds / duration.TotalSeconds, 0, 1)
                : 0;
            var filled = (int)(barWidth * fraction);
            transport += StringHelper.Repeat('█', filled) + StringHelper.Repeat('░', barWidth - filled);
            _barX = x + transport.Length - barWidth;
            _barY = y;
            _barWidth = barWidth;
        }

        WriteLine(
            buffer,
            x,
            y,
            w,
            transport,
            state == VideoPlaybackState.Playing ? Color.Green : colorScheme.Foreground,
            colorScheme.Background);
    }

    private void DrawHints(IBuffer buffer, int x, int y, int w, ColorScheme colorScheme) =>
        WriteLine(buffer, x, y, w, KeyHints, colorScheme.Foreground, colorScheme.Background);

    private void WriteLine(IBuffer buffer, int x, int y, int w, string text, Color foreground, Color background)
    {
        if (w <= 0)
        {
            return;
        }

        text = text.Length > w ? text[..w] : text.PadRight(w);
        buffer.Write(ZOrder, new Position(x, y), new Size(w, 1), new ConxelString(text, foreground, background));
    }

    private static string StateGlyph(VideoPlaybackState state) => state switch
    {
        VideoPlaybackState.Playing => "▶",
        VideoPlaybackState.Paused => "‖",
        _ => "■",
    };

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

    #endregion
}
