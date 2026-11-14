using iConsole.Localization;
using iTerminal.Core.Services.BackgroundStart;
using System.Reflection;
using System.Runtime.InteropServices;
using iConsole.Buffers;
using iConsole.Controls;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Display;
using iConsole.Graphics;
using iConsole.Helper;
using iConsole.Services;

namespace iTerminal.Core.Dialogs;

/// <summary>
/// What this is, which build of it, and where to go next. It used to be an empty 80×15 frame with
/// nothing in it but its title. It now opens the way the splash does: the emblem above the text where
/// the terminal can draw pictures, the motto and the copyright, and the colour map.
/// </summary>
public class AboutDialog : Dialog
{
    /// <summary>Index, in <see cref="Lines"/>, of the first label/value row.</summary>
    private const int DetailStart = 4;

    /// <summary>Index, in <see cref="Lines"/>, of the first of the two rows the colour map takes.</summary>
    private const int ColorMapStart = 8;

    private const int DetailGap = 3;

    /// <summary>Rows of cells the emblem takes above the text when the terminal can draw pictures; the same as the splash.</summary>
    private const int LogoRows = 7;

    private readonly KeyValueList _details;

    private readonly ColorMap _colorMap;

    private readonly IBackgroundStartService? _backgroundStart;

    private readonly SixelOverlay? _sixelOverlay;

    private string? _logoSixel;

    private int _logoColumns;

    public AboutDialog(IThemeService themeService, IBackgroundStartService? backgroundStart = null, SixelOverlay? sixelOverlay = null)
        : base(nameof(AboutDialog), themeService)
    {
        _backgroundStart = backgroundStart;
        _sixelOverlay = sixelOverlay;
        Title = iConsole.Localization.UiText.Get("Menu.About", "About");

        // The label/value block is a KeyValueList rather than pre-padded strings: it aligns the values
        // itself, so a new row ("Commit", "Data folder") is one more pair, not another PadRight.
        _details = new KeyValueList(themeService) { Gap = DetailGap };
        _details.SetItems(DetailPairs);
        AddControl(_details);

        _colorMap = new ColorMap(themeService);
        AddControl(_colorMap);
    }

    internal static IReadOnlyList<(string Key, string Value)> DetailPairs =>
    [
        (UiText.Get("About.Version", "Version"), Version),
        (UiText.Get("About.Runtime", "Runtime"), RuntimeInformation.FrameworkDescription),
        (UiText.Get("About.System", "System"), RuntimeInformation.OSDescription),
    ];

    /// <summary>The version of the running app, as the build stamped it.</summary>
    internal static string Version
    {
        get
        {
            var assembly = Assembly.GetEntryAssembly() ?? typeof(AboutDialog).Assembly;
            var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(informational))
            {
                // Drop the "+<commit hash>" the SDK appends; it is noise in a dialog.
                var plus = informational.IndexOf('+');
                return plus > 0 ? informational[..plus] : informational;
            }

            return assembly.GetName().Version?.ToString() ?? "unknown";
        }
    }

    internal static IReadOnlyList<string> Lines =>
    [
        "iTerminal BETA",
        UiText.Get("Splash.Motto", "Swiss Army Terminal Knife"),
        "Copyright (C) Tamas Nagy, 2024-2026",
        iConsole.Localization.UiText.Get("About.Tagline", "A keyboard-driven file manager and terminal for the console."),
        .. DetailLines,
        string.Empty,
        string.Empty, // the colour map takes these two rows
        string.Empty,
        string.Empty,
        iConsole.Localization.UiText.Get("About.Hint", "F1 on a file panel lists its keys; the hot key bar shows the rest."),
        iConsole.Localization.UiText.Get("Hint.Part.EscClose", "Esc:close"),
    ];

    /// <summary>
    /// The label/value block, padded to one width: every line is centred on its own, so lines of
    /// different lengths left the labels in three different columns.
    /// </summary>
    private static IEnumerable<string> DetailLines
    {
        get
        {
            var pairs = DetailPairs;
            var keyWidth = pairs.Max(p => p.Key.Length);
            var lines = pairs.Select(p => p.Key.PadRight(keyWidth) + new string(' ', DetailGap) + p.Value).ToList();
            var width = lines.Max(l => l.Length);
            return lines.Select(l => l.PadRight(width));
        }
    }

    /// <summary>The row, inside the dialog, of the first line of <see cref="Lines"/>: under the emblem when there is one.</summary>
    private int FirstRow => HasLogo ? LogoRows + 1 : 2;

    /// <summary>The emblem is drawn as a picture where the terminal can show one; elsewhere the dialog is text only.</summary>
    private bool HasLogo => _logoSixel is not null;

    private void PrepareLogo()
    {
        _logoSixel = null;
        if (_sixelOverlay is null)
        {
            return;
        }

        if (SplashLogo.TryEncode(TerminalGraphics.Current, LogoRows, out string? sixel, out int columns))
        {
            _logoSixel = sixel;
            _logoColumns = columns;
        }
    }

    public override async Task ShowAsync()
    {
        // Looks for a newer release when About is opened, or as configured (BackgroundStart.UpdateCheck).
        _backgroundStart?.Request(BackgroundServices.UpdateCheck);
        await base.ShowAsync();
    }

    /// <summary>Withdraws the emblem with the dialog, so it does not stay on screen after About has gone.</summary>
    public override void Close()
    {
        _sixelOverlay?.Hide(this);
        base.Close();
    }

    public override void OnInitializeLayout()
    {
        base.OnInitializeLayout();

        if (_logoSixel is null && _sixelOverlay is not null)
        {
            PrepareLogo();
        }

        if (Parent != null)
        {
            CenterInParent(new Size(80, FirstRow + Lines.Count + 2));
        }

        // The block sits where the centred text used to: as wide as its longest row, in the middle.
        var innerWidth = Math.Max(1, (int)ViewSize.Width - 2);
        var blockWidth = Math.Min(innerWidth, Lines.Skip(DetailStart).Take(DetailPairs.Count).Max(l => l.Length));
        _details.ViewSize = new Size(blockWidth, DetailPairs.Count);
        _details.Position = new Position(1 + (innerWidth - blockWidth) / 2, FirstRow + DetailStart);

        _colorMap.Position = new Position(1 + (innerWidth - (int)_colorMap.ViewSize.Width) / 2, FirstRow + ColorMapStart);
    }

    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);

        var innerWidth = (int)ViewSize.Width - 2;
        var innerHeight = (int)ViewSize.Height - 2;
        if (innerWidth <= 0 || innerHeight <= 0)
        {
            return;
        }

        var colorScheme = ThemeService.CurrentColorScheme;
        DrawEmblem(buffer, colorScheme, innerWidth);

        var lines = Lines;
        for (var i = 0; i < lines.Count; i++)
        {
            var row = FirstRow + i;
            if (row >= (int)ViewSize.Height - 1)
            {
                break;
            }

            bool isDetail = i >= DetailStart && i < DetailStart + DetailPairs.Count;
            bool isColorMap = i >= ColorMapStart && i < ColorMapStart + 2;
            if (isDetail || isColorMap)
            {
                continue; // drawn by the KeyValueList and the ColorMap
            }

            // A line wider than the dialog ends on an ellipsis: cut at the edge, "the hot key b" stopped mid-word.
            var text = lines[i].Length <= innerWidth ? lines[i].Expand(innerWidth) : innerWidth > 1 ? lines[i][..(innerWidth - 1)] + "…" : lines[i].Cut(innerWidth);
            var foreground = i == 0 ? colorScheme.SelectedFocusedForeground : colorScheme.Foreground;
            buffer.Write(
                ZOrder,
                new Position(AbsolutePosition.X + 1, AbsolutePosition.Y + row),
                new Size(innerWidth, 1),
                new ConxelString(text, foreground, colorScheme.Background));
        }
    }

    /// <summary>The emblem, centred in the rows above the text, on the picture layer where the terminal shows one.</summary>
    private void DrawEmblem(IBuffer buffer, ColorScheme colorScheme, int innerWidth)
    {
        if (_sixelOverlay is null || _logoSixel is null)
        {
            return;
        }

        int x = (int)AbsolutePosition.X + 1 + Math.Max(0, (innerWidth - _logoColumns) / 2);
        int y = (int)AbsolutePosition.Y + 1;
        var blank = new ConxelString(new string(' ', _logoColumns), colorScheme.Foreground, colorScheme.Background);
        for (int row = 0; row < LogoRows; row++)
        {
            buffer.Write(ZOrder, new Position((uint)x, (uint)(y + row)), new Size(_logoColumns, 1), blank);
        }

        _sixelOverlay.Show(this, x, y, _logoColumns, LogoRows, _logoSixel, colorScheme.Background);
    }
}
