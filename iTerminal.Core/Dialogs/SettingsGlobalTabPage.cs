using iConsole.Controls;
using iConsole.Controls.Tabs;
using iConsole.Data;
using iConsole.Helper;
using iConsole.Services;

namespace iTerminal.Core.Dialogs;

/// <summary>
/// The first page of the settings dialog, and so the one it opens on. It used to be empty — the
/// dialog greeted the user with a blank page — so it now says where the app-wide choices live.
/// </summary>
public class SettingsGlobalTabPage : TabPage
{
    internal static readonly string[] Lines =
    [
        "Colour theme: Theme manager (Alt+F6).",
        "Folders, favourites and remote hosts: Folder selector (Ctrl+F1).",
        "Quote and weather indicators, music and documents folders: pages 3 to 5.",
    ];

    private readonly List<Label> _labels = [];

    public SettingsGlobalTabPage(
        IThemeService themeService)
        : base("1-Global ", themeService)
    {
        HotConsoleKey = ConsoleKey.D1;

        // A fixed set of rows, filled at layout: each line is word-wrapped to the page's width, and
        // building labels per wrapped row would change the control tree on every layout. On an
        // 80-column screen the lines used to be cut at the page edge ("…remote hosts: Folde").
        for (var i = 0; i < MaxRows; i++)
        {
            var label = new Label(string.Empty, themeService);
            _labels.Add(label);
            AddControl(label);
        }
    }

    private const int MaxRows = 12;

    /// <summary>The page's rows at <paramref name="width"/>: every line wrapped, continuation rows indented.</summary>
    internal static List<string> WrapLines(int width)
    {
        List<string> rows = [];
        foreach (var line in Lines)
        {
            var wrapped = line.WrapWords(Math.Max(10, width - 2));
            for (var i = 0; i < wrapped.Count; i++)
            {
                rows.Add(i == 0 ? wrapped[i] : "  " + wrapped[i]);
            }
        }
        return rows;
    }

    public override void OnInitializeLayout()
    {
        base.OnInitializeLayout();

        var rows = WrapLines((int)ViewSize.Width);
        for (var i = 0; i < _labels.Count; i++)
        {
            _labels[i].Text = i < rows.Count ? rows[i] : string.Empty;
            _labels[i].Position = new Position(0, i);
            _labels[i].ViewSize = new Size(ViewSize.Width, 1);
        }
    }
}