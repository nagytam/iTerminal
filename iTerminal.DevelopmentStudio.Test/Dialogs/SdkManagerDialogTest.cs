using iTerminal.Core.Model.Dotnet;
using iTerminal.Core.Services.Sdk;
using iTerminal.DevelopmentStudio.Dialogs;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.DevelopmentStudio.Test.Dialogs;

[TestClass]
public class SdkManagerDialogTest
{
    private static readonly string Jdk21Home = Path.Combine("jdks", "jdk-21.0.13.1+2", "jdk-21.0.13.1+2");

    /// <summary>Two installs (a .NET SDK and a JDK), and two downloads: the newest .NET SDK of a channel and a JDK that is not the newest.</summary>
    private static FakeSdkManager Manager() => new()
    {
        DotNet = [new DotnetSdk("10.0.404", Path.Combine("dotnet", "10.0.404"))],
        Jdks = [new InstalledJdk("jdk-21.0.13.1+2", Path.Combine("jdks", "jdk-21.0.13.1+2"), Jdk21Home)],
        Available =
        [
            new AvailableSdk(SdkKind.DotNet, "11.0.100", "11.0", "https://example.test/11", "11.tar.gz", new string('a', 128), System.Security.Cryptography.HashAlgorithmName.SHA512, null, IsLatest: true),
            new AvailableSdk(SdkKind.DotNet, "10.0.404", "10.0", "https://example.test/10", "10.tar.gz", new string('b', 128), System.Security.Cryptography.HashAlgorithmName.SHA512, null, IsLatest: true),
            new AvailableSdk(SdkKind.Jdk, "jdk-25.0.1+8", "25", "https://example.test/25", "25.tar.gz", new string('c', 64), System.Security.Cryptography.HashAlgorithmName.SHA256, 200, IsLatest: false),
        ],
    };

    private static async Task<(SdkManagerDialog Dialog, FakeSdkManager Manager)> OpenAsync(FakeSdkManager manager)
    {
        var dialog = new SdkManagerDialog(manager, TestTheme());
        await ShowAsync(dialog);
        await WaitForRowsAsync(dialog);
        return (dialog, manager);
    }

    private static async Task WaitForRowsAsync(SdkManagerDialog dialog) =>
        await WaitUntilAsync(() => { dialog.OnIdle(); return dialog.ListedRows().Count > 0; }, "the SDK list to be read");

    /// <summary>Moves the cursor down until the row of the given kind and version is selected.</summary>
    private static async Task SelectAsync(SdkManagerDialog dialog, Func<SdkRow, bool> match)
    {
        for (int step = 0; step < 10 && dialog.SelectedRow is not null && !match(dialog.SelectedRow); step++)
        {
            await dialog.HandleKeyEventAsync(Key(ConsoleKey.DownArrow));
        }

        Assert.IsTrue(dialog.SelectedRow is not null && match(dialog.SelectedRow), "the row is on the list");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheListShowsTheInstalledSdksFirstThenWhatCanBeDownloaded()
    {
        var (dialog, _) = await OpenAsync(Manager());

        CollectionAssert.AreEqual(new[]
        {
            ".NET SDK 10.0.404",
            "Java JDK jdk-21.0.13.1+2",
            ".NET SDK 11.0.100",
            "Java JDK jdk-25.0.1+8",
        }, dialog.ListedRows().Select(row => $"{(row.Kind == SdkKind.DotNet ? ".NET SDK" : "Java JDK")} {row.Version}").ToArray(),
            "an SDK that is installed is not offered again");
        Assert.AreEqual("Installed", dialog.ListedRows()[1].State, "a JDK that is not the one in use says so");
        Assert.AreEqual("Available, the latest", dialog.ListedRows()[2].State);
        Assert.AreEqual("Available", dialog.ListedRows()[3].State);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AnSdkTheSystemHasIsListedAsInstalledAndNotOfferedForDownload()
    {
        // The system's 11.0.100 is installed in /usr/lib/dotnet beside 10.0.115, not by iTerminal: it must not show as a download.
        var manager = new FakeSdkManager
        {
            Available =
            [
                new AvailableSdk(SdkKind.DotNet, "11.0.100", "11.0", "https://example.test/11", "11.tar.gz", new string('a', 128), System.Security.Cryptography.HashAlgorithmName.SHA512, null, IsLatest: true),
                new AvailableSdk(SdkKind.DotNet, "9.0.300", "9.0", "https://example.test/9", "9.tar.gz", new string('d', 128), System.Security.Cryptography.HashAlgorithmName.SHA512, null, IsLatest: true),
            ],
        };
        var system = new List<DotnetSdk> { new("11.0.100", "/usr/lib/dotnet"), new("10.0.115", "/usr/lib/dotnet") };
        var dialog = new SdkManagerDialog(manager, TestTheme(), () => system);
        await ShowAsync(dialog);
        await WaitForRowsAsync(dialog);

        CollectionAssert.AreEqual(new[]
        {
            ".NET SDK 11.0.100",
            ".NET SDK 10.0.115",
            ".NET SDK 9.0.300",
        }, dialog.ListedRows().Select(row => $"{(row.Kind == SdkKind.DotNet ? ".NET SDK" : "Java JDK")} {row.Version}").ToArray());
        Assert.AreEqual("Installed", dialog.ListedRows()[0].State);
        Assert.AreEqual("Installed", dialog.ListedRows()[1].State);
        Assert.IsNull(dialog.ListedRows()[0].Folder, "iTerminal does not own the system's SDK, so it cannot remove it");

        await SelectAsync(dialog, row => row.Version == "11.0.100");
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Delete));
        Assert.IsFalse(dialog.Status.Contains("(y/n)", StringComparison.Ordinal), "a system SDK is not offered for removal");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task EnterInstallsTheDownloadUnderTheCursor()
    {
        var (dialog, manager) = await OpenAsync(Manager());
        await SelectAsync(dialog, row => row.Available is { Version: "jdk-25.0.1+8" });

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter));

        await WaitUntilAsync(() => { dialog.OnIdle(); return manager.Installs.Count == 1; }, "the install to start");
        Assert.AreEqual("jdk-25.0.1+8", manager.Installs[0].Version);
        await WaitUntilAsync(() => { dialog.OnIdle(); return dialog.Status.Contains("is installed", StringComparison.Ordinal); }, "the install to finish");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task EnterOnAnInstalledJdkMakesItTheJavaInUse()
    {
        var (dialog, manager) = await OpenAsync(Manager());
        await SelectAsync(dialog, row => row.Kind == SdkKind.Jdk && row.Folder is not null);

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter));

        await WaitUntilAsync(() => { dialog.OnIdle(); return manager.ActiveJavaHome == Jdk21Home; }, "the JDK to be chosen");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task DeleteAsksForYAndOnlyThenRemovesTheInstall()
    {
        var (dialog, manager) = await OpenAsync(Manager());
        await SelectAsync(dialog, row => row.Kind == SdkKind.Jdk && row.Folder is not null);

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Delete));
        Assert.IsTrue(dialog.Status.Contains("(y/n)", StringComparison.Ordinal), "the removal waits for a y or n");
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.N, 'n'));
        Assert.AreEqual(0, manager.Removed.Count, "n keeps the install");

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Delete));
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Y, 'y'));

        await WaitUntilAsync(() => { dialog.OnIdle(); return manager.Removed.Count == 1; }, "the removal to run");
        Assert.AreEqual(Path.Combine("jdks", "jdk-21.0.13.1+2"), manager.Removed[0]);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task EscClosesTheDialogAndTellsTheStudio()
    {
        var (dialog, _) = await OpenAsync(Manager());
        bool closed = false;
        dialog.Closed = () => closed = true;

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Escape));

        Assert.IsTrue(closed);
    }
}
