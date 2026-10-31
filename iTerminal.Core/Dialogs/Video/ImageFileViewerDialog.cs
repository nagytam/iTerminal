using iConsole.Localization;
using iConsole.Buffers;
using iConsole.Controls;
using iConsole.Controls.Buttons;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Display;
using iConsole.Graphics;
using iConsole.Services;
using iTerminal.Core.Services.Images;
using iTerminal.Core.Video;

namespace iTerminal.Core.Dialogs.Video;

/// <summary>
/// F3 on a picture: the image drawn as ASCII art, fitted to the room the dialog has. It shares the
/// decoder and the character conversion with <see cref="VideoFileViewerDialog"/> — to ffmpeg a
/// picture is a one-frame video — so an animated GIF shows its first frame.
///
/// <para>On a terminal that draws Sixel (Windows Terminal, WezTerm, foot, Konsole, iTerm2…) the picture is a real bitmap sized
/// in pixels and drawn over blank cells by the renderer; anywhere else the ASCII art is the fallback.</para>
///
/// <para>Decoding runs on a background task and is redone when the available room changes, so a
/// terminal resize never leaves a stretched or clipped picture. Only the newest request is kept.</para>
/// </summary>
public class ImageFileViewerDialog : Dialog
{
    internal const string KeyHints = " c:colour  i:info  Esc:close";

    private const int HeaderRows = 1;
    private const int FooterRows = 2;

    private readonly IVideoDecoderFactory _decoderFactory;
    private readonly IBeepService _beepService;
    private readonly IDateTimeProviderService _dateTimeProviderService;
    private readonly Button _colorButton;
    private readonly object _lock = new();

    private int _generation;
    private Task _loadTask = Task.CompletedTask;
    private VideoInfo? _info;
    private VideoFrame? _picture;
    private string _error = string.Empty;
    private bool _isLoading;
    private (int Columns, int Rows) _requested;

    private AsciiColorMode _colorMode = AsciiColorMode.Color;
    private AsciiFrame? _ascii;
    private VideoFrame? _asciiSource;
    private AsciiColorMode _asciiMode;

    private readonly SixelOverlay? _sixelOverlay;
    private SixelPicture? _sixel;

    private IReadOnlyList<ImageInfoField> _metadata = [];
    private List<string>? _metadataLines;
    private bool _showMetadata;
    private int _metadataScroll;

    /// <summary>Absolute path of the file to view. Set by the caller before the dialog is shown.</summary>
    public string FilePath { get; set; } = string.Empty;

    public ImageFileViewerDialog(
        IVideoDecoderFactory decoderFactory,
        IBeepService beepService,
        IDateTimeProviderService dateTimeProviderService,
        IThemeService themeService,
        SixelOverlay? sixelOverlay = null)
        : base(nameof(ImageFileViewerDialog), themeService)
    {
        _sixelOverlay = sixelOverlay;
        _decoderFactory = decoderFactory;
        _beepService = beepService;
        _dateTimeProviderService = dateTimeProviderService;
        Title = iConsole.Localization.UiText.Get("Dialog.Image", "Image");

        _colorButton = new Button(
            new VideoActionCommand(
                () => _colorMode == AsciiColorMode.Color ? "Colour: on" : "Colour: off",
                "Switch colour on or off",
                () =>
                {
                    ToggleColor();
                    StateHasChanged();
                }),
            themeService)
        {
            CanFocus = false,
        };
        AddControl(_colorButton);
    }

    /// <summary>The newest load, so a test can await it instead of polling. Never awaited by the UI.</summary>
    public Task LoadTask => _loadTask;

    public AsciiColorMode ColorMode => _colorMode;

    public string Error => _error;

    /// <summary>The picture currently on screen, or null while there is none.</summary>
    internal VideoFrame? Picture => _picture;

    /// <summary>A picture encoded for the terminal, and the cells it covers.</summary>
    private sealed record SixelPicture(string Data, int Columns, int Rows);

    /// <summary>True when the terminal draws Sixel pictures: the picture is then a real bitmap rather than characters.</summary>
    private bool UseSixel => _sixelOverlay is not null && TerminalGraphics.Current.IsSixelSupported;

    /// <summary>The picture on screen is a Sixel bitmap and not text.</summary>
    internal bool IsShownAsSixel => _sixel is not null;

    private void ToggleColor()
    {
        _colorMode = _colorMode == AsciiColorMode.Color ? AsciiColorMode.Mono : AsciiColorMode.Color;
        RebuildSixel();
    }

    /// <summary>Encodes the picture again for the colour mode just chosen, off the render thread; the old one stays up meanwhile.</summary>
    private void RebuildSixel()
    {
        VideoFrame? picture;
        SixelPicture? current;
        int generation;
        lock (_lock)
        {
            picture = _picture;
            current = _sixel;
            generation = Volatile.Read(ref _generation);
        }

        if (picture is null || current is null)
        {
            return;
        }

        AsciiColorMode mode = _colorMode;
        _ = Task.Run(() =>
        {
            string data = SixelEncoder.Encode(SixelPixels(picture.Rgb, mode), picture.Columns, picture.Rows);
            lock (_lock)
            {
                if (generation != Volatile.Read(ref _generation) || !ReferenceEquals(_picture, picture) || mode != _colorMode)
                {
                    return;
                }

                _sixel = new SixelPicture(data, current.Columns, current.Rows);
            }

            StateHasChanged();
        });
    }

    /// <summary>The pixels to encode: as decoded, or reduced to grey for the colour-off mode.</summary>
    private static byte[] SixelPixels(byte[] rgb, AsciiColorMode mode)
    {
        if (mode == AsciiColorMode.Color)
        {
            return rgb;
        }

        byte[] grey = new byte[rgb.Length];
        for (int i = 0; i < rgb.Length; i += 3)
        {
            byte luma = (byte)((rgb[i] * 299 + rgb[i + 1] * 587 + rgb[i + 2] * 114) / 1000);
            grey[i] = luma;
            grey[i + 1] = luma;
            grey[i + 2] = luma;
        }

        return grey;
    }

    public override async Task OnInitializeAsync()
    {
        await base.OnInitializeAsync();

        lock (_lock)
        {
            Interlocked.Increment(ref _generation);
            _info = null;
            _picture = null;
            _sixel = null;
            _error = string.Empty;
            _requested = (0, 0);
        }

        _ascii = null;
        _asciiSource = null;
        _metadataLines = null;
        _metadataScroll = 0;
        Title = string.Format(UiText.Get("Dialog.ImageTitle", "Image — {0}"), Path.GetFileName(FilePath));
        // The metadata is at the front of the file: it is read here, off the render thread, so i shows it at once.
        _metadata = await Task.Run(() => ImageInfoReader.ReadFile(FilePath));
        StateHasChanged();
    }

    public override void Close()
    {
        Interlocked.Increment(ref _generation);
        _sixelOverlay?.Hide(this);
        base.Close();
    }

    private (int Columns, int Rows) Area()
    {
        var iw = (int)ViewSize.Width - 2;
        var ih = (int)ViewSize.Height - 2;
        return (Math.Max(0, iw), Math.Max(0, ih - HeaderRows - FooterRows));
    }

    public override void OnInitializeLayout()
    {
        if (Parent is not null)
        {
            CenterInParent(Parent.ViewSize * 0.9);
        }

        var iw = (int)ViewSize.Width - 2;
        var ih = (int)ViewSize.Height - 2;
        const int width = 14;
        _colorButton.Position = new Position(1 + Math.Max(0, (iw - width) / 2), 1 + ih - FooterRows);
        _colorButton.ViewSize = iw >= width && ih > HeaderRows + FooterRows ? new Size(width, 1) : new Size(0, 0);

        base.OnInitializeLayout();
    }

    /// <summary>
    /// Starts a decode for the given room unless that is already what is loaded or loading. Called
    /// from <see cref="Draw"/>, so it only records the request and starts a task.
    /// </summary>
    private void EnsureLoaded(int areaColumns, int areaRows)
    {
        lock (_lock)
        {
            // A Sixel picture is fitted in pixels while loading, so it is the room itself that is compared.
            var wanted = _info is null || UseSixel ? (areaColumns, areaRows) : AsciiArt.Fit(_info.AspectRatio, areaColumns, areaRows);
            if (_requested == wanted || !string.IsNullOrEmpty(_error) || areaColumns <= 0 || areaRows <= 0)
            {
                return;
            }

            // Until the size of the picture is known the whole room is asked for; the load itself
            // fits the picture to it once it has probed the file.
            _requested = wanted;
            var generation = Interlocked.Increment(ref _generation);
            var path = FilePath;
            _isLoading = true;
            _loadTask = Task.Run(() => Load(path, generation, areaColumns, areaRows));
        }
    }

    private void Load(string path, int generation, int areaColumns, int areaRows)
    {
        try
        {
            if (!_decoderFactory.IsAvailable)
            {
                throw new InvalidOperationException(FfmpegVideoDecoderFactory.MissingMessage);
            }

            using IVideoDecoder decoder = _decoderFactory.Create(path);
            if (UseSixel)
            {
                LoadSixel(decoder, generation, areaColumns, areaRows);
                StateHasChanged();
                return;
            }

            var (columns, rows) = AsciiArt.Fit(decoder.Info.AspectRatio, areaColumns, areaRows);
            decoder.Open(TimeSpan.Zero, columns, rows);

            var rgb = new byte[columns * rows * 3];
            if (!decoder.ReadFrame(rgb))
            {
                throw new InvalidOperationException("No picture could be decoded from this file.");
            }

            lock (_lock)
            {
                if (generation != Volatile.Read(ref _generation))
                {
                    return;
                }

                _info = decoder.Info;
                _picture = new VideoFrame(rgb, columns, rows, TimeSpan.Zero);
                _isLoading = false;
                // What was loaded is the fit for the shape just probed, so Draw has no reason to ask again.
                _requested = (columns, rows);
            }
        }
        catch (Exception exception)
        {
            lock (_lock)
            {
                if (generation == Volatile.Read(ref _generation))
                {
                    _error = exception.Message;
                    _isLoading = false;
                }
            }
        }

        StateHasChanged();
    }

    /// <summary>Decodes at the pixel size that fills the room, and encodes the result for the terminal.</summary>
    private void LoadSixel(IVideoDecoder decoder, int generation, int areaColumns, int areaRows)
    {
        SixelFit fit = TerminalGraphics.Current.Fit(decoder.Info.AspectRatio, areaColumns, areaRows);
        decoder.Open(TimeSpan.Zero, fit.PixelWidth, fit.PixelHeight);

        var rgb = new byte[fit.PixelWidth * fit.PixelHeight * 3];
        if (!decoder.ReadFrame(rgb))
        {
            throw new InvalidOperationException("No picture could be decoded from this file.");
        }

        string data = SixelEncoder.Encode(SixelPixels(rgb, _colorMode), fit.PixelWidth, fit.PixelHeight);
        lock (_lock)
        {
            if (generation != Volatile.Read(ref _generation))
            {
                return;
            }

            _info = decoder.Info;
            _picture = new VideoFrame(rgb, fit.PixelWidth, fit.PixelHeight, TimeSpan.Zero);
            _sixel = new SixelPicture(data, fit.Columns, fit.Rows);
            _isLoading = false;
            _requested = (areaColumns, areaRows);
        }
    }

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        yield return ("C", UiText.Get("DialogHint.SwitchColour", "Switch colour on or off"));
        yield return ("I", UiText.Get("DialogHint.ShowHideImageInfo", "Show or hide the image information"));
        if (_showMetadata)
        {
            yield return ("Up/Down", UiText.Get("DialogHint.ScrollInformation", "Scroll the information"));
            yield return ("PageUp/PageDown", UiText.Get("DialogHint.ScrollPageAtATime", "Scroll a page at a time"));
            yield return ("E", UiText.Get("DialogHint.EditInformation", "Edit the information"));
        }
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        if (keyEvent.KeyPressType == KeyPressType.Down
            && !keyEvent.Control
            && !keyEvent.Alt
            && keyEvent.KeyChar is 'c' or 'C')
        {
            ToggleColor();
            StateHasChanged();
            return true;
        }

        if (keyEvent.KeyPressType == KeyPressType.Down
            && !keyEvent.Control
            && !keyEvent.Alt
            && keyEvent.KeyChar is 'i' or 'I')
        {
            _showMetadata = !_showMetadata;
            StateHasChanged();
            return true;
        }

        if (_showMetadata && keyEvent.KeyPressType == KeyPressType.Down
            && !keyEvent.Control && !keyEvent.Alt
            && keyEvent.KeyChar is 'e' or 'E')
        {
            await EditTextAsync();
            return true;
        }

        if (_showMetadata && keyEvent.KeyPressType == KeyPressType.Down && !keyEvent.Control && !keyEvent.Alt)
        {
            int page = Math.Max(1, Area().Rows - 1);
            int? scroll = keyEvent.ConsoleKey switch
            {
                ConsoleKey.DownArrow => 1,
                ConsoleKey.UpArrow => -1,
                ConsoleKey.PageDown => page,
                ConsoleKey.PageUp => -page,
                ConsoleKey.Home => int.MinValue,
                ConsoleKey.End => int.MaxValue,
                _ => null,
            };
            if (scroll is int step)
            {
                int last = Math.Max(0, MetadataLines().Count - Area().Rows);
                _metadataScroll = step == int.MinValue ? 0 : step == int.MaxValue ? last : Math.Clamp(_metadataScroll + step, 0, last);
                StateHasChanged();
                return true;
            }
        }

        return await base.HandleKeyEventAsync(keyEvent);
    }

    /// <summary>True while the metadata is shown instead of the picture (i toggles it).</summary>
    internal bool ShowingMetadata => _showMetadata;

    /// <summary>
    /// Opens the text editor for the picture's description, artist, copyright and comment (e, in the metadata view, for a JPEG
    /// or a PNG). After a save the metadata is read again, so the view shows what the file now holds.
    /// </summary>
    private async Task EditTextAsync()
    {
        string path = FilePath;
        if (!ImageMetadataWriter.CanWrite(path))
        {
            _beepService.Beep();
            return;
        }

        ImageMetadataEditorDialog editor = new(
            path,
            ImageMetadataEdit.From(_metadata),
            _beepService,
            _dateTimeProviderService,
            ThemeService,
            saved: () => _ = ReloadMetadataAsync(path));
        await ShowDialogAsync(editor);
    }

    private async Task ReloadMetadataAsync(string path)
    {
        IReadOnlyList<ImageInfoField> fields = await Task.Run(() => ImageInfoReader.ReadFile(path));
        if (FilePath != path)
        {
            return;
        }

        lock (_lock)
        {
            _metadata = fields;
            _metadataLines = null;
        }

        StateHasChanged();
    }

    /// <summary>
    /// The metadata as lines: each group under a heading, then its fields as name and value. The lines are built once; the
    /// view scrolls through them.
    /// </summary>
    internal List<string> MetadataLines()
    {
        if (_metadataLines is not null)
        {
            return _metadataLines;
        }

        List<string> lines = [];
        foreach (IGrouping<string, ImageInfoField> group in _metadata.GroupBy(f => f.Group))
        {
            if (lines.Count > 0)
            {
                lines.Add(string.Empty);
            }

            lines.Add($"── {group.Key} ──────────────────────────────");
            foreach (ImageInfoField field in group)
            {
                lines.Add($"  {field.Name,-16} {field.Value}");
            }
        }

        if (lines.Count == 0)
        {
            lines.Add("No metadata in this file.");
        }

        _metadataLines = lines;
        return lines;
    }

    private void DrawMetadata(IBuffer buffer, int ix, int iy, int iw, int ih)
    {
        ColorScheme colorScheme = ThemeService.CurrentColorScheme;
        var size = new Size(Math.Max(0, iw), 1);
        var header = $" {FilePath}   metadata" + (ImageMetadataWriter.CanWrite(FilePath) ? "   e:edit text" : string.Empty);
        buffer.Write(ZOrder, new Position((uint)ix, (uint)iy), size, new ConxelString(Fit(header, iw), Color.Cyan, colorScheme.Background));

        List<string> lines = MetadataLines();
        int rows = Math.Max(0, ih - HeaderRows - FooterRows);
        for (int row = 0; row < rows; row++)
        {
            int index = _metadataScroll + row;
            string text = index < lines.Count ? lines[index] : string.Empty;
            buffer.Write(ZOrder, new Position((uint)ix, (uint)(iy + HeaderRows + row)), size,
                new ConxelString(Fit(text, iw), colorScheme.Foreground, colorScheme.Background));
        }
    }

    private static string Fit(string text, int width) =>
        text.Length >= width ? text[..Math.Max(0, width)] : text.PadRight(width);

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
            _sixelOverlay?.Hide(this);
            return;
        }

        if (_showMetadata)
        {
            _sixelOverlay?.Hide(this);
            DrawMetadata(buffer, ix, iy, iw, ih);
            return;
        }

        var (areaColumns, areaRows) = Area();
        EnsureLoaded(areaColumns, areaRows);

        VideoInfo? info;
        VideoFrame? picture;
        SixelPicture? sixel;
        string error;
        bool loading;
        lock (_lock)
        {
            info = _info;
            picture = _picture;
            sixel = _sixel;
            error = _error;
            loading = _isLoading;
        }

        var header = $" {FilePath}";
        if (info is not null)
        {
            header += $"   {info.Width}×{info.Height} pixels";
            if (picture is not null)
            {
                header += sixel is not null
                    ? $"   shown as {picture.Columns}×{picture.Rows} pixels"
                    : $"   shown as {picture.Columns}×{picture.Rows} characters";
            }
        }

        WriteLine(buffer, ix, iy, iw, header, Color.Cyan, colorScheme.Background);
        if (sixel is not null && _sixelOverlay is not null && string.IsNullOrEmpty(error)
            && sixel.Columns <= areaColumns && sixel.Rows <= areaRows)
        {
            // The room is blank black cells; the terminal draws the bitmap over them, centred.
            DrawPicture(buffer, ix, iy + HeaderRows, areaColumns, areaRows, null, string.Empty, false);
            _sixelOverlay.Show(
                this,
                ix + (areaColumns - sixel.Columns) / 2,
                iy + HeaderRows + (areaRows - sixel.Rows) / 2,
                sixel.Columns,
                sixel.Rows,
                sixel.Data,
                Color.Black);
        }
        else
        {
            _sixelOverlay?.Hide(this);
            DrawPicture(buffer, ix, iy + HeaderRows, areaColumns, areaRows, UseSixel ? null : picture, error, loading);
        }

        WriteLine(buffer, ix, iy + ih - 1, iw, KeyHints, colorScheme.Foreground, colorScheme.Background);
    }

    private void DrawPicture(IBuffer buffer, int x, int y, int width, int height, VideoFrame? picture, string error, bool loading)
    {
        AsciiFrame? ascii = picture is null ? null : ConvertToAscii(picture);
        var left = ascii is null ? 0 : Math.Max(0, (width - ascii.Columns) / 2);
        var top = ascii is null ? 0 : Math.Max(0, (height - ascii.Rows) / 2);
        var message = !string.IsNullOrEmpty(error) ? error : loading ? "Opening…" : string.Empty;
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
            else if (ascii is null && message.Length > 0 && row == height / 2)
            {
                var text = message.Length > width ? message[..width] : message;
                var start = (width - text.Length) / 2;
                for (var i = 0; i < text.Length; i++)
                {
                    cells[start + i] = new Conxel(text[i], error.Length > 0 ? Color.Red : Color.Gray, Color.Black);
                }
            }

            buffer.Write(ZOrder, new Position(x, y + row), new Size(width, 1), new ConxelString(cells));
        }
    }

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

    private void WriteLine(IBuffer buffer, int x, int y, int w, string text, Color foreground, Color background)
    {
        if (w <= 0)
        {
            return;
        }

        text = text.Length > w ? text[..w] : text.PadRight(w);
        buffer.Write(ZOrder, new Position(x, y), new Size(w, 1), new ConxelString(text, foreground, background));
    }
}
