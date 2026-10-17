using iConsole.Buffers;
using iConsole.Controls.Tabs;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Helper;
using iConsole.Interfaces;
using iConsole.Services;
using iConsole.Tasks;
using iTerminal.Core.Model;

namespace iTerminal.Core.Dialogs;

public class TerminalDialog : Dialog
{
    private ITerminalModel _terminalModel { get; set; }
    private ITerminalCommand? _terminalCommand {  get; set; }
    private Tab _tab { get; set; }

    public TerminalDialog(        
        TerminalModel terminalModel,
        IThemeService themeService) 
        : base(nameof(TerminalDialog), themeService)
    {
        _terminalModel = terminalModel ?? new TerminalModel(new iConsole.Tasks.TaskManager(), null!);
        _terminalCommand = _terminalModel.History.LastOrDefault();
        Title = $"Terminal";
        _tab = new Tab(themeService);
        AddControl(_tab);

        var tabOutputPage = new TerminalOutputTabPage(_terminalModel, themeService);
        _tab.AddTabPage(tabOutputPage);

        var tabErrorPage = new TerminalErrorTabPage(_terminalModel, themeService);
        _tab.AddTabPage(tabErrorPage);

        var tabEnvironmentVariablesPage = new TerminalEnvironmentVariablesTabPage(_terminalModel, themeService);
        _tab.AddTabPage(tabEnvironmentVariablesPage);

        var tabCommandPage = new TerminalCommandTabPage(_terminalModel, themeService);
        _tab.AddTabPage(tabCommandPage);
    }

    public override async Task OnInitializeAsync()
    {
        await base.OnInitializeAsync();

        _terminalCommand = _terminalModel.History.LastOrDefault();
        if (_terminalCommand is not null)
        {
            Title = $"Terminal - {_terminalCommand?.Command}";
        }
        else
        {
            Title = $"Terminal";
        }

    }
    public override void OnInitializeLayout()
    {
        if (Parent != null)
        {
            CenterInParent(new Size(80, 24));
        }

        _tab.Position = new Position(1, 1);
        _tab.ViewSize = new Size(ViewSize.Width - 2, ViewSize.Height - 3);

        base.OnInitializeLayout();
        _terminalCommand = _terminalModel.History.LastOrDefault();
    }

    public void Reset()
    {
        _terminalCommand = null;
    }

    internal const string KeyHints = "Ctrl+1..4 or Ctrl+Left/Right:page   Up/Down:scroll   Esc:close";

    internal Tab TabForTest => _tab;

    /// <summary>
    /// The row under the pages names the keys: the pages are switched with chords nothing else on
    /// screen mentions, and the row was left blank.
    /// </summary>
    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);

        var innerWidth = (int)ViewSize.Width - 2;
        if (innerWidth <= 0 || ViewSize.Height < 3)
        {
            return;
        }

        var colorScheme = ThemeService.CurrentColorScheme;
        buffer.Write(
            ZOrder,
            new Position(AbsolutePosition.X + 1, AbsolutePosition.Y + ViewSize.Height - 2),
            new Size(innerWidth, 1),
            new iConsole.Display.ConxelString((" " + KeyHints).FitHints(innerWidth).ExpandRight(innerWidth), colorScheme.DisabledForeground, colorScheme.Background));
    }
}

