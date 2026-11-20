using iConsole.Data;
using iTerminal.Core.Commands;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace iTerminal.Core.Commands.Test;

/// <summary>Two features on one chord would leave one of them unreachable: the chords of these global commands must differ.</summary>
[TestClass]
public class HotKeyCollisionTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void TheDuplicateFinderAndTheSignaturesHaveDifferentChords()
    {
        var duplicates = new DuplicateFinderDialogCommand(null!, Mock.Of<iConsole.Interfaces.IContentModelService>());
        var signatures = new SignatureDialogCommand(null!, Mock.Of<iConsole.Interfaces.IContentModelService>());

        Assert.AreEqual(ConsoleKey.F6, duplicates.Key!.ConsoleKey);
        Assert.AreEqual(ConsoleKey.F7, signatures.Key!.ConsoleKey);
        Assert.AreNotEqual(duplicates.Key.ConsoleKey, signatures.Key.ConsoleKey);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheHashManifestChordIsShiftAltF3_AndNotOneOfTheOthers()
    {
        var manifest = new HashManifestDialogCommand(null!, Mock.Of<iConsole.Interfaces.IContentModelService>());
        var signatures = new SignatureDialogCommand(null!, Mock.Of<iConsole.Interfaces.IContentModelService>());

        Assert.AreEqual(new Key(ConsoleKey.F3, true, true, false), manifest.Key);
        Assert.AreNotEqual(signatures.Key, manifest.Key);
    }

    /// <summary>The calendar is a global command on Ctrl+D; git sync is offered in the panel and must not share it.</summary>
    [Timeout(60_000)]
    [TestMethod]
    public void GitSyncAndTheCalendarHaveDifferentChords()
    {
        var sync = new GitSyncCommand(
            new iTerminal.Core.Dialogs.Git.GitSyncDialog(Mock.Of<iTerminal.Core.Services.Git.IGitService>(), Mock.Of<iConsole.Interfaces.IContentModelService>(), Mock.Of<iConsole.Services.IThemeService>()),
            Mock.Of<iConsole.Interfaces.IContentModelService>());
        var calendar = new CalendarDialogCommand(null!);

        Assert.AreEqual(new Key(ConsoleKey.G, true, false, true), sync.Key);
        Assert.AreEqual(new Key(ConsoleKey.D, false, false, true), calendar.Key);
    }

    /// <summary>
    /// Print is Ctrl+P: not Ctrl+Shift+P, which Windows Terminal keeps for its own command palette. Alt+P walks the command
    /// history, so the two must stay different chords.
    /// </summary>
    [Timeout(60_000)]
    [TestMethod]
    public void PrintIsPlainCtrlP_NotTheHostsOrTheHistorysChord()
    {
        var print = new PrintDialogCommand(null!, Mock.Of<iConsole.Interfaces.IContentModelService>());

        Assert.AreEqual(new Key(ConsoleKey.P, false, false, true), print.Key);
        Assert.AreNotEqual(new Key(ConsoleKey.P, true, false, true), print.Key, "Ctrl+Shift+P is the terminal host's");
        Assert.AreNotEqual(new Key(ConsoleKey.P, false, true, false), print.Key, "Alt+P walks the command history");

    }

    /// <summary>
    /// Ctrl+Shift+X is the password manager's chord that always reaches it (Ctrl+X cuts in a file panel), so Add evidence,
    /// which a focused file panel runs before the menu bar's commands, must not take it.
    /// </summary>
    [Timeout(60_000)]
    [TestMethod]
    public void AddEvidenceAndThePasswordManagerHaveDifferentChords()
    {
        var passwords = new PasswordManagerDialogCommand(null!);
        var addEvidence = new iTerminal.Core.Commands.Evidence.AddEvidenceCommand(
            Mock.Of<iConsole.Interfaces.IContentModelService>(),
            Mock.Of<iTerminal.Core.Services.Evidence.IEvidenceNotebookService>(),
            Mock.Of<iConsole.Services.IPromptService>(),
            Mock.Of<iTerminal.Core.Services.ISettingsService>(),
            Mock.Of<iConsole.Services.ITextClipboardService>(),
            Mock.Of<iTerminal.Core.Speech.IMicrophoneCaptureFactory>(),
            null!);

        Assert.AreEqual(new Key(ConsoleKey.X, true, false, true), passwords.AlternateKey);
        Assert.AreEqual(new Key(ConsoleKey.Delete, true, false, true), addEvidence.Key);
        Assert.AreNotEqual(passwords.AlternateKey, addEvidence.Key);
        Assert.AreNotEqual(passwords.Key, addEvidence.Key);
    }

    /// <summary>
    /// Copy Path is Ctrl+Shift+Insert: Ctrl+Shift+P is the terminal host's command palette, and Ctrl+Shift+Y sends the same
    /// byte as Ctrl+Y (ToDo list). Ctrl+Insert and Shift+Insert are Copy and Paste, so this chord stays clear of both.
    /// </summary>
    [Timeout(60_000)]
    [TestMethod]
    public void CopyPathIsCtrlShiftInsert_NotTheHostsPalette()
    {
        var copyPath = new CopyPathCommand(Mock.Of<iConsole.Interfaces.IContentModelService>());

        Assert.AreEqual(new Key(ConsoleKey.Insert, true, false, true), copyPath.Key);
        Assert.AreNotEqual(new Key(ConsoleKey.P, true, false, true), copyPath.Key, "Ctrl+Shift+P is the terminal host's");
    }
}
