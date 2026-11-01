using iConsole.Localization;
﻿using iConsole.Buffers;
using iConsole.Controls;
using iConsole.Data;
using iConsole.Graphics;
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

    /// <summary>Rows of cells the emblem takes above the text when the terminal can draw pictures.</summary>
    private const int LogoRows = 7;

    private readonly SixelOverlay? _sixelOverlay;
    private string? _logoSixel;
    private int _logoColumns;

    public SplashDialog(IThemeService themeService, SixelOverlay? sixelOverlay = null)
        : base("iTerminal BETA", themeService)
    {
        _sixelOverlay = sixelOverlay;
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
        _sixelOverlay?.Hide(this);
        base.Close();
    }

    /// <summary>
    /// The terminal protocols in use, in two columns under a heading: the name, then on (green) or off (grey). Drawn in the rows
    /// below the colour map.
    /// </summary>
    private void DrawProtocols(IBuffer buffer, ColorScheme colorScheme)
    {
        int top = HasLogo ? LogoRows : 0;
        int x = (int)AbsolutePosition.X + 1;
        int y = (int)AbsolutePosition.Y + 7 + top;
        int inner = (int)ViewSize.Width - 2;
        string on = UiText.Get("Splash.StateOn", "on");
        string off = UiText.Get("Splash.StateOff", "off");
        int stateWidth = Math.Max(on.Length, off.Length);

        string heading = StringHelper.AlignCenter(" ".Repeat(inner), UiText.Get("Splash.Protocols", "Terminal protocols"));
        buffer.Write(ZOrder, new Position((uint)x, (uint)y), new Size(inner, 1), new ConxelString(heading, Color.Cyan, colorScheme.Background));

        IReadOnlyList<TerminalProtocolState> protocols = TerminalProtocols.Current();
        int columnWidth = inner / 2;
        int nameWidth = columnWidth - stateWidth - 2;
        for (int i = 0; i < protocols.Count; i++)
        {
            int column = i % 2;
            int row = i / 2;
            TerminalProtocolState protocol = protocols[i];
            int cellX = x + column * columnWidth + 1;
            string name = (protocol.Name.Length > nameWidth ? protocol.Name[..nameWidth] : protocol.Name).PadRight(nameWidth + 1);
            string state = (protocol.IsEnabled ? on : off).PadRight(stateWidth);
            buffer.Write(ZOrder, new Position((uint)cellX, (uint)(y + 1 + row)), new Size(name.Length, 1),
                new ConxelString(name, colorScheme.Foreground, colorScheme.Background));
            buffer.Write(ZOrder, new Position((uint)(cellX + name.Length), (uint)(y + 1 + row)), new Size(state.Length, 1),
                new ConxelString(state, protocol.IsEnabled ? Color.Green : Color.DarkGray, colorScheme.Background));
        }
    }

    /// <summary>The emblem is drawn as a picture where the terminal can show one; elsewhere the splash is text only.</summary>
    private bool HasLogo => _logoSixel is not null;

    private void PrepareLogo()
    {
        _logoSixel = null;
        TerminalGraphics graphics = TerminalGraphics.Current;
        if (_sixelOverlay is null || !graphics.IsSixelSupported)
        {
            return;
        }

        // Square, as tall as the rows set aside for it.
        int pixelHeight = LogoRows * graphics.CellHeight;
        int pixelWidth = pixelHeight;
        _logoColumns = (pixelWidth + graphics.CellWidth - 1) / graphics.CellWidth;
        _logoSixel = SixelEncoder.Encode(SplashLogo.Render(pixelWidth, pixelHeight), pixelWidth, pixelHeight);
    }

    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);

        if (ViewSize.IsEmpty)
        {
            return;
        }

        ColorScheme colorScheme = ThemeService.CurrentColorScheme;
        DrawProtocols(buffer, colorScheme);

        if (_sixelOverlay is null || _logoSixel is null)
        {
            return;
        }

        int x = (int)AbsolutePosition.X + 1 + Math.Max(0, ((int)ViewSize.Width - 2 - _logoColumns) / 2);
        int y = (int)AbsolutePosition.Y + 1;
        var blank = new ConxelString(new string(' ', _logoColumns), colorScheme.Foreground, colorScheme.Background);
        for (int row = 0; row < LogoRows; row++)
        {
            buffer.Write(ZOrder, new Position((uint)x, (uint)(y + row)), new Size(_logoColumns, 1), blank);
        }

        _sixelOverlay.Show(this, x, y, _logoColumns, LogoRows, _logoSixel, colorScheme.Background);
    }

    public override async Task OnInitializeAsync()
    {
        await base.OnInitializeAsync();
        IsVisible = true;
    }

    public override void OnInitializeLayout()
    {
        base.OnInitializeLayout();

        if (_logoSixel is null && _sixelOverlay is not null)
        {
            PrepareLogo();
        }

        // The emblem goes above the text, which moves down by its height.
        int top = HasLogo ? LogoRows : 0;

        if (Parent != null)
        {
            // The text, the colour map and the list of protocols (a heading and five rows).
            ViewSize = new Size(50, (uint)(15 + top));
            Position = new Position(
                Math.Max(0, (Parent.ViewSize.Width - ViewSize.Width) / 2),
                Math.Max(0, (Parent.ViewSize.Height - ViewSize.Height) / 2));
        }

        Layout.Center(this);

        _welcomeLabel.Text = StringHelper.AlignCenter(
            " ".Repeat((int)ViewSize.Width - 2), 
            "Welcome to iTerminal BETA " + RuntimeInformation.FrameworkDescription);
        _welcomeLabel.IsBold = true;
        _welcomeLabel.Position = new Position(1, 1 + top);
        _welcomeLabel.ViewSize = new Size(_welcomeLabel.Text.Length, 1);

        _copyrightLabel.Text = StringHelper.AlignCenter(
            " ".Repeat((int)ViewSize.Width - 2),
            "Copyright (C) Tamas Nagy, 2024-2026");
        _copyrightLabel.Position = new Position(1, 2 + top);
        _copyrightLabel.ViewSize = new Size(_copyrightLabel.Text.Length, 1);

        _userLabel.Text = StringHelper.AlignCenter(
            " ".Repeat((int)ViewSize.Width - 2),
            $"{Environment.UserName} @ {Environment.MachineName}");
        _userLabel.Position = new Position(1, 3 + top);
        _userLabel.ViewSize = new Size(_userLabel.Text.Length, 1);

        _systemInfoLabel.Text = StringHelper.AlignCenter(
            " ".Repeat((int)ViewSize.Width - 2),
            string.Format(UiText.Get("Splash.SystemInfo", "CPU: {0} cores, Memory: {1} GBs"), Environment.ProcessorCount, SystemHelper.GetAvailableMemoryInMBs() / 1000));
        _systemInfoLabel.Position = new Position(1, 4 + top);
        _systemInfoLabel.ViewSize = new Size(_systemInfoLabel.Text.Length, 1);

        _colorMap.Position = new Position((ViewSize.Width - 16) / 2, 5 + top);
    }
}
