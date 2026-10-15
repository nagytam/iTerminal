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
using iTerminal.Core.Dialogs.Portfolio;
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
    private readonly PortfolioManagerDialog? _portfolioManagerDialog;
    private readonly PortfolioSetupWizardDialog? _portfolioSetupWizardDialog;
    private readonly NetworkBrowserDialog? _networkBrowserDialog;
    private readonly ISettingsService _settingsService;
    private readonly ToastHost? _toastHost;
    private readonly PasswordManagerStartupDialog? _passwordManagerStartupDialog;

    public TerminalScreen(
        IConsoleDriver consoleDriver,
        TerminalMenuBar menuBar,
        Content content,
        TerminalStatusBar statusBar,
        IThemeService themeService,
        StartupWarningService startupWarningService,
        GitClientDialog gitClientDialog,
        PortfolioManagerDialog portfolioManagerDialog,
        PortfolioSetupWizardDialog portfolioSetupWizardDialog,
        NetworkBrowserDialog networkBrowserDialog,
        ISettingsService settingsService,
        ToastHost? toastHost = null,
        PasswordManagerStartupDialog? passwordManagerStartupDialog = null)
        : base(consoleDriver, themeService)
    {
        _consoleDriver = consoleDriver ?? NullConsoleDriver.Instance;
        _menuBar = menuBar;
        _content = content;
        _statusBar = statusBar;
        _startupWarningService = startupWarningService;
        _gitClientDialog = gitClientDialog;
        _portfolioManagerDialog = portfolioManagerDialog;
        _portfolioSetupWizardDialog = portfolioSetupWizardDialog;
        _networkBrowserDialog = networkBrowserDialog;
        _settingsService = settingsService;
        _toastHost = toastHost;
        _passwordManagerStartupDialog = passwordManagerStartupDialog;
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
        AddControl(new SplashDialog(themeService));
        if (toastHost is not null)
        {
            AddControl(toastHost);
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

        // Global shortcut: Ctrl+P opens the Portfolio Manager, same reasoning as Ctrl+G above.
        // First run: walk the user through the setup wizard (quote source + default stocks)
        // before ever showing the manager, then chain straight into it once it completes.
        if (_portfolioManagerDialog is not null
            && keyEvent.ConsoleKey == ConsoleKey.P
            && keyEvent.Control && !keyEvent.Shift && !keyEvent.Alt)
        {
            if (_portfolioSetupWizardDialog is not null
                && _settingsService.GetAsBool(PortfolioSetupWizardDialog.ConfiguredSettingKey, false) != true)
            {
                _portfolioSetupWizardDialog.OnCompleted = () => ShowDialogAsync(_portfolioManagerDialog);
                await ShowDialogAsync(_portfolioSetupWizardDialog);
                return true;
            }

            await ShowDialogAsync(_portfolioManagerDialog);
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

    /// <summary>The person gave up on the master password: the app does not start, and the exit code says so.</summary>
    private void StopApplication()
    {
        Environment.ExitCode = 1;
        Desktop?.CancellationTokenSource.Cancel();
    }

    public override async Task OnInitializeAsync()
    {
        // Before anything is drawn, so the first frame is already in the chosen colours.
        ThemePersistence.Restore(ThemeService, _settingsService);

        // Before the panels initialise: they restore their folders in base.OnInitializeAsync, and a remote one
        // must not try to connect while the saved passwords are still locked.
        if (_passwordManagerStartupDialog is not null && !_passwordManagerStartupDialog.IsUnlocked)
        {
            _passwordManagerStartupDialog.OnStopped = StopApplication;
            await ShowDialogAsync(_passwordManagerStartupDialog);
        }

        await base.OnInitializeAsync();

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
