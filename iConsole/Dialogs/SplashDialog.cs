using iConsole.Localization;
﻿using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Layouts;
using iConsole.Services;
using System.Runtime.InteropServices;

namespace iConsole.Dialogs;

public class SplashDialog : Dialog
{
    private Label _welcomeLabel { get; set; }
    private Label _copyrightLabel { get; set; }
    private Label _userLabel { get; set; }
    private Label _systemInfoLabel { get; set; }
    private ColorMap _colorMap { get; set; }

    public SplashDialog(IThemeService themeService) 
        : base("iTerminal", themeService)
    {
        _welcomeLabel = new Label(string.Empty, themeService);
        _copyrightLabel = new Label(string.Empty, themeService);
        _userLabel = new Label(string.Empty, themeService);
        _systemInfoLabel = new Label(string.Empty, themeService);
        _colorMap = new ColorMap(themeService);

        AddControl(_welcomeLabel);
        AddControl(_copyrightLabel);
        AddControl(_userLabel);
        AddControl(_systemInfoLabel);
        AddControl(_colorMap);

        var timer = new System.Timers.Timer(3000)
        {
            AutoReset = false
        };
        timer.Elapsed += (sender, e) =>
        {
            timer.Stop();
            timer.Dispose();
            Close();
        };
        timer.Start();
    }

    private int _isClosed;

    /// <summary>
    /// Closes once. Esc closes the splash at once and the timer closes it again three seconds later;
    /// the second close used to run all of <see cref="Dialog.Close"/> again — popping the focus entry
    /// of whatever dialog had been opened since (focus then went back to the wrong control when that
    /// one closed) and repainting the screen from under it.
    /// </summary>
    public override void Close()
    {
        if (Interlocked.Exchange(ref _isClosed, 1) == 1)
        {
            return;
        }
        base.Close();
    }

    public override async Task OnInitializeAsync()
    {
        await base.OnInitializeAsync();
        IsVisible = true;
    }

    public override void OnInitializeLayout()
    {
        base.OnInitializeLayout();

        if (Parent != null)
        {
            ViewSize = new Size(50, 8);
            Position = new Position(
                Math.Max(0, (Parent.ViewSize.Width - ViewSize.Width) / 2),
                Math.Max(0, (Parent.ViewSize.Height - ViewSize.Height) / 2));
        }

        Layout.Center(this);

        _welcomeLabel.Text = StringHelper.AlignCenter(
            " ".Repeat((int)ViewSize.Width - 2), 
            "Welcome to iTerminal " + RuntimeInformation.FrameworkDescription);
        _welcomeLabel.IsBold = true;
        _welcomeLabel.Position = new Position(1, 1);
        _welcomeLabel.ViewSize = new Size(_welcomeLabel.Text.Length, 1);

        _copyrightLabel.Text = StringHelper.AlignCenter(
            " ".Repeat((int)ViewSize.Width - 2),
            "Copyright (C) Tamas Nagy, 2024-2026");
        _copyrightLabel.Position = new Position(1, 2);
        _copyrightLabel.ViewSize = new Size(_copyrightLabel.Text.Length, 1);

        _userLabel.Text = StringHelper.AlignCenter(
            " ".Repeat((int)ViewSize.Width - 2),
            $"{Environment.UserName} @ {Environment.MachineName}");
        _userLabel.Position = new Position(1, 3);
        _userLabel.ViewSize = new Size(_userLabel.Text.Length, 1);

        _systemInfoLabel.Text = StringHelper.AlignCenter(
            " ".Repeat((int)ViewSize.Width - 2),
            string.Format(UiText.Get("Splash.SystemInfo", "CPU: {0} cores, Memory: {1} GBs"), Environment.ProcessorCount, SystemHelper.GetAvailableMemoryInMBs() / 1000));
        _systemInfoLabel.Position = new Position(1, 4);
        _systemInfoLabel.ViewSize = new Size(_systemInfoLabel.Text.Length, 1);

        _colorMap.Position = new Position((ViewSize.Width - 16) / 2, 5);
    }
}
