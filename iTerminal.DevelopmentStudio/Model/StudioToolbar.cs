using iConsole.Localization;

namespace iTerminal.DevelopmentStudio.Model;

/// <summary>A command the hotkey toolbar offers. The toolbar's click and the command's key run the same action.</summary>
public enum StudioCommand
{
    Build,
    Clean,
    Debug,
    DebugTests,
    Run,
    RunTests,
    Stop,
    Cancel,
    StepOver,
    StepInto,
    StepOut,
    Breakpoint,
    Save,
    Open,
    Expand,
    Check,
    Main,
    Menu,
    SdkManager,
    Coverage,
    Profile,
    GoToFile,
    RemoveWatch,
    NextTab,
    Refresh,
    Panes,
    Layout,
}

/// <summary>The component that has the focus, which picks the commands the hotkey toolbar offers.</summary>
public enum StudioToolbarContext
{
    SolutionExplorer,
    TestExplorer,
    FileExplorer,
    Editor,
    OutputPanel,
    ErrorsPanel,
    DebugPanel,
    CoveragePanel,
    ProfilePanel,
    Default,
}

/// <summary>The Development studio's hotkey toolbar: the commands each focused component offers, with their captions and keys.</summary>
public static class StudioToolbar
{
    /// <summary>The commands of a context, in the order the toolbar draws them (the first ones are the most used).</summary>
    public static IReadOnlyList<StudioCommand> CommandsFor(StudioToolbarContext context) => context switch
    {
        StudioToolbarContext.SolutionExplorer =>
        [
            StudioCommand.Build, StudioCommand.Clean, StudioCommand.Debug, StudioCommand.Run,
            StudioCommand.DebugTests, StudioCommand.RunTests, StudioCommand.Check, StudioCommand.Main,
        ],
        StudioToolbarContext.TestExplorer =>
        [
            StudioCommand.RunTests, StudioCommand.DebugTests, StudioCommand.Coverage, StudioCommand.Build,
            StudioCommand.Clean, StudioCommand.Cancel,
        ],
        StudioToolbarContext.FileExplorer => [StudioCommand.Open, StudioCommand.Expand, StudioCommand.Refresh, StudioCommand.Menu, StudioCommand.Panes],
        StudioToolbarContext.Editor =>
        [
            StudioCommand.Save, StudioCommand.Breakpoint, StudioCommand.Debug, StudioCommand.StepOver,
            StudioCommand.StepInto, StudioCommand.StepOut, StudioCommand.Stop, StudioCommand.Run,
        ],
        StudioToolbarContext.OutputPanel => [StudioCommand.Cancel, StudioCommand.NextTab],
        StudioToolbarContext.ErrorsPanel => [StudioCommand.Open, StudioCommand.NextTab],
        StudioToolbarContext.DebugPanel =>
        [
            StudioCommand.Debug, StudioCommand.StepOver, StudioCommand.StepInto, StudioCommand.StepOut,
            StudioCommand.Stop, StudioCommand.RemoveWatch, StudioCommand.NextTab,
        ],
        StudioToolbarContext.CoveragePanel => [StudioCommand.Coverage, StudioCommand.GoToFile, StudioCommand.NextTab],
        StudioToolbarContext.ProfilePanel => [StudioCommand.Profile, StudioCommand.NextTab],
        _ => [StudioCommand.Debug, StudioCommand.Stop, StudioCommand.Panes, StudioCommand.Layout, StudioCommand.SdkManager],
    };

    /// <summary>The key that runs the command, as the toolbar shows it beside the caption.</summary>
    public static string KeyOf(StudioCommand command) => command switch
    {
        StudioCommand.Build => "B",
        StudioCommand.Clean => "C",
        StudioCommand.Debug => "F5",
        StudioCommand.DebugTests => "Ctrl+Shift+F5",
        StudioCommand.Run => "Ctrl+F5",
        StudioCommand.RunTests => "T",
        StudioCommand.Stop => "Shift+F5",
        StudioCommand.Cancel => "X",
        StudioCommand.StepOver => "F10",
        StudioCommand.StepInto => "F11",
        StudioCommand.StepOut => "Shift+F11",
        StudioCommand.Breakpoint => "F9",
        StudioCommand.Save => "Ctrl+S",
        StudioCommand.Open => "Enter",
        StudioCommand.Expand => "Left/Right",
        StudioCommand.Check => "Space",
        StudioCommand.Main => "M",
        StudioCommand.Menu => "Menu",
        StudioCommand.SdkManager => "K",
        StudioCommand.Coverage => "O",
        StudioCommand.Profile => "P",
        StudioCommand.GoToFile => "G",
        StudioCommand.RemoveWatch => "Delete",
        StudioCommand.NextTab => "F7",
        StudioCommand.Refresh => "Ctrl+R",
        StudioCommand.Panes => "F6",
        StudioCommand.Layout => "F8",
        _ => string.Empty,
    };

    /// <summary>The command's caption in the current language.</summary>
    public static string CaptionOf(StudioCommand command) => command switch
    {
        StudioCommand.Build => UiText.Get("DevelopmentStudio.Toolbar.Build", "Build"),
        StudioCommand.Clean => UiText.Get("DevelopmentStudio.Toolbar.Clean", "Clean"),
        StudioCommand.Debug => UiText.Get("DevelopmentStudio.Toolbar.Debug", "Debug"),
        StudioCommand.DebugTests => UiText.Get("DevelopmentStudio.Toolbar.DebugTests", "Debug tests"),
        StudioCommand.Run => UiText.Get("DevelopmentStudio.Toolbar.Run", "Run"),
        StudioCommand.RunTests => UiText.Get("DevelopmentStudio.Toolbar.RunTests", "Run tests"),
        StudioCommand.Stop => UiText.Get("DevelopmentStudio.Toolbar.Stop", "Stop"),
        StudioCommand.Cancel => UiText.Get("DevelopmentStudio.Toolbar.Cancel", "Cancel"),
        StudioCommand.StepOver => UiText.Get("DevelopmentStudio.Toolbar.StepOver", "Step over"),
        StudioCommand.StepInto => UiText.Get("DevelopmentStudio.Toolbar.StepInto", "Step into"),
        StudioCommand.StepOut => UiText.Get("DevelopmentStudio.Toolbar.StepOut", "Step out"),
        StudioCommand.Breakpoint => UiText.Get("DevelopmentStudio.Toolbar.Breakpoint", "Breakpoint"),
        StudioCommand.Save => UiText.Get("DevelopmentStudio.Toolbar.Save", "Save"),
        StudioCommand.Open => UiText.Get("DevelopmentStudio.Toolbar.Open", "Open"),
        StudioCommand.Expand => UiText.Get("DevelopmentStudio.Toolbar.Expand", "Expand/collapse"),
        StudioCommand.Check => UiText.Get("DevelopmentStudio.Toolbar.Check", "Check"),
        StudioCommand.Main => UiText.Get("DevelopmentStudio.Toolbar.Main", "Main program"),
        StudioCommand.Menu => UiText.Get("DevelopmentStudio.Toolbar.Menu", "Menu"),
        StudioCommand.SdkManager => UiText.Get("DevelopmentStudio.Toolbar.SdkManager", "SDKs"),
        StudioCommand.Coverage => UiText.Get("DevelopmentStudio.Toolbar.Coverage", "Coverage"),
        StudioCommand.Profile => UiText.Get("DevelopmentStudio.Toolbar.Profile", "Profile"),
        StudioCommand.GoToFile => UiText.Get("DevelopmentStudio.Toolbar.GoToFile", "Open file"),
        StudioCommand.RemoveWatch => UiText.Get("DevelopmentStudio.Toolbar.RemoveWatch", "Remove watch"),
        StudioCommand.NextTab => UiText.Get("DevelopmentStudio.Toolbar.NextTab", "Next tab"),
        StudioCommand.Refresh => UiText.Get("DevelopmentStudio.Toolbar.Refresh", "Refresh"),
        StudioCommand.Panes => UiText.Get("DevelopmentStudio.Toolbar.Panes", "Panes"),
        StudioCommand.Layout => UiText.Get("DevelopmentStudio.Toolbar.Layout", "Layout"),
        _ => string.Empty,
    };
}
