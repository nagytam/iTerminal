using iConsole.Controls;
using iConsole.Controls.Buttons;
using iConsole.Localization;
using iConsole.Services;
using iTerminal.Core.Services;
using iTerminal.DevelopmentStudio.Services;

namespace iTerminal.DevelopmentStudio.Dialogs;

/// <summary>
/// The code analysis time estimates: how long each item's last analysis took, which the next one's estimate is. The button forgets
/// them, so the studio shows no estimate until the next analysis of each item.
/// </summary>
public class StudioAnalysisSettingsTabPage : StudioSettingsTabPage
{
    private readonly Button _forget;
    private readonly Label _hintLabel;

    public StudioAnalysisSettingsTabPage(ISettingsService settings, IThemeService themeService)
        : base(UiText.Get("DevelopmentStudioSettings.TabAnalysis", "5-Analysis"), settings, themeService)
    {
        HotConsoleKey = ConsoleKey.D5;
        _forget = new Button(UiText.Get("DevelopmentStudioSettings.ForgetDurations", "Forget the analysis time estimates"), new StudioActionCommand(UiText.Get("DevelopmentStudioSettings.ForgetDurations", "Forget the analysis time estimates"), ForgetDurations), themeService)
        {
            CanFocus = true,
        };
        _hintLabel = new Label(UiText.Get("DevelopmentStudioSettings.AnalysisHint", "A code analysis shows how long the last one of its item took. Forgetting it shows no estimate until the next analysis."), themeService);

        AddControl(_forget);
        AddControl(_hintLabel);
    }

    /// <summary>Saves an empty set of estimates.</summary>
    public void ForgetDurations() => Settings.Set(StudioSettings.AnalysisDurationsKey, StudioSettings.EncodeDurations(new Dictionary<string, double>()));

    protected override void OnLoad()
    {
        // The page has no values to show: its one control is an action.
    }

    protected override void Save()
    {
        // Nothing is kept by the page itself; the button saves the estimates when it is pressed.
    }

    public override void OnInitializeLayout()
    {
        base.OnInitializeLayout();
        Place(_forget, 0, 1, ViewSize.Width);
        Place(_hintLabel, 0, 3, ViewSize.Width);
    }
}
