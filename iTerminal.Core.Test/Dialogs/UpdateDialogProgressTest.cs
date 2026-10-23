using iConsole.Controls;
using iConsole.Data;
using iConsole.Test.Core;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Services.RuntimeDependencies;
using iTerminal.Core.Services.Updates;
using static iTerminal.Core.Test.Helper.UiTestHelper;
using StringAssert = Microsoft.VisualStudio.TestTools.UnitTesting.StringAssert;

namespace iTerminal.Core.Test.Dialogs;

/// <summary>The update dialog's progress bar, drawn from what the download says in the service's log.</summary>
[TestClass]
public class UpdateDialogProgressTest
{
    private sealed class FakeUpdateService : IUpdateService
    {
        private readonly List<string> _log = [];

        public string CurrentVersion => "1.5.0";

        public UpdateInfo? Available { get; set; } = new("2.0.0", new GitHubAsset("iTerminal-2.0.msi", "https://example.invalid/iTerminal-2.0.msi", 1234, null));

        public bool IsSkipped => false;

        public bool CanInstall => true;

        public bool IsBusy { get; set; }

        public IReadOnlyList<string> Log => _log;

        public string ReleasesAddress => "https://example.invalid/releases";

        public void AddLog(string line) => _log.Add(line);

        public Task<UpdateInfo?> CheckAsync(CancellationToken cancellationToken) => Task.FromResult(Available);

        public void Skip()
        {
        }

        public Task<bool> InstallAsync(CancellationToken cancellationToken) => Task.FromResult(true);
    }

    private static async Task<(UpdateDialog Dialog, Control Root)> OpenAsync(FakeUpdateService service)
    {
        UpdateDialog dialog = new(service, TestTheme());
        Control root = await ShowAsync(dialog);
        return (dialog, root);
    }

    [TestMethod]
    public void ParseProgress_ReadsTheNewestDownloadLine()
    {
        UpdateDialog.UpdateProgress? progress = UpdateDialog.ParseProgress(
            ["Downloading iTerminal-2.0.msi: 1.0 of 10.0 MB", "Downloading iTerminal-2.0.msi: 2.5 of 10.0 MB"]);

        Assert.IsNotNull(progress);
        Assert.AreEqual(0.25, progress.Value.Fraction, 1e-9);
        Assert.AreEqual("2.5 of 10.0 MB", progress.Value.Label);
    }

    [TestMethod]
    public void ParseProgress_AcceptsADecimalComma()
    {
        UpdateDialog.UpdateProgress? progress = UpdateDialog.ParseProgress(["Downloading a.msi: 5,0 of 20,0 MB"]);

        Assert.AreEqual(0.25, progress!.Value.Fraction, 1e-9);
        Assert.AreEqual("5,0 of 20,0 MB", progress.Value.Label);
    }

    [TestMethod]
    public void ParseProgress_IsCompleteOnceDownloadedOrTheInstallerStarts()
    {
        Assert.AreEqual(1.0, UpdateDialog.ParseProgress(["Downloading a.msi: 1.0 of 10.0 MB", "Downloaded a.msi (10.0 MB, checksum verified)"])!.Value.Fraction);
        Assert.AreEqual(1.0, UpdateDialog.ParseProgress(["Starting the installer. iTerminal closes so its files can be replaced."])!.Value.Fraction);
    }

    [TestMethod]
    public void ParseProgress_HasNoFraction_ForUnknownSizeNothingYetOrAFailure()
    {
        Assert.IsNull(UpdateDialog.ParseProgress([]));
        Assert.IsNull(UpdateDialog.ParseProgress(["Downloading a.msi: 3.0 MB"]));
        Assert.IsNull(UpdateDialog.ParseProgress(["Downloading a.msi: 3.0 of 0.0 MB"]));
        Assert.IsNull(UpdateDialog.ParseProgress(["Downloading a.msi: 3.0 of 10.0 MB", "The update could not be installed: no space"]));
        Assert.IsNull(UpdateDialog.ParseProgress(["something else entirely"]));
    }

    [TestMethod]
    public void ParseProgress_NeverExceedsTheWhole()
    {
        Assert.AreEqual(1.0, UpdateDialog.ParseProgress(["Downloading a.msi: 12.0 of 10.0 MB"])!.Value.Fraction);
    }

    [TestMethod]
    public void ProgressBar_FillsInProportion_AndFallsBackToAPercentageWhenNarrow()
    {
        (string filled, string empty, string tail) = UpdateDialog.ProgressBar(new UpdateDialog.UpdateProgress(0.5, "5.0 of 10.0 MB"), 72);
        Assert.AreEqual(15, filled.Count(c => c == '█'));
        Assert.AreEqual(15, empty.Count(c => c == '░'));
        Assert.IsTrue(filled.StartsWith('[') && empty.EndsWith(']'));
        StringAssert.Contains(tail, " 50%");
        StringAssert.Contains(tail, "5.0 of 10.0 MB");

        (string narrowFilled, string narrowEmpty, string narrowTail) = UpdateDialog.ProgressBar(new UpdateDialog.UpdateProgress(0.5, "5.0 of 10.0 MB"), 12);
        Assert.AreEqual(string.Empty, narrowFilled);
        Assert.AreEqual(string.Empty, narrowEmpty);
        Assert.AreEqual("50%", narrowTail);
    }

    [TestMethod]
    public async Task WhileDownloading_TheDialogDrawsABarFromTheLog_InPlaceOfWorking()
    {
        FakeUpdateService service = new() { IsBusy = true };
        service.AddLog("Downloading iTerminal-2.0.msi: 5.0 of 10.0 MB");
        (UpdateDialog dialog, Control root) = await OpenAsync(service);

        ScreenBuffer screen = Render(root);
        AssertScreenContains(screen, "█");
        AssertScreenContains(screen, "░");
        AssertScreenContains(screen, " 50%");
        AssertScreenContains(screen, "5.0 of 10.0 MB");
        AssertScreenDoesNotContain(screen, "Working...");

        service.AddLog("Downloading iTerminal-2.0.msi: 10.0 of 10.0 MB");
        dialog.OnIdle();
        AssertScreenContains(Render(root), "100%");
    }

    [TestMethod]
    public async Task BusyWithoutAnyProgressYet_StillSaysWorking_AndWhenIdleNoBarShows()
    {
        FakeUpdateService busy = new() { IsBusy = true };
        (_, Control root) = await OpenAsync(busy);
        AssertScreenContains(Render(root), "Working...");

        FakeUpdateService idle = new();
        idle.AddLog("Downloading a.msi: 5.0 of 10.0 MB");
        (_, Control idleRoot) = await OpenAsync(idle);
        AssertScreenDoesNotContain(Render(idleRoot), "50%");
    }
}
