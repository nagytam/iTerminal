using iConsole.Controls;
using iConsole.Controls.Buttons;
using iConsole.Localization;
using iConsole.Services;
using iTerminal.Core.Services;
using iTerminal.DevelopmentStudio.Model;
using iTerminal.DevelopmentStudio.Services;

namespace iTerminal.DevelopmentStudio.Dialogs;

/// <summary>The panel layout: the button puts the panels back where a new studio starts them. F8 moves them the same way.</summary>
public class StudioLayoutSettingsTabPage : StudioSettingsTabPage
{
    private readonly Button _restore;
    private readonly Label _hintLabel;

    public StudioLayoutSettingsTabPage(ISettingsService settings, IThemeService themeService)
        : base(UiText.Get("DevelopmentStudioSettings.TabLayout", "4-Layout"), settings, themeService)
    {
        HotConsoleKey = ConsoleKey.D4;
        _restore = new Button(UiText.Get("DevelopmentStudioSettings.RestoreLayout", "Restore the default layout"), new StudioActionCommand(UiText.Get("DevelopmentStudioSettings.RestoreLayout", "Restore the default layout"), RestoreDefaultLayout), themeService)
        {
            CanFocus = true,
        };
        _hintLabel = new Label(UiText.Get("DevelopmentStudioSettings.LayoutHint", "Moves the panels back to the arrangement a new studio starts with. F8 moves them too."), themeService);

        AddControl(_restore);
        AddControl(_hintLabel);
    }

    /// <summary>Saves the default arrangement of the panels. The open studio takes it when this dialog closes.</summary>
    public void RestoreDefaultLayout() => Settings.Set(StudioSettings.LayoutKey, StudioPanelLayout.Default().Serialize());

    protected override void OnLoad()
    {
        // The page has no values to show: its one control is an action.
    }

    protected override void Save()
    {
        // Nothing is kept by the page itself; the button saves the layout when it is pressed.
    }

    public override void OnInitializeLayout()
    {
        base.OnInitializeLayout();
        Place(_restore, 0, 1, ViewSize.Width);
        Place(_hintLabel, 0, 3, ViewSize.Width);
    }
}
