using iConsole.Buffers;
using iConsole.Controls.Tabs;
using iConsole.Data;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Services;
using iTerminal.Core.Model.Calculator;

namespace iTerminal.Core.Dialogs.Calculator;

/// <summary>
/// Calculator with Basic, Scientific and Programming modes, switched via a horizontal <see cref="Tab"/>
/// (Left/Right arrow, matching the rest of the app's tabbed dialogs). All three modes share one
/// <see cref="CalculatorEngine"/> instance; the engine's <see cref="CalculatorEngine.Mode"/> is kept in
/// sync with the focused tab page. Digits/operators/Enter/Backspace work directly from the keyboard
/// regardless of which control has focus; scientific/programmer functions are button-driven (Tab to
/// focus, Space or click to trigger). Enter is always "=" — that is what a calculator's Enter means —
/// so it never presses the focused button; the footer says so, since nothing else on screen does.
/// </summary>
public class CalculatorDialog : SplitPaneDialog
{
    private const int HeaderRows = 3;
    private const int FooterRows = 1;

    internal const string KeyHints =
        "0-9 . + - * / ^ %  Enter:=  Bksp:back  Del/c:clear  Space:press button  Ctrl+←/→:mode  Esc:close";

    private readonly CalculatorEngine _engine = new();
    private readonly Tab _tab;

    public CalculatorDialog(IThemeService themeService)
        : base(nameof(CalculatorDialog), themeService)
    {
        Title = "Calculator";

        _tab = new Tab(themeService) { Orientation = Orientation.Horizontal };
        AddControl(_tab);

        _tab.AddTabPage(new CalculatorBasicTabPage(_engine, StateHasChanged, themeService));
        _tab.AddTabPage(new CalculatorScientificTabPage(_engine, StateHasChanged, themeService));
        _tab.AddTabPage(new CalculatorProgrammingTabPage(_engine, StateHasChanged, themeService));
    }

    public override void OnInitializeLayout()
    {
        if (Parent is not null)
        {
            CenterInParent(Parent.ViewSize * 0.8);
        }

        var iw = Math.Max(0, (int)ViewSize.Width - 2);
        var ih = Math.Max(0, (int)ViewSize.Height - 2 - HeaderRows - FooterRows);
        if (iw > 0 && ih > 0)
        {
            _tab.Position = new Position(1, 1 + HeaderRows);
            _tab.ViewSize = new Size(iw, ih);
        }

        base.OnInitializeLayout();
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        if (keyEvent.ConsoleKey == ConsoleKey.Escape && !keyEvent.Control && !keyEvent.Shift && !keyEvent.Alt)
        {
            Close();
            return true;
        }

        if (HandleCalculatorShortcut(keyEvent))
        {
            StateHasChanged();
            return true;
        }

        var handled = await base.HandleKeyEventAsync(keyEvent);
        SyncEngineMode();
        return handled;
    }

    /// <summary>A click on a mode's tab switches the page as the arrows do, and the engine has to follow at once.</summary>
    public override async Task<bool> HandleMouseEventAsync(MouseEvent mouseEvent)
    {
        bool handled = await base.HandleMouseEventAsync(mouseEvent);
        SyncEngineMode();
        return handled;
    }

    private void SyncEngineMode()
    {
        var mode = _tab.FocusedTabPage switch
        {
            0 => CalculatorMode.Basic,
            1 => CalculatorMode.Scientific,
            2 => CalculatorMode.Programming,
            _ => _engine.Mode
        };
        if (_engine.Mode != mode)
        {
            _engine.SetMode(mode);
            StateHasChanged();
        }
    }

    private bool HandleCalculatorShortcut(KeyEvent keyEvent)
    {
        if (keyEvent.Control || keyEvent.Alt)
        {
            return false;
        }

        var isHexDigit = _engine.Mode == CalculatorMode.Programming
            && _engine.NumeralBase == NumeralBase.Hexadecimal
            && char.ToUpperInvariant(keyEvent.KeyChar) is >= 'A' and <= 'F';
        if (isHexDigit)
        {
            _engine.InputDigit(char.ToUpperInvariant(keyEvent.KeyChar));
            return true;
        }

        if (keyEvent.KeyChar is >= '0' and <= '9')
        {
            _engine.InputDigit(keyEvent.KeyChar);
            return true;
        }

        switch (keyEvent.ConsoleKey)
        {
            case ConsoleKey.Enter:
                _engine.Evaluate();
                return true;
            case ConsoleKey.Backspace:
                _engine.Backspace();
                return true;
            case ConsoleKey.Delete:
                _engine.Clear();
                return true;
        }

        switch (keyEvent.KeyChar)
        {
            case '.':
                _engine.InputDecimalPoint();
                return true;
            case '+':
                _engine.ApplyOperator("+");
                return true;
            case '-':
                _engine.ApplyOperator("-");
                return true;
            case '*':
                _engine.ApplyOperator("*");
                return true;
            case '/':
                _engine.ApplyOperator("/");
                return true;
            case '^':
                _engine.ApplyOperator("pow");
                return true;
            case '%':
                _engine.Percent();
                return true;
            case '=':
                _engine.Evaluate();
                return true;
            case 'c' or 'C':
                _engine.Clear();
                return true;
        }

        return false;
    }

    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);

        if (ViewSize.IsEmpty)
        {
            return;
        }

        var colorScheme = ThemeService.CurrentColorScheme;
        var ix = (int)AbsolutePosition.X + 1;
        var iy = (int)AbsolutePosition.Y + 1;
        var iw = (int)ViewSize.Width - 2;
        if (iw <= 0)
        {
            return;
        }

        var memoryFlag = _engine.Memory != 0 ? "M  " : string.Empty;
        var modeLabel = _engine.Mode == CalculatorMode.Programming ? $"  [{_engine.NumeralBase}]" : string.Empty;
        WriteLine(buffer, ix, iy, iw, $"{memoryFlag}Calculator{modeLabel}", Color.Cyan, colorScheme.Background);
        WriteLine(buffer, ix, iy + 1, iw, _engine.Expression, colorScheme.Foreground, colorScheme.Background);

        var displayColor = _engine.IsError ? colorScheme.Error : colorScheme.SelectedFocusedForeground;
        WriteLine(buffer, ix, iy + 2, iw, _engine.Display.PadLeft(iw), displayColor, colorScheme.Background);

        var ih = (int)ViewSize.Height - 2;
        if (ih > HeaderRows)
        {
            WriteLine(buffer, ix, iy + ih - 1, iw, KeyHints.FitHints(iw), colorScheme.DisabledForeground, colorScheme.Background);
        }
    }

    #region Test seams

    internal CalculatorEngine EngineForTest => _engine;
    internal Tab TabForTest => _tab;

    #endregion
}
