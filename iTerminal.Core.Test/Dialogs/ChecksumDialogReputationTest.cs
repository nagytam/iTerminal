using iConsole.Controls;
using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Model.Passwords;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Checksums;
using iTerminal.Core.Services.FileAssociations;
using iTerminal.Core.Services.Reputation;
using Moq;
using StringAssert = Microsoft.VisualStudio.TestTools.UnitTesting.StringAssert;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Test.Dialogs;

/// <summary>Shift+F12, F4 and F5: the VirusTotal look-up of the SHA-256, on request only.</summary>
[TestClass]
public class ChecksumDialogReputationTest
{
    private static readonly ChecksumResult Hashes = new(
        new Dictionary<ChecksumAlgorithm, string>
        {
            [ChecksumAlgorithm.Md5] = new('a', 32),
            [ChecksumAlgorithm.Sha1] = new('b', 40),
            [ChecksumAlgorithm.Sha256] = new('c', 64),
            [ChecksumAlgorithm.Sha512] = new('d', 128),
        },
        2048);

    private static readonly ReputationResult Flagged = new(
        ReputationStatus.Found, Malicious: 5, Suspicious: 0, Total: 72,
        FirstSeen: new DateTimeOffset(2020, 1, 2, 12, 0, 0, TimeSpan.Zero),
        LastSeen: new DateTimeOffset(2026, 3, 4, 12, 0, 0, TimeSpan.Zero),
        Link: "https://www.virustotal.com/gui/file/x");

    private string _folder = string.Empty;

    [TestInitialize]
    public void Init()
    {
        _folder = Path.Combine(Path.GetTempPath(), "checksum-reputation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(_folder, true);

    private static Mock<IPasswordManager> PasswordsWithKey(string? key)
    {
        Mock<IPasswordManager> passwords = new();
        passwords.SetupGet(p => p.IsUnlocked).Returns(true);
        passwords.Setup(p => p.List()).Returns(key is null ? [] : [new PasswordEntry { Title = "VirusTotal", Password = key }]);
        return passwords;
    }

    private static Mock<IHashReputationService> Service(ReputationResult? answer = null)
    {
        Mock<IHashReputationService> service = new();
        if (answer is not null)
        {
            service.Setup(r => r.LookUpAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(answer);
        }

        return service;
    }

    private static void VerifyNoLookUp(Mock<IHashReputationService> service) =>
        service.Verify(r => r.LookUpAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

    private async Task<(ChecksumDialog Dialog, Control Root)> OpenAsync(
        bool hashed, IHashReputationService reputation, IPasswordManager passwords, IFileLauncher? launcher = null)
    {
        string path = Path.Combine(_folder, "data.bin");
        File.WriteAllText(path, "x");
        Mock<IChecksumService> checksums = new();
        if (hashed)
        {
            checksums.Setup(s => s.ComputeAsync(It.IsAny<Url>(), It.IsAny<IFileSystem>(), It.IsAny<IProgress<long>?>(), It.IsAny<CancellationToken>())).ReturnsAsync(Hashes);
        }
        else
        {
            checksums.Setup(s => s.ComputeAsync(It.IsAny<Url>(), It.IsAny<IFileSystem>(), It.IsAny<IProgress<long>?>(), It.IsAny<CancellationToken>())).ThrowsAsync(new IOException("gone"));
        }

        ChecksumDialog dialog = new(checksums.Object, reputation, passwords, launcher ?? Mock.Of<IFileLauncher>(),
            Mock.Of<IBeepService>(), Mock.Of<IDateTimeProviderService>(), TestTheme());
        dialog.Load(new Url(path), Mock.Of<IFileSystem>());
        Control root = await ShowAsync(dialog, null);
        await dialog.ComputeTask;
        dialog.OnIdle();
        return (dialog, root);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task OpeningTheDialog_NeverAsksVirusTotal()
    {
        Mock<IHashReputationService> service = Service();

        await OpenAsync(true, service.Object, PasswordsWithKey("k").Object);

        VerifyNoLookUp(service);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task F4_SendsOnlyTheSha256AndTheKey_AndShowsDetectionsAndDates()
    {
        Mock<IHashReputationService> service = Service(Flagged);
        (ChecksumDialog dialog, Control root) = await OpenAsync(true, service.Object, PasswordsWithKey("the-key").Object);

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.F4));
        await dialog.LookupTask;
        dialog.OnIdle();

        service.Verify(r => r.LookUpAsync(new string('c', 64), "the-key", It.IsAny<CancellationToken>()), Times.Once);
        AssertScreenContains(Render(root), "5/72 engines flag this file");
        StringAssert.Contains(dialog.Reputation, "First seen 20");
        StringAssert.Contains(dialog.Reputation, "F5 opens the VirusTotal page");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task F4_WithoutAKey_SaysHowToAddOne_AndAsksNothing()
    {
        Mock<IHashReputationService> service = Service();
        (ChecksumDialog dialog, _) = await OpenAsync(true, service.Object, PasswordsWithKey(null).Object);

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.F4));

        StringAssert.Contains(dialog.Reputation, "Password Manager entry titled");
        VerifyNoLookUp(service);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task F4_BeforeTheFileIsRead_SaysToWait()
    {
        Mock<IHashReputationService> service = Service();
        (ChecksumDialog dialog, _) = await OpenAsync(false, service.Object, PasswordsWithKey("k").Object);

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.F4));

        StringAssert.Contains(dialog.Reputation, "Wait until the file has been read");
        VerifyNoLookUp(service);
    }

    [Timeout(60_000)]
    [TestMethod]
    [DataRow(ReputationStatus.NotFound, "never seen this file")]
    [DataRow(ReputationStatus.RateLimited, "rate limit reached: try again in a minute")]
    [DataRow(ReputationStatus.Offline, "offline")]
    [DataRow(ReputationStatus.InvalidKey, "refused the API key")]
    public async Task F4_SaysEachFailureInWords(ReputationStatus status, string expected)
    {
        Mock<IHashReputationService> service = Service(new ReputationResult(status));
        (ChecksumDialog dialog, _) = await OpenAsync(true, service.Object, PasswordsWithKey("k").Object);

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.F4));
        await dialog.LookupTask;
        dialog.OnIdle();

        StringAssert.Contains(dialog.Reputation, expected);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ClosingTheDialog_CancelsALookUpInFlight()
    {
        CancellationToken seen = default;
        TaskCompletionSource started = new();
        Mock<IHashReputationService> service = Service();
        service.Setup(r => r.LookUpAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(async (string _, string _, CancellationToken token) =>
            {
                seen = token;
                started.SetResult();
                await Task.Delay(Timeout.Infinite, token);
                return Flagged;
            });
        (ChecksumDialog dialog, _) = await OpenAsync(true, service.Object, PasswordsWithKey("k").Object);
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.F4));
        await started.Task;

        dialog.Close();
        await dialog.LookupTask;

        Assert.IsTrue(seen.IsCancellationRequested);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ASecondF4_DiscardsTheFirstAnswer()
    {
        TaskCompletionSource<ReputationResult> first = new();
        int calls = 0;
        Mock<IHashReputationService> service = Service();
        service.Setup(r => r.LookUpAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string _, string _, CancellationToken _) => ++calls == 1
                ? first.Task
                : Task.FromResult(new ReputationResult(ReputationStatus.NotFound)));
        (ChecksumDialog dialog, _) = await OpenAsync(true, service.Object, PasswordsWithKey("k").Object);

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.F4));
        Task firstTask = dialog.LookupTask;
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.F4));
        await dialog.LookupTask;
        first.SetResult(Flagged);
        await firstTask;
        dialog.OnIdle();

        StringAssert.Contains(dialog.Reputation, "never seen this file");
        Assert.IsFalse(dialog.Reputation.Contains("engines flag"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task F5_OpensTheVirusTotalPage_OnlyAfterAnAnswer()
    {
        TaskCompletionSource<string> opened = new();
        Mock<IFileLauncher> launcher = new();
        launcher.Setup(l => l.OpenUrlAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string url, CancellationToken _) =>
            {
                opened.TrySetResult(url);
                return Task.FromResult(LaunchOutcome.Success);
            });
        Mock<IHashReputationService> service = Service(Flagged);
        (ChecksumDialog dialog, _) = await OpenAsync(true, service.Object, PasswordsWithKey("k").Object, launcher.Object);

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.F5));
        Assert.IsFalse(opened.Task.IsCompleted);

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.F4));
        await dialog.LookupTask;
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.F5));

        Assert.AreEqual("https://www.virustotal.com/gui/file/x", await opened.Task.WaitAsync(TimeSpan.FromSeconds(10)));
    }
}
