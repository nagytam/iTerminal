using iConsole.Buffers;
using iConsole.Commands;using iConsole.Controls.Buttons;
using iConsole.Controls.PopUpWindows;
using iConsole.Data;
using iConsole.Layouts;
using iConsole.Services;
using iTerminal.Core.Services;

namespace iTerminal.Core.Controls.StatusBar;

public class TerminalStatusBar : iConsole.Controls.StatusBar.StatusBar
{
    private readonly HotKeys _hotKeys;
    private readonly Button _dateOpenButton;
    protected DatePopUpWindow _datePopUpWindow { get; private set; }

    public TerminalStatusBar(
        DatePopUpWindow datePopUpWindow, 
        HotKeys hotKeys,
        IThemeService themeService) 
        : base(themeService)
    {
        Name = nameof(TerminalStatusBar);
        _hotKeys = hotKeys ?? new HotKeys(
            // 31 leading dialog/service dependencies are irrelevant to the null-object fallback.
            null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!,
            null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!,
            NullSettingsService.Instance, null!, themeService);
        _datePopUpWindow = datePopUpWindow ?? new DatePopUpWindow(themeService);

        ViewSize = new Size(0, 1);

        AddControl(_hotKeys);

        var command = new DatePopUpWindowCommand(_datePopUpWindow);
        AddCommand(command);

        AddControl(_datePopUpWindow);
        _dateOpenButton = new Button("*", command, themeService)
        {
            ViewSize = new Size(3, 1)
        };
        AddControl(_dateOpenButton);
    }

    public override async Task OnInitializeAsync()
    {
        await base.OnInitializeAsync();
        _datePopUpWindow.Hide();
    }

    public override void OnInitializeLayout()
    {
        if (Parent != null)
        {
            ViewSize = new Size(Parent.ViewSize.Width, 1);
        }
        base.OnInitializeLayout();

        Layout.LeftBottom(_hotKeys);
        LayoutDateOpenButton();
    }

    public override void Draw(IBuffer buffer)
    {
        _hotKeys.LayoutHotKeysRow();
        LayoutDateOpenButton();
        base.Draw(buffer);
    }

    private void LayoutDateOpenButton()
    {
        _dateOpenButton.Position = new Position(
            _hotKeys.Position.X + _hotKeys.ViewSize.Width,
            _hotKeys.Position.Y);
    }
}
