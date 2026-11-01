using iConsole.Controls;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Drivers;
using iConsole.Layouts;
using iConsole.Services;
using iTerminal.Core.Controls.Contents;
using iTerminal.Core.Controls.MenuBar;
using iTerminal.Core.Controls.StatusBar;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Dialogs.Git;
using iTerminal.Core.Dialogs.PasswordManager;
using iTerminal.Core.Services.RuntimeDependencies;
using iTerminal.Core.Application;
using iTerminal.Core.Services;
using System.Collections.Generic;

namespace iTerminal.Core.Screens;

public class TerminalScreen : iConsole.Screens.Screen
{
    private readonly IConsoleDriver _consoleDriver;
    private readonly MenuBar? _menuBar;
    private readonly Content? _content;
    private readonly TerminalStatusBar? _statusBar;
    private readonly StartupWarningService _startupWarningService;
    private readonly GitClientDialog? _gitClientDialog;
    private readonly NetworkBrowserDialog? _networkBrowserDialog;
    private readonly ISettingsService _settingsService;
    private readonly ToastHost? _toastHost;
    private readonly RuntimeDependencyDialog? _runtimeDependencyDialog;

    public TerminalScreen(
        IConsoleDriver consoleDriver,
        TerminalMenuBar menuBar,
        Content content,
        TerminalStatusBar statusBar,
        IThemeService themeService,
        StartupWarningService startupWarningService,
        GitClientDialog gitClientDialog,
        NetworkBrowserDialog networkBrowserDialog,
        ISettingsService settingsService,
        ToastHost? toastHost = null,
        RuntimeDependencyDialog? runtimeDependencyDialog = null,
        ToolTipHost? toolTipHost = null,
        iConsole.Graphics.SixelOverlay? sixelOverlay = null)
        : base(consoleDriver, themeService)
    {
        _consoleDriver = consoleDriver ?? NullConsoleDriver.Instance;
        Title = ApplicationInfo.Title;
        _menuBar = menuBar;
        _content = content;
        _statusBar = statusBar;
        _startupWarningService = startupWarningService;
        _gitClientDialog = gitClientDialog;
        _networkBrowserDialog = networkBrowserDialog;
        _settingsService = settingsService;
        _toastHost = toastHost;
        _runtimeDependencyDialog = runtimeDependencyDialog;
        if (menuBar is not null)
        {
            AddControl(menuBar);
        }
        if (content is not null)
        {
            AddControl(content);
        }
        if (statusBar is not null)
        {
            AddControl(statusBar);
        }
        // The Sixel feature flag is a setting, read each time a picture is about to be drawn.
        iConsole.Graphics.TerminalGraphics.EnabledSource = () => settingsService?.GetAsBool(iTerminal.Core.Data.Settings.SixelEnabled, true) ?? true;
        AddControl(new SplashDialog(themeService, sixelOverlay));
        if (toastHost is not null)
        {
            AddControl(toastHost);
        }
        // Last, so a tooltip is drawn over everything else on the screen.
        if (toolTipHost is not null)
        {
            AddControl(toolTipHost);
        }
    }

    /// <summary>
    /// A click on a toast goes to the toast. The toasts are drawn over the panels, and the base
    /// implementation offers a click to the children in the order they were added — the panel under
    /// the toast would take it first.
    /// </summary>
    public override async Task<bool> HandleMouseEventAsync(MouseEvent mouseEvent)
    {
        if (_toastHost is not null && await _toastHost.HandleMouseEventAsync(mouseEvent))
        {
            return true;
        }

        return await base.HandleMouseEventAsync(mouseEvent);
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        // Global shortcut: Ctrl+G opens the Git client regardless of which panel has focus.
        // Handled here, before delegating to the focused child, because a focused terminal
        // otherwise swallows the key in its catch-all input handler. When another modal dialog
        // is already open the input router targets that dialog instead of the screen, so this
        // intercept correctly does not fire inside other dialogs.
        if (_gitClientDialog is not null
            && keyEvent.ConsoleKey == ConsoleKey.G
            && keyEvent.Control && !keyEvent.Shift && !keyEvent.Alt)
        {
            await ShowDialogAsync(_gitClientDialog);
            return true;
        }

        // Global shortcut: Ctrl+N opens the Network Browser, same reasoning as Ctrl+G above.
        if (_networkBrowserDialog is not null
            && keyEvent.ConsoleKey == ConsoleKey.N
            && keyEvent.Control && !keyEvent.Shift && !keyEvent.Alt)
        {
            await ShowDialogAsync(_networkBrowserDialog);
            return true;
        }

        return await base.HandleKeyEventAsync(keyEvent);
    }

    public override async Task OnInitializeAsync()
    {
        // Before anything is drawn, so the first frame is already in the chosen colours.
        ThemePersistence.Restore(ThemeService, _settingsService);

        await base.OnInitializeAsync();

        // The start-up dialogs wait until the splash has been seen and closed. They are shown from a task, so this
        // method returns at once and the splash is not held back.
        _ = ShowStartupDialogsAfterSplashAsync();
    }

    /// <summary>The start-up dialogs open this long after start-up: the splash first, then a short pause.</summary>
    internal static readonly TimeSpan StartupDialogDelay = SplashDialog.DisplayTime + TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Offers the runtime dependencies (a component the features need is missing, or a newer one can be installed;
    /// skipping a version, or installing, keeps it from being offered again), then the start-up warnings. Both
    /// open over the panels once the splash is gone.
    /// </summary>
    private async Task ShowStartupDialogsAfterSplashAsync()
    {
        await Task.Delay(StartupDialogDelay);

        if (_runtimeDependencyDialog is not null && _runtimeDependencyDialog.ShouldOpenAtStartup)
        {
            await ShowDialogAsync(_runtimeDependencyDialog);
        }

        if (_startupWarningService.Warnings.Count > 0)
        {
            var dialog = new StartupWarningDialog(_startupWarningService.Warnings, ThemeService);
            await ShowDialogAsync(dialog);
        }
    }

    public override void OnWindowSizeChanged(object? sender, Size size)
    {
        ViewSize = size;
        OnInitializeLayout();
    }

    public override void OnInitializeLayout()
    {
        Layout.Fill(_consoleDriver, this);
        if (_content is not null)
        {
            _content.ViewSize = new Size(ViewSize.Width, ViewSize.Height - 2);
        }
        base.OnInitializeLayout();
        var controls = new List<IControl>();
        if (_menuBar is not null)
        {
            controls.Add(_menuBar);
        }
        if (_content is not null)
        {
            controls.Add(_content);
        }
        if (_statusBar is not null)
        {
            controls.Add(_statusBar);
        }
        if (controls.Count > 0)
        {
            Layout.StackVertical(controls);
        }
    }
}
