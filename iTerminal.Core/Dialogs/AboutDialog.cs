using iTerminal.Core.Services.BackgroundStart;
using System.Reflection;
using System.Runtime.InteropServices;
using iConsole.Buffers;
using iConsole.Controls;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Services;

namespace iTerminal.Core.Dialogs;

/// <summary>
/// What this is, which build of it, and where to go next. It used to be an empty 80×15 frame with
/// nothing in it but its title.
/// </summary>
public class AboutDialog : Dialog
{
    /// <summary>Index, in <see cref="Lines"/>, of the first label/value row.</summary>
    private const int DetailStart = 3;

    private const int DetailGap = 3;

    private readonly KeyValueList _details;

    private readonly IBackgroundStartService? _backgroundStart;

    public AboutDialog(IThemeService themeService, IBackgroundStartService? backgroundStart = null)
        : base(nameof(AboutDialog), themeService)
    {
        _backgroundStart = backgroundStart;
        Title = iConsole.Localization.UiText.Get("Menu.About", "About");

        // The label/value block is a KeyValueList rather than pre-padded strings: it aligns the values
        // itself, so a new row ("Commit", "Data folder") is one more pair, not another PadRight.
        _details = new KeyValueList(themeService) { Gap = DetailGap };
        _details.SetItems(DetailPairs);
        AddControl(_details);
    }

    internal static IReadOnlyList<(string Key, string Value)> DetailPairs =>
    [
        ("Version", Version),
        ("Runtime", RuntimeInformation.FrameworkDescription),
        ("System", RuntimeInformation.OSDescription),
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
        "A keyboard-driven file manager and terminal for the console.",
        string.Empty,
        .. DetailLines,
        string.Empty,
        "F1 on a file panel lists its keys; the hot key bar shows the rest.",
        string.Empty,
        "Esc:close",
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

    public override async Task ShowAsync()
    {
        // Looks for a newer release when About is opened, or as configured (BackgroundStart.UpdateCheck).
        _backgroundStart?.Request(BackgroundServices.UpdateCheck);
        await base.ShowAsync();
    }

    public override void OnInitializeLayout()
    {
        base.OnInitializeLayout();

        if (Parent != null)
        {
            CenterInParent(new Size(80, Lines.Count + 4));
        }

        // The block sits where the centred text used to: as wide as its longest row, in the middle.
        var innerWidth = Math.Max(1, (int)ViewSize.Width - 2);
        var blockWidth = Math.Min(innerWidth, Lines.Skip(DetailStart).Take(DetailPairs.Count).Max(l => l.Length));
        _details.ViewSize = new Size(blockWidth, DetailPairs.Count);
        _details.Position = new Position(1 + (innerWidth - blockWidth) / 2, DetailStart + 2);
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
        var lines = Lines;
        for (var i = 0; i < lines.Count && i + 1 < innerHeight; i++)
        {
            if (i >= DetailStart && i < DetailStart + DetailPairs.Count)
            {
                continue; // drawn by the KeyValueList
            }

            var text = lines[i].Length <= innerWidth ? lines[i].Expand(innerWidth) : lines[i].Cut(innerWidth);
            var foreground = i == 0 ? colorScheme.SelectedFocusedForeground : colorScheme.Foreground;
            buffer.Write(
                ZOrder,
                new Position(AbsolutePosition.X + 1, AbsolutePosition.Y + 1 + i + 1),
                new Size(innerWidth, 1),
                new ConxelString(text, foreground, colorScheme.Background));
        }
    }
}
