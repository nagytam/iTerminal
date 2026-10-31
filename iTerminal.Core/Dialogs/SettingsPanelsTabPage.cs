using iConsole.Localization;
using iConsole.Controls;
using iConsole.Controls.Tabs;
using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Model;
using iTerminal.Core.Services;

namespace iTerminal.Core.Dialogs;

/// <summary>
/// How the two panels list a folder: each panel's order (what Shift+F11 sets, here for both at
/// once), and three things that hold for both — whether hidden entries are listed, whether folders
/// are kept ahead of files, and whether names are compared the way a person reads numbers.
///
/// <para>Like every page of this dialog, a change is the setting: it is written as it is made and
/// the panels behind the dialog re-order at once. There is no OK to press and nothing to lose by
/// Esc.</para>
/// </summary>
public class SettingsPanelsTabPage : TabPage
{
    private static readonly IReadOnlyList<PanelSortMode> Modes = SortDialog.Modes;

    private readonly ISettingsService _settingsService;
    private readonly IContentModelService? _contentModelService;
    private readonly ComboBox<PanelSortMode> _leftSort;
    private readonly CheckBox _leftReversed;
    private readonly ComboBox<PanelSortMode> _rightSort;
    private readonly CheckBox _rightReversed;
    private readonly CheckBox _showHidden;
    private readonly CheckBox _foldersFirst;
    private readonly CheckBox _naturalOrder;
    private readonly Label _hintLabel;
    private readonly Separator _orderRule;
    private readonly Separator _listingRule;

    /// <summary>True while the controls are being filled from the settings, so that filling them is not read as the user changing them.</summary>
    private bool _loading;

    public SettingsPanelsTabPage(
        ISettingsService settingsService,
        IContentModelService? contentModelService,
        IThemeService themeService)
        : base("6-Panels ", themeService)
    {
        HotConsoleKey = ConsoleKey.D6;
        _settingsService = settingsService ?? NullSettingsService.Instance;
        _contentModelService = contentModelService;

        _leftSort = CreateSortBox("Left panel order ", themeService);
        _leftReversed = CreateCheckBox("Reversed", themeService);
        _rightSort = CreateSortBox("Right panel order", themeService);
        _rightReversed = CreateCheckBox("Reversed", themeService);
        _showHidden = CreateCheckBox("Show hidden files and folders", themeService);
        _foldersFirst = CreateCheckBox("Keep folders ahead of files when sorting", themeService);
        _naturalOrder = CreateCheckBox("Sort numbers in names by value (file2 before file10)", themeService);
        _hintLabel = new Label("Shift+F11 changes the focused panel's order without coming here.", themeService);

        // Two titled rules group the page: what orders the panels, and what both of them list.
        _orderRule = new Separator(themeService) { Title = UiText.Get("Settings.Order", "Order") };
        _listingRule = new Separator(themeService) { Title = UiText.Get("Settings.Listing", "Listing") };

        // The first focusable control is where the cursor lands when the page is chosen.
        AddControl(_orderRule);
        AddControl(_leftSort);
        AddControl(_leftReversed);
        AddControl(_rightSort);
        AddControl(_rightReversed);
        AddControl(_listingRule);
        AddControl(_showHidden);
        AddControl(_foldersFirst);
        AddControl(_naturalOrder);
        AddControl(_hintLabel);
    }

    private ComboBox<PanelSortMode> CreateSortBox(string label, IThemeService themeService)
    {
        var box = new ComboBox<PanelSortMode>(themeService) { Label = label, DisplaySelector = PanelSort.DisplayName };
        box.SetItems(Modes);
        box.SelectionChanged += OnChanged;
        return box;
    }

    private CheckBox CreateCheckBox(string label, IThemeService themeService)
    {
        var box = new CheckBox(themeService) { Label = label };
        box.CheckedChanged += OnChanged;
        return box;
    }

    public PanelSort LeftSort
    {
        get => Read(_leftSort, _leftReversed);
        set => Write(_leftSort, _leftReversed, value);
    }

    public PanelSort RightSort
    {
        get => Read(_rightSort, _rightReversed);
        set => Write(_rightSort, _rightReversed, value);
    }

    public PanelViewOptions ViewOptions
    {
        get => new(_showHidden.IsChecked, _foldersFirst.IsChecked, _naturalOrder.IsChecked);
        set
        {
            _showHidden.IsChecked = value.ShowHidden;
            _foldersFirst.IsChecked = value.FoldersFirst;
            _naturalOrder.IsChecked = value.NaturalOrder;
        }
    }

    private static PanelSort Read(ComboBox<PanelSortMode> box, CheckBox reversed)
    {
        var mode = box.SelectedIndex >= 0 && box.SelectedIndex < Modes.Count ? Modes[box.SelectedIndex] : PanelSortMode.AsListed;

        // The drive's own order has no reverse: a box left ticked beside it means nothing.
        return new PanelSort(mode, mode != PanelSortMode.AsListed && reversed.IsChecked);
    }

    private static void Write(ComboBox<PanelSortMode> box, CheckBox reversed, PanelSort sort)
    {
        box.SelectedIndex = Math.Max(0, Modes.ToList().IndexOf(sort.Mode));
        reversed.IsChecked = sort.Descending;
    }

    public override Task OnInitializeAsync()
    {
        Load();
        return base.OnInitializeAsync();
    }

    /// <summary>Fills the page from what is saved — which Shift+F11 may have changed since it was last shown.</summary>
    public void Load()
    {
        _loading = true;
        try
        {
            LeftSort = PanelSort.Parse(_settingsService.Get(PanelSort.SettingsKey(ContentModelService.Left)));
            RightSort = PanelSort.Parse(_settingsService.Get(PanelSort.SettingsKey(ContentModelService.Right)));
            ViewOptions = PanelViewOptions.Load(_settingsService);
        }
        finally
        {
            _loading = false;
        }
    }

    private void OnChanged(object? sender, EventArgs e)
    {
        if (!_loading)
        {
            Apply();
        }
    }

    /// <summary>Saves what the page shows and puts the open panels in that order.</summary>
    public void Apply()
    {
        var left = LeftSort;
        var right = RightSort;
        var options = ViewOptions;
        _settingsService.Set(PanelSort.SettingsKey(ContentModelService.Left), left.Serialize());
        _settingsService.Set(PanelSort.SettingsKey(ContentModelService.Right), right.Serialize());
        options.Save(_settingsService);

        foreach (var model in _contentModelService?.ContentModels?.AsSnapshot() ?? [])
        {
            if (model is not ISortableContentModel sortable)
            {
                continue;
            }

            sortable.SetViewOptions(options);
            if (model.Name == ContentModelService.Left)
            {
                sortable.SetSort(left);
            }
            else if (model.Name == ContentModelService.Right)
            {
                sortable.SetSort(right);
            }
        }
    }

    public override void OnInitializeLayout()
    {
        base.OnInitializeLayout();

        const int ReversedWidth = 14;
        var sortWidth = Math.Max(20, ViewSize.Width - ReversedWidth - 2);

        _orderRule.Position = new Position(0, 0);
        _orderRule.ViewSize = new Size(ViewSize.Width, 1);

        _leftSort.Position = new Position(0, 1);
        _leftSort.ViewSize = new Size(sortWidth, 1);
        _leftReversed.Position = new Position(sortWidth + 2, 1);
        _leftReversed.ViewSize = new Size(ReversedWidth, 1);

        _rightSort.Position = new Position(0, 3);
        _rightSort.ViewSize = new Size(sortWidth, 1);
        _rightReversed.Position = new Position(sortWidth + 2, 3);
        _rightReversed.ViewSize = new Size(ReversedWidth, 1);

        _listingRule.Position = new Position(0, 5);
        _listingRule.ViewSize = new Size(ViewSize.Width, 1);

        var row = 6;
        foreach (var checkBox in new[] { _showHidden, _foldersFirst, _naturalOrder })
        {
            checkBox.Position = new Position(0, row++);
            checkBox.ViewSize = new Size(ViewSize.Width, 1);
        }

        _hintLabel.Position = new Position(0, row + 1);
        _hintLabel.ViewSize = new Size(ViewSize.Width, 1);
    }
}
