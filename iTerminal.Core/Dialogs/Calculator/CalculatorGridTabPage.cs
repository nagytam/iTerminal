using iConsole.Controls.Buttons;
using iConsole.Controls.Tabs;
using iConsole.Data;
using iConsole.Services;
using iTerminal.Core.Model.Calculator;

namespace iTerminal.Core.Dialogs.Calculator;

/// <summary>
/// A <see cref="TabPage"/> whose buttons are laid out in a fixed-column grid computed from the
/// page's own <see cref="Control.ViewSize"/>. Concrete pages just add buttons (in row-major reading
/// order) in their constructor; layout is entirely automatic.
/// </summary>
public abstract class CalculatorGridTabPage : TabPage
{
    protected readonly CalculatorEngine Engine;
    private readonly Action _onChanged;

    protected abstract int Columns { get; }

    protected CalculatorGridTabPage(string name, CalculatorEngine engine, Action onChanged, IThemeService themeService)
        : base(name, themeService)
    {
        Engine = engine;
        _onChanged = onChanged;
    }

    protected Button AddActionButton(string label, Action action, Func<bool>? canExecute = null) =>
        AddActionButton(() => label, action, canExecute);

    protected Button AddActionButton(Func<string> label, Action action, Func<bool>? canExecute = null)
    {
        var button = new Button(new CalculatorActionCommand(label, () =>
        {
            action();
            _onChanged();
        }, canExecute), ThemeService)
        {
            CanFocus = true
        };
        AddControl(button);
        return button;
    }

    public override void OnInitializeLayout()
    {
        var buttons = Controls.AsSnapshot().OfType<Button>().ToList();
        if (buttons.Count > 0 && ViewSize.Width > 0 && ViewSize.Height > 0)
        {
            var columns = Math.Max(1, Columns);
            var rows = (int)Math.Ceiling(buttons.Count / (double)columns);
            var cellWidth = Math.Max(1, (int)ViewSize.Width / columns);
            var cellHeight = Math.Max(1, (int)ViewSize.Height / Math.Max(1, rows));
            for (var i = 0; i < buttons.Count; i++)
            {
                var row = i / columns;
                var col = i % columns;
                buttons[i].Position = new Position(col * cellWidth, row * cellHeight);
                buttons[i].ViewSize = new Size(Math.Max(1, cellWidth - 1), 1);
            }
        }

        base.OnInitializeLayout();
    }
}
