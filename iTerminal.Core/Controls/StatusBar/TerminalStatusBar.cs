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
    /// <summary>
    /// Columns the date button may take: its bracketed label of up to ten letters ("[Calendar]"). The hot key row
    /// leaves this room, so the label is never cut.
    /// </summary>
    public const int DateOpenButtonWidth = 12;

    private static string DateOpenButtonLabel => iConsole.Localization.UiText.Get("HotKey.Calendar", "Calendar");

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
            // 29 leading dialog/service dependencies are irrelevant to the null-object fallback.
            null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!,
            null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!,
            NullSettingsService.Instance, null!, themeService, null!, null!);
        _datePopUpWindow = datePopUpWindow ?? new DatePopUpWindow(themeService);

        ViewSize = new Size(0, 1);

        AddControl(_hotKeys);

        var command = new DatePopUpWindowCommand(_datePopUpWindow);
        AddCommand(command);

        AddControl(_datePopUpWindow);
        // A named button, not a bare "*": the footer has to say what the button opens.
        _dateOpenButton = new Button(DateOpenButtonLabel, command, themeService);
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
        // Read on every draw, so a language change relabels it as it does the hot keys.
        _dateOpenButton.Title = DateOpenButtonLabel;
        LayoutDateOpenButton();
        base.Draw(buffer);
    }

    private void LayoutDateOpenButton()
    {
        // As wide as its label and its brackets, so a language with a longer word still shows all of it.
        _dateOpenButton.ViewSize = new Size(_dateOpenButton.Title.Length + 2, 1);
        _dateOpenButton.Position = new Position(
            _hotKeys.Position.X + _hotKeys.ViewSize.Width,
            _hotKeys.Position.Y);
    }
}
