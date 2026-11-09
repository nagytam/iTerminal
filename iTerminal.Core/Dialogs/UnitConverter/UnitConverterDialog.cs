using iConsole.Localization;
using iConsole.Buffers;
using iConsole.Commands;
using iConsole.Controls;
using iConsole.Controls.Buttons;
using iConsole.Data;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Model;
using iConsole.Services;
using iTerminal.Core.Model.UnitConverter;

namespace iTerminal.Core.Dialogs.UnitConverter;

/// <summary>
/// Unit converter covering length, area, volume, mass, temperature, speed, time, pressure, energy,
/// power, angle and digital storage. Category/From/To are plain <see cref="ListBox{T}"/>s (arrow-key
/// navigable, consistent with the rest of the app) and the value is a free-text <see cref="TextBox"/>
/// so typing isn't hijacked the way Calculator's digit shortcuts are. Every key event resyncs the
/// shared <see cref="UnitConverterEngine"/> from whichever control the user just changed and
/// recomputes the result live.
/// </summary>
public class UnitConverterDialog : SplitPaneDialog
{
    private sealed class CategoryListBox(IListBoxModel<UnitCategory> listModel, IThemeService themeService)
        : ListBox<UnitCategory>(listModel, themeService);

    private sealed class UnitListBox(IListBoxModel<UnitDefinition> listModel, IThemeService themeService)
        : ListBox<UnitDefinition>(listModel, themeService);

    private sealed class UnitConverterActionCommand(Action action) : Command
    {
        public override Task ExecuteAsync(object? args)
        {
            action();
            return Task.CompletedTask;
        }
    }

    private readonly UnitConverterEngine _engine = new();

    private readonly ConcurrentObservableCollection<UnitCategory> _categoryItems = new();
    private readonly ConcurrentObservableCollection<UnitDefinition> _fromUnitItems = new();
    private readonly ConcurrentObservableCollection<UnitDefinition> _toUnitItems = new();

    private readonly CategoryListBox _categoryListBox;
    private readonly UnitListBox _fromUnitListBox;
    private readonly UnitListBox _toUnitListBox;
    private readonly TextBox _inputTextBox;
    private readonly Button _swapButton;

    public UnitConverterDialog(IBeepService beepService, IDateTimeProviderService dateTimeProviderService, IThemeService themeService)
        : base(nameof(UnitConverterDialog), themeService)
    {
        Title = iConsole.Localization.UiText.Get("Dialog.UnitConverter", "Unit Converter");

        _categoryListBox = new CategoryListBox(
            new ListBoxModel<UnitCategory>(_categoryItems) { TransformItem = TransformCategory },
            themeService);
        _fromUnitListBox = new UnitListBox(
            new ListBoxModel<UnitDefinition>(_fromUnitItems) { TransformItem = TransformUnit },
            themeService);
        _toUnitListBox = new UnitListBox(
            new ListBoxModel<UnitDefinition>(_toUnitItems) { TransformItem = TransformUnit },
            themeService);
        _inputTextBox = new TextBox(beepService, dateTimeProviderService, themeService)
        {
            Label = iConsole.Localization.UiText.Get("Label.Value", "Value"),
            Text = _engine.InputText
        };
        _swapButton = new Button(iConsole.Localization.UiText.Get("Button.Swap", "Swap"), new UnitConverterActionCommand(SwapUnits), themeService) { HotKeyChar = 'S' };

        AddControl(_categoryListBox);
        AddControl(_fromUnitListBox);
        AddControl(_toUnitListBox);
        AddControl(_inputTextBox);
        AddControl(_swapButton);

        _categoryItems.ReplaceAll(Enum.GetValues<UnitCategory>());
        _categoryListBox.UpdateModel();
        RepopulateUnitLists();
    }

    /// <summary>The width a category name may use; 0 until the layout knows it.</summary>
    private int _categoryTextWidth;

    /// <summary>The category name, cut with an ellipsis where its column ends: "Temperature" ran into the first unit column.</summary>
    private ListItem<UnitCategory> TransformCategory(UnitCategory category, CancellationToken cancellationToken)
    {
        string name = UnitCatalog.GetCategoryName(category);
        if (_categoryTextWidth > 2 && name.Length > _categoryTextWidth - 1)
        {
            name = name[..(_categoryTextWidth - 2)] + "…";
        }

        return new(category) { Value = name, IsEnabled = true };
    }

    /// <summary>The width a unit may use in its list; 0 until the layout knows it.</summary>
    private int _unitTextWidth;

    /// <summary>"Name (sym)", cut with an ellipsis where the column ends: the two unit columns touch, and a long name ran into the next one.</summary>
    private ListItem<UnitDefinition> TransformUnit(UnitDefinition unit, CancellationToken cancellationToken)
    {
        string text = $"{unit.Name} ({unit.Symbol})";
        if (_unitTextWidth > 2 && text.Length > _unitTextWidth - 1)
        {
            text = text[..(_unitTextWidth - 2)] + "…";
        }

        return new(unit) { Value = text, IsEnabled = true };
    }

    private void RepopulateUnitLists()
    {
        _fromUnitItems.ReplaceAll(_engine.Units.ToList());
        _toUnitItems.ReplaceAll(_engine.Units.ToList());
        _fromUnitListBox.UpdateModel();
        _toUnitListBox.UpdateModel();
        SyncListCursorsFromEngine();
    }

    private void SyncListCursorsFromEngine()
    {
        _fromUnitListBox.ScrollableModel.CursorPosition.Y = _engine.FromUnitIndex;
        _toUnitListBox.ScrollableModel.CursorPosition.Y = _engine.ToUnitIndex;
    }

    private void SwapUnits()
    {
        _engine.SwapUnits();
        SyncListCursorsFromEngine();
        StateHasChanged();
    }

    public override void OnInitializeLayout()
    {
        if (Parent is not null)
        {
            CenterInParent(Parent.ViewSize * 0.85);
        }

        var iw = Math.Max(0, (int)ViewSize.Width - 2);
        var ih = Math.Max(0, (int)ViewSize.Height - 2);
        if (iw > 0 && ih > 0)
        {
            const int headerRows = 3;
            const int footerRows = 3;
            var listHeight = Math.Max(1, ih - headerRows - footerRows);
            var listY = 1 + headerRows;

            var categoryWidth = Math.Max(10, Math.Min(22, iw / 3));
            var unitsWidth = Math.Max(1, iw - categoryWidth);
            var fromWidth = Math.Max(1, unitsWidth / 2);
            var toWidth = Math.Max(1, unitsWidth - fromWidth);

            _categoryListBox.Position = new Position(1, listY);
            _categoryListBox.ViewSize = new Size(categoryWidth, listHeight);

            _fromUnitListBox.Position = new Position(1 + categoryWidth, listY);
            _fromUnitListBox.ViewSize = new Size(fromWidth, listHeight);

            _toUnitListBox.Position = new Position(1 + categoryWidth + fromWidth, listY);
            _toUnitListBox.ViewSize = new Size(toWidth, listHeight);

            if (categoryWidth != _categoryTextWidth)
            {
                _categoryTextWidth = categoryWidth;
                _categoryItems.ReplaceAll(Enum.GetValues<UnitCategory>());
                _categoryListBox.UpdateModel();
            }

            int unitTextWidth = Math.Min(fromWidth, toWidth);
            if (unitTextWidth != _unitTextWidth)
            {
                _unitTextWidth = unitTextWidth;
                RepopulateUnitLists();
            }

            var inputY = listY + listHeight + 1;
            var inputWidth = Math.Max(1, iw - 12);
            _inputTextBox.Position = new Position(1, inputY);
            _inputTextBox.ViewSize = new Size(inputWidth, 1);
            _inputTextBox.ScrollableModel.ViewSize = _inputTextBox.ViewSize;

            _swapButton.Position = new Position(1 + inputWidth + 1, inputY);
            _swapButton.ViewSize = new Size(10, 1);
        }

        base.OnInitializeLayout();
    }

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        yield return ("Tab", UiText.Get("DialogHint.MoveCategoryUnitsValue", "Move between the category, the units and the value"));
        yield return ("Esc", iConsole.Localization.UiText.Get("Common.Close", "Close"));
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        if (keyEvent.ConsoleKey == ConsoleKey.Escape && !keyEvent.Control && !keyEvent.Shift && !keyEvent.Alt)
        {
            Close();
            return true;
        }

        var handled = await base.HandleKeyEventAsync(keyEvent);
        SyncEngineFromControls();
        return handled;
    }

    /// <summary>
    /// A click or the wheel on a list moves its cursor as a key does, and the result has to follow: before, picking a
    /// unit with the mouse left the old result on screen until some key was pressed.
    /// </summary>
    public override async Task<bool> HandleMouseEventAsync(MouseEvent mouseEvent)
    {
        bool handled = await base.HandleMouseEventAsync(mouseEvent);
        if (handled)
        {
            SyncEngineFromControls();
        }

        return handled;
    }

    private void SyncEngineFromControls()
    {
        var categories = _categoryListBox.ListModel.ListItems;
        var categoryIndex = (int)_categoryListBox.ScrollableModel.CursorPosition.Y;
        if (categoryIndex >= 0 && categoryIndex < categories.Count)
        {
            var category = categories[categoryIndex].Item;
            if (category != _engine.Category)
            {
                _engine.SetCategory(category);
                RepopulateUnitLists();
            }
        }

        var fromIndex = (int)_fromUnitListBox.ScrollableModel.CursorPosition.Y;
        if (fromIndex != _engine.FromUnitIndex)
        {
            _engine.SetFromUnit(fromIndex);
        }

        var toIndex = (int)_toUnitListBox.ScrollableModel.CursorPosition.Y;
        if (toIndex != _engine.ToUnitIndex)
        {
            _engine.SetToUnit(toIndex);
        }

        if (_inputTextBox.Text != _engine.InputText)
        {
            _engine.SetInputText(_inputTextBox.Text);
        }

        StateHasChanged();
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
        var ih = (int)ViewSize.Height - 2;
        if (iw <= 0 || ih <= 0)
        {
            return;
        }

        WriteLine(buffer, ix, iy, iw, string.Format(iConsole.Localization.UiText.Get("Units.Heading", "Unit Converter — {0}"), UnitCatalog.GetCategoryName(_engine.Category)), Color.Cyan, colorScheme.Background);

        var resultLine = ResultLine();
        var resultColor = string.IsNullOrEmpty(_engine.ResultText) ? colorScheme.Error : colorScheme.SelectedFocusedForeground;
        WriteLine(buffer, ix, iy + 1, iw, resultLine, resultColor, colorScheme.Background);

        // One heading per list, over the list itself and highlighted while it has focus. The three
        // lists stood side by side under a single band carrying the category name, and nothing said
        // which of the two unit lists was "from" and which "to".
        var categoryWidth = (int)_categoryListBox.ViewSize.Width;
        var fromWidth = (int)_fromUnitListBox.ViewSize.Width;
        var toWidth = iw - categoryWidth - fromWidth;
        DrawSectionHeader(buffer, ix, iy + 2, categoryWidth, UiText.Get("Units.Category", "Category"), _categoryListBox.IsFocused, colorScheme);
        DrawSectionHeader(buffer, ix + categoryWidth, iy + 2, fromWidth, UiText.Get("Units.From", "From"), _fromUnitListBox.IsFocused, colorScheme);
        DrawSectionHeader(buffer, ix + categoryWidth + fromWidth, iy + 2, toWidth, UiText.Get("Units.To", "To"), _toUnitListBox.IsFocused, colorScheme);

        var footerY = iy + ih - 1;
        WriteLine(buffer, ix, footerY, iw, KeyHints.FitHints(iw), colorScheme.Foreground, colorScheme.Background);
    }

    internal static string KeyHints => iConsole.Localization.UiText.Get("Hints.UnitConverter", "Tab:next list  Up/Down:choose  type a value  Ctrl+S:swap units  Esc:close");

    /// <summary>
    /// "1 m = 3.28 ft", or — when the value is not a number — says so, rather than the bare "?" that
    /// used to stand in for the result.
    /// </summary>
    internal string ResultLine()
    {
        var fromUnit = _engine.Units[_engine.FromUnitIndex];
        var toUnit = _engine.Units[_engine.ToUnitIndex];
        if (string.IsNullOrEmpty(_engine.ResultText))
        {
            return string.IsNullOrWhiteSpace(_engine.InputText)
                ? string.Format(UiText.Get("Units.TypeValue", "Type a value in {0} to convert it to {1}."), fromUnit.Name, toUnit.Name)
                : string.Format(UiText.Get("Units.NotANumber", "\"{0}\" is not a number."), _engine.InputText);
        }

        return $"{_engine.InputText} {fromUnit.Symbol}  =  {_engine.ResultText} {toUnit.Symbol}";
    }

    #region Test seams

    internal UnitConverterEngine EngineForTest => _engine;
    internal ListBox<UnitCategory> CategoryListBoxForTest => _categoryListBox;
    internal ListBox<UnitDefinition> FromUnitListBoxForTest => _fromUnitListBox;
    internal ListBox<UnitDefinition> ToUnitListBoxForTest => _toUnitListBox;
    internal TextBox InputTextBoxForTest => _inputTextBox;
    internal Button SwapButtonForTest => _swapButton;

    #endregion
}
