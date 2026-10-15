using iConsole.Controls;
using iConsole.Controls.Tabs;
using iConsole.Data;
using iConsole.Services;

namespace iTerminal.Core.Dialogs;

/// <summary>
/// Network settings. There is nothing to set here yet — hosts and their credentials are kept by the
/// network browser — so the page says where they are rather than standing empty.
/// </summary>
public class SettingsNetworkTabPage : TabPage
{
    internal static readonly string[] Lines =
    [
        "Saved hosts and their credentials: Network browser (Ctrl+N).",
        "Network interfaces: Network manager.",
    ];

    private readonly List<Label> _labels = [];

    public SettingsNetworkTabPage(
        IThemeService themeService)
        : base("2-Network", themeService)
    {
        HotConsoleKey = ConsoleKey.D2;

        foreach (var line in Lines)
        {
            var label = new Label(line, themeService);
            _labels.Add(label);
            AddControl(label);
        }
    }

    public override void OnInitializeLayout()
    {
        base.OnInitializeLayout();

        for (var i = 0; i < _labels.Count; i++)
        {
            _labels[i].Position = new Position(0, i);
            _labels[i].ViewSize = new Size(ViewSize.Width, 1);
        }
    }
}
