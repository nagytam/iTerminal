using System.Text;
using iConsole.Certificates;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Model;
using iConsole.Services;
using iConsole.Test.Certificates;
using Moq;

namespace iConsole.Test.Dialogs;

/// <summary>
/// The certificate viewer: a file that holds a certificate is listed; any other file runs the fallback once, after the
/// dialog has closed itself, and a fallback that throws is reported rather than lost.
/// </summary>
[TestClass]
public class CertificateViewerDialogTest
{
    private readonly List<string> _files = [];

    [TestCleanup]
    public void Cleanup()
    {
        foreach (string file in _files.Where(File.Exists))
        {
            File.Delete(file);
        }
    }

    private string WriteFile(byte[] bytes)
    {
        string path = Path.Combine(Path.GetTempPath(), "certificate-viewer-" + Guid.NewGuid().ToString("N") + ".pem");
        File.WriteAllBytes(path, bytes);
        _files.Add(path);
        return path;
    }

    private string WriteText(string text) => WriteFile(Encoding.UTF8.GetBytes(text));

    private static (CertificateViewerDialog Dialog, Mock<INotificationService> Notifications) Create()
    {
        Mock<INotificationService> notifications = new();
        CertificateViewerDialog dialog = new(new ThemeService(), notifications.Object) { Clock = () => CertificateTestData.Now };
        return (dialog, notifications);
    }

    /// <summary>Shows the file the way the command does: start the read, wait for it, then run the apply step.</summary>
    private static async Task LoadAsync(CertificateViewerDialog dialog)
    {
        await dialog.OnInitializeAsync();
        await dialog.LoadTask;
        dialog.OnIdle();
    }

    [TestMethod]
    public async Task CertificateFile_ListsItsFieldsAndTheCountOnTheStatusLine()
    {
        string path = WriteText(CertificateTestData.Pem(CertificateTestData.SelfSignedRsa()));
        (CertificateViewerDialog dialog, _) = Create();
        dialog.Url = new Url(path);

        await LoadAsync(dialog);

        Assert.AreEqual(1, dialog.Certificates.Count);
        CollectionAssert.Contains(dialog.Lines.ToList(), "Subject: CN=example.test");
        StringAssert.StartsWith(dialog.Status, "Certificate 1 of 1");
        StringAssert.Contains(dialog.Status, "valid until");
    }

    [TestMethod]
    public async Task NotACertificate_ClosesAndRunsTheFallbackOnce()
    {
        string path = WriteText("just some text, no certificate");
        (CertificateViewerDialog dialog, _) = Create();
        int fallbacks = 0;
        dialog.OnFallback = () =>
        {
            fallbacks++;
            return Task.CompletedTask;
        };
        dialog.Url = new Url(path);

        await LoadAsync(dialog);
        await dialog.FallbackTask;

        Assert.AreEqual(1, fallbacks);
        Assert.AreEqual(0, dialog.Certificates.Count);
    }

    [TestMethod]
    public async Task PemWithOnlyAKey_FallsBack()
    {
        string path = WriteText(CertificateTestData.PrivateKeyPem());
        (CertificateViewerDialog dialog, _) = Create();
        int fallbacks = 0;
        dialog.OnFallback = () =>
        {
            fallbacks++;
            return Task.CompletedTask;
        };
        dialog.Url = new Url(path);

        await LoadAsync(dialog);
        await dialog.FallbackTask;

        Assert.AreEqual(1, fallbacks, "a key is never shown, so the text viewer takes the file");
    }

    [TestMethod]
    public async Task FileOverTheCap_FallsBack()
    {
        string path = WriteFile(new byte[CertificateLimits.MaxFileBytes + 1]);
        (CertificateViewerDialog dialog, _) = Create();
        int fallbacks = 0;
        dialog.OnFallback = () =>
        {
            fallbacks++;
            return Task.CompletedTask;
        };
        dialog.Url = new Url(path);

        await LoadAsync(dialog);
        await dialog.FallbackTask;

        Assert.AreEqual(1, fallbacks);
    }

    [TestMethod]
    public async Task MissingFile_FallsBack()
    {
        string path = Path.Combine(Path.GetTempPath(), "certificate-viewer-missing-" + Guid.NewGuid().ToString("N") + ".pem");
        (CertificateViewerDialog dialog, _) = Create();
        int fallbacks = 0;
        dialog.OnFallback = () =>
        {
            fallbacks++;
            return Task.CompletedTask;
        };
        dialog.Url = new Url(path);

        await LoadAsync(dialog);
        await dialog.FallbackTask;

        Assert.AreEqual(1, fallbacks);
    }

    [TestMethod]
    public async Task FallbackThatThrows_IsReportedAsANotificationNotLost()
    {
        string path = WriteText("no certificate here");
        (CertificateViewerDialog dialog, Mock<INotificationService> notifications) = Create();
        dialog.OnFallback = () => throw new IOException("the text viewer failed");
        dialog.Url = new Url(path);

        await LoadAsync(dialog);
        await dialog.FallbackTask;

        notifications.Verify(service => service.Notify(
            It.IsAny<string>(),
            "the text viewer failed",
            NotificationSeverity.Error,
            It.IsAny<TimeSpan?>()), Times.Once);
    }

    [TestMethod]
    public async Task Apply_RunsOnceAndAFallbackIsNotRunTwice()
    {
        string path = WriteText("no certificate here");
        (CertificateViewerDialog dialog, _) = Create();
        int fallbacks = 0;
        dialog.OnFallback = () =>
        {
            fallbacks++;
            return Task.CompletedTask;
        };
        dialog.Url = new Url(path);

        await LoadAsync(dialog);
        dialog.OnIdle();
        dialog.OnIdle();
        await dialog.FallbackTask;

        Assert.AreEqual(1, fallbacks, "the apply step runs once per request");
    }

    [TestMethod]
    public async Task ReusedDialog_ShowsTheSecondFilesCertificatesNotTheFirsts()
    {
        string first = WriteText(CertificateTestData.Pem(CertificateTestData.SelfSignedRsa("first.test")));
        string second = WriteText(CertificateTestData.Pem(CertificateTestData.SelfSignedRsa("second.test")));
        (CertificateViewerDialog dialog, _) = Create();

        dialog.Url = new Url(first);
        await LoadAsync(dialog);
        dialog.Url = new Url(second);
        await LoadAsync(dialog);

        Assert.AreEqual("CN=second.test", dialog.CurrentCertificate?.Subject);
        CollectionAssert.DoesNotContain(dialog.Lines.ToList(), "Subject: CN=first.test");
    }

    [TestMethod]
    public async Task SameUnchangedFileAgain_IsServedFromTheCache()
    {
        string path = WriteText(CertificateTestData.Pem(CertificateTestData.SelfSignedRsa("cached.test")));
        (CertificateViewerDialog dialog, _) = Create();
        dialog.Url = new Url(path);
        await LoadAsync(dialog);

        dialog.Url = new Url(path);
        await dialog.OnInitializeAsync();

        Assert.IsTrue(dialog.LoadTask.IsCompleted, "the cached result is ready at once: nothing was read again");
        dialog.OnIdle();
        Assert.AreEqual("CN=cached.test", dialog.CurrentCertificate?.Subject);
    }

    [TestMethod]
    public async Task EditedFileAgain_IsReadAgainNotShownFromTheCache()
    {
        string path = WriteText(CertificateTestData.Pem(CertificateTestData.SelfSignedRsa("before.test")));
        (CertificateViewerDialog dialog, _) = Create();
        dialog.Url = new Url(path);
        await LoadAsync(dialog);

        File.WriteAllText(path, CertificateTestData.Pem(CertificateTestData.SelfSignedRsa("after-the-edit.test")));
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));
        dialog.Url = new Url(path);
        await LoadAsync(dialog);

        Assert.AreEqual("CN=after-the-edit.test", dialog.CurrentCertificate?.Subject, "the file changed on disk, so the old certificate must not show");
    }

    [TestMethod]
    public async Task Stale_ResultForAnEarlierUrlIsNeverApplied()
    {
        string first = WriteText("no certificate here");
        string second = WriteText(CertificateTestData.Pem(CertificateTestData.SelfSignedRsa("current.test")));
        (CertificateViewerDialog dialog, _) = Create();
        int fallbacks = 0;
        dialog.OnFallback = () =>
        {
            fallbacks++;
            return Task.CompletedTask;
        };

        dialog.Url = new Url(first);
        await dialog.OnInitializeAsync();
        dialog.Url = new Url(second);
        await LoadAsync(dialog);

        Assert.AreEqual(0, fallbacks, "the first file's fallback never runs once the URL has moved on");
        Assert.AreEqual("CN=current.test", dialog.CurrentCertificate?.Subject);
    }

    [TestMethod]
    public async Task Escape_WhileLoading_CancelsTheReadAndDoesNotFallBack()
    {
        string path = WriteText(CertificateTestData.Pem(CertificateTestData.SelfSignedRsa()));
        (CertificateViewerDialog dialog, _) = Create();
        int fallbacks = 0;
        dialog.OnFallback = () =>
        {
            fallbacks++;
            return Task.CompletedTask;
        };
        dialog.Url = new Url(path);
        await dialog.OnInitializeAsync();

        Assert.IsTrue(await dialog.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.Escape, false, false, false)));
        await dialog.LoadTask;
        dialog.OnIdle();

        Assert.AreEqual(0, fallbacks, "a cancelled read is dropped, not shown as a fallback");
    }
}
