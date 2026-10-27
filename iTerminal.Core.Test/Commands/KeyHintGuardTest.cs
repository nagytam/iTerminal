using System.Reflection;
using iConsole.Controls;
using iTerminal.Core.Application;
using iTerminal.Core.Commands;
using iTerminal.Core.Controls.Contents;

namespace iTerminal.Core.Test.Commands;

/// <summary>Guards that keep the help overlay (F1) honest as controls and dialogs are added.</summary>
[TestClass]
public class KeyHintGuardTest
{
    /// <summary>
    /// Controls that take keys but are not listed by the overlay on purpose: the panels and the terminal pass nearly every
    /// key to what they show, so their keys are the menus' and the shells'.
    /// </summary>
    private static readonly HashSet<string> ExemptTypes =
    [
        "ContentBrowser", "TerminalPanel", "TerminalWindow", "TerminalScreen", "PopUpWindow", "MenuBar", "Window", "Dialog",
    ];

    [TestMethod]
    public void EveryControlThatHandlesKeys_ListsThem_OrIsExempt()
    {
        List<string> silent = [];
        foreach (Assembly assembly in new[] { typeof(Control).Assembly, typeof(iTerminal.Core.Dialogs.SortDialog).Assembly })
        {
            foreach (Type type in assembly.GetExportedTypes().Where(t => typeof(Control).IsAssignableFrom(t) && !t.IsAbstract))
            {
                MethodInfo? handles = type.GetMethod("HandleKeyEventAsync", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                if (handles is null || ExemptTypes.Contains(type.Name))
                {
                    continue;
                }

                bool lists = type.GetMethod("GetKeyHints", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly) is not null;
                if (!lists)
                {
                    silent.Add(type.FullName!);
                }
            }
        }

        Assert.AreEqual(0, silent.Count, "These handle keys but list none in the help overlay: " + string.Join(", ", silent));
    }

    [TestMethod]
    public void ApplicationInfo_TitleNamesTheApplicationAndItsVersion()
    {
        Assert.IsFalse(string.IsNullOrWhiteSpace(ApplicationInfo.Version));
        Assert.AreEqual("iTerminal " + ApplicationInfo.Version, ApplicationInfo.Title);
    }

    [TestMethod]
    public async Task CycleBottomBarModeCommand_WithNoPanels_DoesNothing_AndIsOnCtrlB()
    {
        CycleBottomBarModeCommand command = new((Content?)null);

        await command.ExecuteAsync(null);

        Assert.AreEqual(System.ConsoleKey.B, command.Key!.ConsoleKey);
        Assert.IsTrue(command.Key.Control);
    }
}
