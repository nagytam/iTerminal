using iConsole.Controls;
using iConsole.Data;
using iConsole.Layouts;
using iConsole.Services;
using iTerminal.Core.Controls.Terminals;
using iTerminal.Core.Factories;
using iTerminal.Core.Commands;
using System.Diagnostics;
using iConsole.Interfaces;

namespace iTerminal.Core.Controls.Contents;

public class Content : Window, IContent
{
    public const string Left = "LeftPanel";
    public const string Right = "RightPanel";

    private readonly FilePanel? _leftPanel;
    private readonly FilePanel? _rightPanel;
    private readonly AiPromptBar? _aiPromptBar;
    private readonly TerminalPanel? _terminalPanel;
    private readonly int _barHeight;
    private bool _showTerminalPanel = false;
    private BottomBarMode _bottomBarMode = BottomBarMode.Ai;

    /// <summary>What had focus when the full-screen terminal was opened, so closing it goes back there.</summary>
    private iConsole.Controls.IControl? _focusBeforeTerminal;

    public Content(
        FilePanelFactory filePanelFactory,
        AiPromptBar aiPromptBar,
        TerminalPanel terminalPanel,
        IContentModelService contentModelService,
        IThemeService themeService,
        Microsoft.Extensions.Options.IOptions<AI.AiOptions> aiOptions,
        InteractiveShellCommand? shellCommand = null)
        : base(themeService)
    {
        Name = nameof(Content);
        _leftPanel = filePanelFactory?.Create(Left);
        _rightPanel = filePanelFactory?.Create(Right);
        _aiPromptBar = aiPromptBar;
        _terminalPanel = terminalPanel;
        _barHeight = aiOptions?.Value.BarHeight ?? 5;

        if (_leftPanel is not null) AddControl(_leftPanel);
        if (_rightPanel is not null) AddControl(_rightPanel);
        if (aiPromptBar is not null)
        {
            AddControl(aiPromptBar);
            aiPromptBar.PreferredHeightChanged += OnBarHeightChanged;
        }
        if (terminalPanel is not null) AddControl(terminalPanel);

        AddCommand(new SwitchPanelsCommand(contentModelService));
        AddCommand(new ToggleTerminalCommand(this));
        if (shellCommand is not null)
        {
            AddCommand(shellCommand);
        }
        AddCommand(new CycleBottomBarModeCommand(this));

        _leftPanel?.SetFocus();
        if (_terminalPanel is not null) _terminalPanel.IsVisible = false;
    }

    /// <summary>
    /// The bottom bar's height: the configured one, except an AI bar with no model behind it, which
    /// needs a single row — the panels get the rest. The terminal bar always keeps its height.
    /// </summary>
    internal int BarHeight =>
        _bottomBarMode == BottomBarMode.Ai && _aiPromptBar is not null
            ? _aiPromptBar.PreferredHeight(_barHeight)
            : _barHeight;

    private void OnBarHeightChanged(object? sender, EventArgs e)
    {
        OnInitializeLayout();
        GetRootControl()?.Refresh();
    }

    /// <summary>What fills the bottom of the screen now; a workspace saves it and puts it back.</summary>
    public iTerminal.Core.Model.Workspaces.WorkspaceBar CurrentBar =>
        _showTerminalPanel ? iTerminal.Core.Model.Workspaces.WorkspaceBar.FullTerminal
        : _bottomBarMode == BottomBarMode.Terminal ? iTerminal.Core.Model.Workspaces.WorkspaceBar.Terminal
        : iTerminal.Core.Model.Workspaces.WorkspaceBar.Ai;

    public void ToggleTerminalPanel()
    {
        if (!_showTerminalPanel)
        {
            iConsole.Controls.IControl?[] candidates = [_leftPanel, _rightPanel, _aiPromptBar, _terminalPanel];
            _focusBeforeTerminal = candidates.FirstOrDefault(c => c is { IsFocused: true });
        }

        _showTerminalPanel = !_showTerminalPanel;
        ApplyVisibility();

        if (_showTerminalPanel)
        {
            _terminalPanel?.SetFocus();
        }
        else
        {
            // Back to where the user was: it always used to be the left panel, so opening the
            // terminal from the right panel and closing it again moved the cursor across.
            var previous = _focusBeforeTerminal is { IsVisible: true } ? _focusBeforeTerminal : _leftPanel;
            previous?.SetFocus();
        }

        OnInitializeLayout();
        StateHasChanged();
    }

    public void CycleBottomBarMode()
    {
        if (_showTerminalPanel) return;
        _bottomBarMode = _bottomBarMode == BottomBarMode.Ai
            ? BottomBarMode.Terminal
            : BottomBarMode.Ai;
        ApplyVisibility();

        if (_bottomBarMode == BottomBarMode.Terminal)
            _terminalPanel?.SetFocus();
        else
            _aiPromptBar?.SetFocus();

        OnInitializeLayout();
        StateHasChanged();
    }

    public void ShowFilePanels()
    {
        _showTerminalPanel = false;
        _bottomBarMode = BottomBarMode.Ai;
        ApplyVisibility();
        _leftPanel?.SetFocus();
        OnInitializeLayout();
        StateHasChanged();
    }

    public void ShowFullTerminal()
    {
        _showTerminalPanel = true;
        ApplyVisibility();
        _terminalPanel?.SetFocus();
        OnInitializeLayout();
        StateHasChanged();
    }

    public void ShowTerminalBar()
    {
        _showTerminalPanel = false;
        _bottomBarMode = BottomBarMode.Terminal;
        ApplyVisibility();
        _terminalPanel?.SetFocus();
        OnInitializeLayout();
        StateHasChanged();
    }

    public void ShowAiBar()
    {
        _showTerminalPanel = false;
        _bottomBarMode = BottomBarMode.Ai;
        ApplyVisibility();
        _aiPromptBar?.SetFocus();
        OnInitializeLayout();
        StateHasChanged();
    }

    /// <summary>Whether the command line is on screen: the terminal bar, or the full-screen terminal.</summary>
    public bool IsCommandLineVisible =>
        _terminalPanel is not null && _terminalPanel.IsVisible;

    /// <summary>
    /// Shows the terminal bar when the AI bar is up, without moving focus - what a name copied
    /// from a panel to the command line needs, since the user stays in the panel. A no-op while
    /// the command line is already on screen.
    /// </summary>
    public void RevealCommandLine()
    {
        if (_terminalPanel is null || IsCommandLineVisible)
        {
            return;
        }

        _showTerminalPanel = false;
        _bottomBarMode = BottomBarMode.Terminal;
        ApplyVisibility();
        OnInitializeLayout();
        StateHasChanged();
    }

    public void FocusLeftPanel() => _leftPanel?.SetFocus();
    public void FocusRightPanel() => _rightPanel?.SetFocus();
    public void FocusAiBar() => _aiPromptBar?.SetFocus();
    public void FocusTerminalBar() => _terminalPanel?.SetFocus();

    private void ApplyVisibility()
    {
        if (_showTerminalPanel)
        {
            if (_leftPanel is not null) _leftPanel.IsVisible = false;
            if (_rightPanel is not null) _rightPanel.IsVisible = false;
            if (_aiPromptBar is not null) _aiPromptBar.IsVisible = false;
            if (_terminalPanel is not null) _terminalPanel.IsVisible = true;
        }
        else
        {
            if (_leftPanel is not null) _leftPanel.IsVisible = true;
            if (_rightPanel is not null) _rightPanel.IsVisible = true;
            if (_aiPromptBar is not null) _aiPromptBar.IsVisible = _bottomBarMode == BottomBarMode.Ai;
            if (_terminalPanel is not null) _terminalPanel.IsVisible = _bottomBarMode == BottomBarMode.Terminal;
        }
    }

    public override void OnWindowSizeChanged(object? sender, iConsole.Data.Size size)
    {
        ViewSize = size;
        OnInitializeLayout();
    }

    public override void OnInitializeLayout()
    {
        Debug.Assert(ViewSize.Width >= 0);
        Debug.Assert(ViewSize.Height >= 0);

        if (_showTerminalPanel)
        {
            if (_terminalPanel is not null)
            {
                _terminalPanel.ViewSize = new Size(ViewSize.Width, ViewSize.Height);
                _terminalPanel.Position = new Position(0, 0);
            }
        }
        else
        {
            var barHeight = BarHeight;
            var panelHeight = Math.Max(0, ViewSize.Height - barHeight);
            if (_leftPanel is not null)
                _leftPanel.ViewSize = new Size(ViewSize.Width / 2, panelHeight);
            // The remainder, not another half: at an odd width two halves left the last column to
            // nobody, and nothing ever repainted it.
            if (_rightPanel is not null)
                _rightPanel.ViewSize = new Size(ViewSize.Width - ViewSize.Width / 2, panelHeight);

            var barPosition = new Position(0, panelHeight);
            var barSize = new Size(ViewSize.Width, barHeight);

            if (_aiPromptBar is not null)
            {
                _aiPromptBar.ViewSize = barSize;
                _aiPromptBar.Position = barPosition;
            }
            if (_terminalPanel is not null && _bottomBarMode == BottomBarMode.Terminal)
            {
                _terminalPanel.ViewSize = barSize;
                _terminalPanel.Position = barPosition;
            }
            if (_leftPanel is not null && _rightPanel is not null)
                Layout.StackHorizontal([_leftPanel, _rightPanel]);
        }

        base.OnInitializeLayout();

        // Re-apply inline terminal size after base call because TerminalPanel.OnInitializeLayout
        // auto-sizes itself to the full parent. This override restores the bar dimensions.
        if (!_showTerminalPanel && _bottomBarMode == BottomBarMode.Terminal && _terminalPanel is not null)
        {
            var panelHeight = Math.Max(0, ViewSize.Height - _barHeight);
            _terminalPanel.ViewSize = new Size(ViewSize.Width, _barHeight);
            _terminalPanel.Position = new Position(0, panelHeight);
        }
    }
}
