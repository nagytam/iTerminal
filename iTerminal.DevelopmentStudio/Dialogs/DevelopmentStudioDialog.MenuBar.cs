using iConsole.Controls;
using iConsole.Data;
using iConsole.Localization;

namespace iTerminal.DevelopmentStudio.Dialogs;

/// <summary>
/// The studio's menu bar. Its titles lead the header row. F12 (File), Ctrl+F, Ctrl+B, Ctrl+G, Ctrl+E and Ctrl+T, or a click on a
/// title, opens that title's menu below the header. Every entry runs the method its hot key runs, and shows the hot key beside it, so the menus list every command
/// of the studio: the keys stay as they are, and the menus are a second way to reach them.
/// </summary>
public partial class DevelopmentStudioDialog
{
    /// <summary>The menu bar's titles, in the order the header draws them.</summary>
    public enum MenuBarTitle { File, Build, Debug, View, Tools }

    /// <summary>The header row of the dialog, relative to its top: row 0 is the frame, so the menu titles are on row 1.</summary>
    private const int MenuBarRow = 1;

    /// <summary>The menu titles as the user reads them, in the order the header draws them.</summary>
    public IReadOnlyList<string> MenuTitles => [.. Enum.GetValues<MenuBarTitle>().Select(MenuTitleText)];

    /// <summary>The entries of one menu, with the hot key beside each one (separators included), as the menu lists them.</summary>
    public IReadOnlyList<ContextMenuItem> MenuItemsOf(MenuBarTitle title) => title switch
    {
        MenuBarTitle.File => FileMenuItems(),
        MenuBarTitle.Build => BuildMenuItems(),
        MenuBarTitle.Debug => DebugMenuItems(),
        MenuBarTitle.View => ViewMenuItems(),
        _ => ToolsMenuItems(),
    };

    private static string MenuTitleText(MenuBarTitle title) => title switch
    {
        MenuBarTitle.File => UiText.Get("DevelopmentStudio.MenuFile", "File"),
        MenuBarTitle.Build => UiText.Get("DevelopmentStudio.MenuBuild", "Build"),
        MenuBarTitle.Debug => UiText.Get("DevelopmentStudio.MenuDebug", "Debug"),
        MenuBarTitle.View => UiText.Get("DevelopmentStudio.MenuView", "View"),
        _ => UiText.Get("DevelopmentStudio.MenuTools", "Tools"),
    };

    /// <summary>A title's cell in the header: its text with a space on each side, so a click just beside the word still opens it.</summary>
    private static string MenuTitleCell(MenuBarTitle title) => $" {MenuTitleText(title)} ";

    /// <summary>The column, from the header's left text edge, where a title's cell starts.</summary>
    private static int MenuTitleColumn(MenuBarTitle title) =>
        Enum.GetValues<MenuBarTitle>().TakeWhile(other => other != title).Sum(other => MenuTitleCell(other).Length);

    /// <summary>The title whose cell covers a column of the header, or null past the last title.</summary>
    private static MenuBarTitle? MenuTitleAtColumn(int column)
    {
        foreach (MenuBarTitle title in Enum.GetValues<MenuBarTitle>())
        {
            int start = MenuTitleColumn(title);
            if (column >= start && column < start + MenuTitleCell(title).Length) return title;
        }

        return null;
    }

    /// <summary>
    /// Opens a title's menu below the header. Without a popover service (as in the tests) nothing opens: the menu needs one to
    /// be shown, as the explorer's context menu does.
    /// </summary>
    private async Task OpenMenuBarAsync(MenuBarTitle title)
    {
        if (_popover is null) return;

        var menu = new ContextMenu(_popover, ThemeService);
        foreach (ContextMenuItem item in MenuItemsOf(title)) menu.Items.Add(item);
        await menu.OpenAtAsync(this, new Position(AbsolutePosition.X + 1 + MenuTitleColumn(title), AbsolutePosition.Y + 2));
    }

    /// <summary>A click on a title of the header opens its menu. Clicks anywhere else go on to the dialog's own handling.</summary>
    public override async Task<bool> HandleMouseEventAsync(MouseEvent mouseEvent)
    {
        if (mouseEvent.LeftClicked && mouseEvent.Position.Y == AbsolutePosition.Y + MenuBarRow)
        {
            long column = mouseEvent.Position.X - (AbsolutePosition.X + 1);
            if (column >= 0 && MenuTitleAtColumn((int)column) is { } title)
            {
                await OpenMenuBarAsync(title);
                return true;
            }
        }

        return await base.HandleMouseEventAsync(mouseEvent);
    }

    private static ContextMenuItem AsyncMenuItem(string title, string shortcut, Func<Task> run, bool enabled = true) =>
        new(title)
        {
            Shortcut = shortcut,
            IsEnabled = enabled,
            Execute = run,
        };

    private List<ContextMenuItem> FileMenuItems() =>
    [
        CommandMenuItem(UiText.Get("DevelopmentStudio.MenuOpen", "Open the selected file or project"), "Enter", EnterPressed),
        CommandMenuItem(UiText.Get("DevelopmentStudio.MenuSave", "Save the file"), "Ctrl+S", SaveSource),
        AsyncMenuItem(UiText.Get("DevelopmentStudio.MenuRefresh", "Refresh the solutions and projects"), "Ctrl+R", RefreshAsync),
        ContextMenuItem.Separator(),
        AsyncMenuItem(UiText.Get("DevelopmentStudio.MenuSettings", "Settings..."), "Ctrl+,", OpenSettingsAsync),
        ContextMenuItem.Separator(),
        AsyncMenuItem(UiText.Get("DevelopmentStudio.MenuClose", "Close the studio"), "Ctrl+W", CloseAsync),
    ];

    private List<ContextMenuItem> BuildMenuItems() =>
    [
        CommandMenuItem(UiText.Get("DevelopmentStudio.MenuBuild", "Build"), "B", () => RunSelected("build")),
        CommandMenuItem(UiText.Get("DevelopmentStudio.MenuRun", "Run"), "R", () => RunSelected("run")),
        CommandMenuItem(UiText.Get("DevelopmentStudio.MenuRunNoDebug", "Run without debugging"), "Ctrl+F5", () => RunMainOrSelected("run")),
        CommandMenuItem(UiText.Get("DevelopmentStudio.MenuTest", "Test"), "T", () => RunSelected("test")),
        CommandMenuItem(UiText.Get("DevelopmentStudio.MenuClean", "Clean"), "C", () => RunSelected("clean")),
        AsyncMenuItem(UiText.Get("DevelopmentStudio.MenuCancel", "Cancel the running command"), "X", CancelRunAsync),
        ContextMenuItem.Separator(),
        CommandMenuItem(UiText.Get("DevelopmentStudio.MenuCheck", "Check or uncheck the project"), "Space", ToggleCheckAtCursor),
        CommandMenuItem(UiText.Get("DevelopmentStudio.MenuCheckAll", "Check all projects"), "A", CheckAllProjects),
        CommandMenuItem(UiText.Get("DevelopmentStudio.MenuCheckAllTests", "Check all test projects"), "Shift+T", CheckAllTestProjects),
        ContextMenuItem.Separator(),
        CommandMenuItem(string.Format(UiText.Get("DevelopmentStudio.MenuConfiguration", "Configuration: {0}"), _configuration), "D", ToggleBuildConfiguration),
    ];

    private List<ContextMenuItem> DebugMenuItems() =>
    [
        CommandMenuItem(UiText.Get("DevelopmentStudio.MenuStartDebugging", "Start debugging, or continue"), "F5", StartOrContinueDebugging),
        CommandMenuItem(UiText.Get("DevelopmentStudio.MenuStop", "Stop debugging"), "Shift+F5", StopDebugging, enabled: _session is not null),
        ContextMenuItem.Separator(),
        CommandMenuItem(UiText.Get("DevelopmentStudio.MenuBreakpoint", "Toggle a breakpoint on the cursor line"), "F9", ToggleBreakpointAtCursor),
        CommandMenuItem(UiText.Get("DevelopmentStudio.MenuStepOver", "Step over"), "F10", () => DebugStep(session => session.StepOverAsync()), enabled: _paused),
        CommandMenuItem(UiText.Get("DevelopmentStudio.MenuStepInto", "Step into"), "F11", () => DebugStep(session => session.StepInAsync()), enabled: _paused),
        CommandMenuItem(UiText.Get("DevelopmentStudio.MenuStepOut", "Step out"), "Shift+F11", () => DebugStep(session => session.StepOutAsync()), enabled: _paused),
        ContextMenuItem.Separator(),
        CommandMenuItem(UiText.Get("DevelopmentStudio.MenuWatch", "Watch the word under the cursor"), "W", ToggleWatchAtCursor),
        CommandMenuItem(UiText.Get("DevelopmentStudio.MenuRemoveWatch", "Remove the watch"), "Delete", RemoveWatchAtCursor),
        ContextMenuItem.Separator(),
        CommandMenuItem(UiText.Get("DevelopmentStudio.MenuMainProgram", "Set or clear the main program"), "M", ToggleMainProgramAtCursor),
    ];

    private List<ContextMenuItem> ViewMenuItems() =>
    [
        CommandMenuItem(UiText.Get("DevelopmentStudio.MenuExplorerTab", "Explorer tab: Solution, Tests or Files"), "F6", ToggleExplorerMode),
        CommandMenuItem(UiText.Get("DevelopmentStudio.MenuOutputTab", "Output panel tab"), "F7", CycleOutputTab),
        CommandMenuItem(UiText.Get("DevelopmentStudio.MenuLayout", "Move and resize the panels"), "F8", ToggleLayoutMode),
        ContextMenuItem.Separator(),
        CommandMenuItem(UiText.Get("DevelopmentStudio.MenuExpand", "Expand the solution, project or folder"), "Right", () => ExpandOrCollapseAtCursor(expand: true)),
        CommandMenuItem(UiText.Get("DevelopmentStudio.MenuCollapse", "Collapse the solution, project or folder"), "Left", () => ExpandOrCollapseAtCursor(expand: false)),
        ContextMenuItem.Separator(),
        CommandMenuItem(UiText.Get("DevelopmentStudio.MenuGoToCoverage", "Open the coverage file under the cursor"), "G", OpenCoverageFileAtCursor),
    ];

    private List<ContextMenuItem> ToolsMenuItems() =>
    [
        CommandMenuItem(UiText.Get("DevelopmentStudio.MenuAnalyze", "Analyze code"), "N", RunCodeAnalysis),
        CommandMenuItem(UiText.Get("DevelopmentStudio.MenuMeasureCoverage", "Measure coverage"), "O", RunCoverage),
        CommandMenuItem(UiText.Get("DevelopmentStudio.MenuProfile", "Profile the Python script"), "P", RunProfile),
        ContextMenuItem.Separator(),
        CommandMenuItem(UiText.Get("DevelopmentStudio.MenuSbom", "Write the SBOM"), "S", WriteSbom),
        CommandMenuItem(UiText.Get("DevelopmentStudio.MenuSbomFormat", "Switch the SBOM format: XML or JSON"), "F", ToggleSbomFormat),
        ContextMenuItem.Separator(),
        CommandMenuItem(UiText.Get("DevelopmentStudio.MenuNextSdk", "Next .NET SDK"), "V", CycleSdk),
        AsyncMenuItem(UiText.Get("DevelopmentStudio.MenuSdkManager", "SDKs and JDKs"), "K", OpenSdkManagerAsync),
        AsyncMenuItem(UiText.Get("DevelopmentStudio.MenuNuGet", "NuGet packages"), "U", OpenNuGetManagerAsync),
    ];

    /// <summary>The menu's Cancel entry: stops the running command, as X does.</summary>
    private async Task CancelRunAsync()
    {
        if (_runCts is not null) await _runCts.CancelAsync();
    }
}
