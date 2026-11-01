using iConsole.Services;
using iTerminal.Core.Dialogs.PackageManager;
using iTerminal.Core.Model.PackageManager;
using iTerminal.Core.Services.PackageManager;
using Moq;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Test.Dialogs.PackageManager;

/// <summary>
/// The package manager dialog: installed list, search, and confirmed install/uninstall. Every query
/// and every install runs on a background task, which the fake below can hold open or fail.
/// </summary>
[TestClass]
public class PackageManagerDialogTest
{
    private sealed class FakePackageManagerService : IPackageManagerService
    {
        public List<PackageInfo> Installed { get; } = [];
        public List<PackageInfo> Available { get; } = [];
        private readonly List<string> _calls = [];

        /// <summary>A snapshot: the dialog records calls from background tasks.</summary>
        public List<string> Calls
        {
            get
            {
                lock (_calls)
                {
                    return [.. _calls];
                }
            }
        }

        private void Record(string call)
        {
            lock (_calls)
            {
                _calls.Add(call);
            }
        }
        public TaskCompletionSource? InstalledGate { get; set; }
        public Exception? InstalledFailure { get; set; }
        public string? InstalledError { get; set; }
        public TaskCompletionSource? InstallGate { get; set; }
        public int InstallExitCode { get; set; }

        public string DisplayName => "FakeGet";

        public async Task<PackageQueryResult> GetInstalledPackagesAsync(CancellationToken cancellationToken = default)
        {
            Record("list");
            if (InstalledGate is not null)
            {
                await InstalledGate.Task;
            }

            if (InstalledFailure is not null)
            {
                throw InstalledFailure;
            }

            return InstalledError is not null ? PackageQueryResult.Fail(InstalledError) : PackageQueryResult.Ok([.. Installed]);
        }

        public Task<PackageQueryResult> SearchPackagesAsync(string query, CancellationToken cancellationToken = default)
        {
            Record($"search:{query}");
            return Task.FromResult(PackageQueryResult.Ok([.. Available.Where(p => p.Name.Contains(query, StringComparison.OrdinalIgnoreCase))]));
        }

        public async Task<int> InstallAsync(string packageId, Action<string> onOutputLine, CancellationToken cancellationToken = default)
        {
            Record($"install:{packageId}");
            onOutputLine("Successfully installed");
            if (InstallGate is not null)
            {
                try
                {
                    await InstallGate.Task.WaitAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return -1;
                }
            }

            return InstallExitCode;
        }

        public Task<int> UninstallAsync(string packageId, Action<string> onOutputLine, CancellationToken cancellationToken = default)
        {
            Record($"uninstall:{packageId}");
            return Task.FromResult(0);
        }
    }

    private static PackageInfo Package(string name, string? available = null) => new(name, $"Vendor.{name}", "1.0", available, "fake");

    private static PackageManagerDialog Create(FakePackageManagerService service)
    {
        var dateTime = new Mock<IDateTimeProviderService>();
        dateTime.SetupGet(d => d.Now).Returns(DateTime.Now);
        return new PackageManagerDialog(service, new Mock<IBeepService>().Object, dateTime.Object, TestTheme());
    }

    private static async Task<PackageManagerDialog> OpenAsync(FakePackageManagerService service, bool waitForList = true)
    {
        var dialog = Create(service);
        await ShowAsync(dialog);
        if (waitForList)
        {
            await WaitUntilAsync(() => !dialog.IsLoadingForTest, "the installed list to load");
        }

        return dialog;
    }

    private static async Task SearchAsync(PackageManagerDialog dialog, string query)
    {
        await dialog.HandleKeyEventAsync(Char('s'));
        await TypeAsync(dialog, query);
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter, '\r'));
        await WaitUntilAsync(() => !dialog.IsLoadingForTest, "the search to finish");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ListsTheInstalledPackages()
    {
        var service = new FakePackageManagerService();
        service.Installed.AddRange([Package("Git"), Package("Node", available: "2.0")]);
        var dialog = await OpenAsync(service);

        var screen = Render(dialog);

        AssertScreenContains(screen, "Installed (2)");
        AssertScreenContains(screen, "Git  [Vendor.Git]  1.0");
        AssertScreenContains(screen, "1.0 -> 2.0", "an available upgrade is shown");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task WhileLoadingTheListSaysSoRatherThanLookingEmpty()
    {
        // An empty "Installed (0)" during the first load read as "nothing is installed".
        var service = new FakePackageManagerService { InstalledGate = new TaskCompletionSource() };
        var dialog = await OpenAsync(service, waitForList: false);

        var screen = Render(dialog);

        AssertScreenContains(screen, "Loading installed packages from FakeGet");
        AssertScreenContains(screen, "(loading…)");
        service.InstalledGate.SetResult();
        await WaitUntilAsync(() => !dialog.IsLoadingForTest, "the list to load");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AnEmptyInstalledListSaysSo()
    {
        var dialog = await OpenAsync(new FakePackageManagerService());

        AssertScreenContains(Render(dialog), "No packages installed.");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AQueryThatThrowsStillEndsTheLoadingState()
    {
        var service = new FakePackageManagerService { InstalledFailure = new InvalidOperationException("winget missing") };
        var dialog = await OpenAsync(service);

        Assert.IsFalse(dialog.IsLoadingForTest);
        Assert.AreEqual("Error: winget missing", dialog.MessageForTest);
        AssertScreenContains(Render(dialog), "r retries");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AFailedQueryIsShownInTheHeader()
    {
        var dialog = await OpenAsync(new FakePackageManagerService { InstalledError = "source unavailable" });

        AssertScreenContains(Render(dialog), "Error: source unavailable");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task SearchShowsMatchesAndIReturnsToTheInstalledList()
    {
        var service = new FakePackageManagerService();
        service.Installed.Add(Package("Git"));
        service.Available.AddRange([Package("Python"), Package("PyCharm")]);
        var dialog = await OpenAsync(service);

        await SearchAsync(dialog, "py");

        AssertScreenContains(Render(dialog), "Search: py (2)");

        await dialog.HandleKeyEventAsync(Char('i'));

        AssertScreenContains(Render(dialog), "Installed (1)");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ASearchWithNoMatchesSaysSo()
    {
        var dialog = await OpenAsync(new FakePackageManagerService());

        await SearchAsync(dialog, "nothing");

        AssertScreenContains(Render(dialog), "No package matches \"nothing\"");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task EscapeInTheSearchBoxCancelsTheSearchNotTheDialog()
    {
        var service = new FakePackageManagerService();
        var dialog = Create(service);
        var root = await ShowAsync(dialog);
        await WaitUntilAsync(() => !dialog.IsLoadingForTest, "load");
        await dialog.HandleKeyEventAsync(Char('s'));

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Escape));

        Assert.IsTrue(root.Controls.AsSnapshot().Contains(dialog));
        CollectionAssert.DoesNotContain(service.Calls, "search:");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task InstallAsksForConfirmationAndRunsOnlyOnY()
    {
        var service = new FakePackageManagerService();
        service.Available.Add(Package("Python"));
        var dialog = await OpenAsync(service);
        await SearchAsync(dialog, "py");

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter, '\r'));

        Assert.IsTrue(dialog.IsConfirmingForTest);
        AssertScreenContains(Render(dialog), "Install Python (Vendor.Python)?  y:yes  any other key:no");

        await dialog.HandleKeyEventAsync(Char('y'));
        await WaitUntilAsync(() => !dialog.IsBusyForTest && service.Calls.Contains("install:Vendor.Python"), "the install to run");

        await WaitUntilAsync(() => dialog.StatusForTest == "Done.", "the install to report");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AnyOtherKeyDeclinesTheConfirmation()
    {
        var service = new FakePackageManagerService();
        service.Installed.Add(Package("Git"));
        var dialog = await OpenAsync(service);

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Spacebar, ' '));
        Assert.IsTrue(dialog.IsConfirmingForTest);

        await dialog.HandleKeyEventAsync(Char('n'));

        Assert.IsFalse(dialog.IsConfirmingForTest);
        CollectionAssert.DoesNotContain(service.Calls, "uninstall:Vendor.Git");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task UninstallRefreshesTheInstalledListWhenItSucceeds()
    {
        var service = new FakePackageManagerService();
        service.Installed.Add(Package("Git"));
        var dialog = await OpenAsync(service);

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter, '\r'));
        await dialog.HandleKeyEventAsync(Char('y'));

        await WaitUntilAsync(() => service.Calls.Count(c => c == "list") >= 2, "the list to be re-read after the uninstall");
        CollectionAssert.Contains(service.Calls, "uninstall:Vendor.Git");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task KeyHintsStayVisibleNextToTheStatus()
    {
        var service = new FakePackageManagerService();
        service.Installed.Add(Package("Git"));
        var dialog = await OpenAsync(service);
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter, '\r'));
        await dialog.HandleKeyEventAsync(Char('y'));
        await WaitUntilAsync(() => dialog.StatusForTest == "Done.", "the uninstall to report");

        var screen = Render(dialog);

        AssertScreenContains(screen, "Done.");
        AssertScreenContains(screen, "s:search", "the hints used to disappear once any status was set");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task APrimaryActionWhileBusyIsExplained()
    {
        var service = new FakePackageManagerService { InstallGate = new TaskCompletionSource() };
        service.Available.Add(Package("Python"));
        var dialog = await OpenAsync(service);
        await SearchAsync(dialog, "py");
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter, '\r'));
        await dialog.HandleKeyEventAsync(Char('y'));
        await WaitUntilAsync(() => dialog.IsBusyForTest, "the install to start");

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter, '\r'));

        Assert.IsFalse(dialog.IsConfirmingForTest, "no second confirmation while busy");
        StringAssert.Contains(dialog.StatusForTest, "x cancels it");

        await dialog.HandleKeyEventAsync(Char('x'));
        await WaitUntilAsync(() => !dialog.IsBusyForTest, "the install to be cancelled");
        Assert.AreEqual("Cancelled.", dialog.StatusForTest);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AFailedInstallReportsTheExitCode()
    {
        var service = new FakePackageManagerService { InstallExitCode = 3 };
        service.Available.Add(Package("Python"));
        var dialog = await OpenAsync(service);
        await SearchAsync(dialog, "py");

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter, '\r'));
        await dialog.HandleKeyEventAsync(Char('y'));

        await WaitUntilAsync(() => dialog.StatusForTest == "Failed (exit 3).", "the failure to be reported");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task EscapeClosesTheDialog()
    {
        var dialog = Create(new FakePackageManagerService());
        var root = await ShowAsync(dialog);

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Escape));

        Assert.IsFalse(root.Controls.AsSnapshot().Contains(dialog));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task OpeningKeepsTheSearchBoxHiddenSoTheFooterIsDrawn()
    {
        // Control.ShowAsync makes every child visible on the way in. The footer was keyed off the
        // search box's visibility, so it — key hints and every y/n confirmation — never appeared.
        var dialog = await OpenAsync(new FakePackageManagerService());

        Assert.IsFalse(dialog.IsInputActiveForTest);
        Assert.IsFalse(dialog.IsInputBoxVisibleForTest);
        AssertScreenContains(Render(dialog), "s:search");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task STheSearchBoxAppearsAndReplacesTheFooter()
    {
        var dialog = await OpenAsync(new FakePackageManagerService());

        await dialog.HandleKeyEventAsync(Char('s'));

        Assert.IsTrue(dialog.IsInputBoxVisibleForTest);
        AssertScreenContains(Render(dialog), "Search query");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheOutputScrollsWithThePageKeys()
    {
        var service = new FakePackageManagerService();
        service.Available.Add(Package("Python"));
        var dialog = await OpenAsync(service);
        await SearchAsync(dialog, "py");
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter, '\r'));
        await dialog.HandleKeyEventAsync(Char('y'));
        await WaitUntilAsync(() => dialog.StatusForTest == "Done.", "the install to finish");

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.PageDown));
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.PageUp));

        Assert.AreEqual(0, dialog.OutputScrollForTest, "a one-line output has nothing to scroll past");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task RReloadsTheInstalledList()
    {
        var service = new FakePackageManagerService();
        var dialog = await OpenAsync(service);

        await dialog.HandleKeyEventAsync(Char('r'));

        await WaitUntilAsync(() => service.Calls.Count(c => c == "list") >= 2, "the list to be read again");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task RInSearchResultsRepeatsTheSearch()
    {
        var service = new FakePackageManagerService();
        service.Available.Add(Package("Python"));
        var dialog = await OpenAsync(service);
        await SearchAsync(dialog, "py");

        await dialog.HandleKeyEventAsync(Char('r'));

        await WaitUntilAsync(() => service.Calls.Count(c => c == "search:py") >= 2, "the search to run again");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task EnterInTheInstalledListOffersToUninstallNotInstall()
    {
        var service = new FakePackageManagerService();
        service.Installed.Add(Package("Git"));
        var dialog = await OpenAsync(service);

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter, '\r'));

        AssertScreenContains(Render(dialog), "Uninstall Git");
    }
}
