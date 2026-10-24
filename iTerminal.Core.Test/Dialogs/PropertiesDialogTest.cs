using iConsole.Controls;
using iConsole.Helper;
using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Services;
using iConsole.Test.Core;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Services.Labels;
using iTerminal.Core.Services.Properties;
using Moq;
using static iTerminal.Core.Test.Helper.UiTestHelper;
using StringAssert = Microsoft.VisualStudio.TestTools.UnitTesting.StringAssert;

namespace iTerminal.Core.Test.Dialogs;

/// <summary>Ctrl+F12: what the disk records, the attributes or mode that can be changed, and the folder total that arrives later.</summary>
[TestClass]
public class PropertiesDialogTest
{
    private static readonly DateTime Stamp = new(2025, 3, 4, 5, 6, 7);
    private static readonly UnixFileMode Octal644 = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead;

    private static FileProperties File(
        FileAttributes attributes = FileAttributes.Archive,
        UnixFileMode? mode = null,
        bool folder = false,
        long? size = 1536,
        string? link = null)
        => new(Path.Combine(Path.GetTempPath(), "dir", "report.txt"), "report.txt", folder, folder ? null : size, Stamp, Stamp.AddDays(1), Stamp.AddDays(2), attributes, mode, link);

    private static IContentModelService Panels(params IContentModel[] models)
    {
        Mock<IContentModelService> panels = new();
        ConcurrentList<IContentModel> list = [];
        foreach (IContentModel model in models)
        {
            list.Add(model);
        }

        panels.SetupGet(p => p.ContentModels).Returns(list);
        return panels.Object;
    }

    private sealed class Rig
    {
        public required PropertiesDialog Dialog { get; init; }

        public required Control Root { get; init; }

        public required Mock<IFilePropertiesService> Service { get; init; }
    }

    private static async Task<Rig> OpenAsync(FileProperties? properties, Exception? readFailure = null, FolderMeasure? measure = null, Size? screen = null)
    {
        Mock<IFilePropertiesService> service = new();
        if (readFailure is not null)
        {
            service.Setup(s => s.Read(It.IsAny<string>())).Throws(readFailure);
        }
        else
        {
            service.Setup(s => s.Read(It.IsAny<string>())).Returns(properties!);
        }

        service.Setup(s => s.MeasureAsync(It.IsAny<string>(), It.IsAny<IProgress<FolderMeasure>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(measure ?? new FolderMeasure(3, 2, 4096, 0));
        PropertiesDialog dialog = new(service.Object, Panels(), Mock.Of<IBeepService>(), Mock.Of<IDateTimeProviderService>(), TestTheme());
        dialog.Load(Path.Combine(Path.GetTempPath(), "dir", "report.txt"));
        Control root = await ShowAsync(dialog, screen);
        await dialog.MeasureTask;
        dialog.OnIdle();
        return new Rig { Dialog = dialog, Root = root, Service = service };
    }

    [TestMethod]
    public async Task AFile_ShowsItsFactsAndDates()
    {
        Rig rig = await OpenAsync(File());

        ScreenBuffer screen = Render(rig.Root);
        AssertScreenContains(screen, "Properties of report.txt");
        AssertScreenContains(screen, "Type       File");
        AssertScreenContains(screen, "Size");
        AssertScreenContains(screen, "2025-03-04 05:06:07");
        AssertScreenContains(screen, "2025-03-05 05:06:07");
        AssertScreenContains(screen, "2025-03-06 05:06:07");
        AssertScreenDoesNotContain(screen, "Contains");
    }

    [TestMethod]
    public async Task ALink_SaysWhereItPoints_AndIsNotMeasured()
    {
        Rig rig = await OpenAsync(File(folder: true, link: "/elsewhere"));

        StringAssert.Contains(rig.Dialog.Details, "Link to a folder");
        StringAssert.Contains(rig.Dialog.Details, "Points to");
        StringAssert.Contains(rig.Dialog.Details, "/elsewhere");
        Assert.IsFalse(rig.Dialog.Details.Contains("Contains"));
        rig.Service.Verify(s => s.MeasureAsync(It.IsAny<string>(), It.IsAny<IProgress<FolderMeasure>?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task AFolder_ShowsItsTotalOnceMeasured()
    {
        Rig rig = await OpenAsync(File(folder: true));

        StringAssert.Contains(rig.Dialog.Details, "3 files, 2 folders");
        Assert.IsFalse(rig.Dialog.Details.Contains("so far"));
        Assert.AreEqual(4096, rig.Dialog.Measure!.Bytes);
    }

    [TestMethod]
    public async Task AFolderThatCouldNotBeFullyRead_SaysTheTotalIsALowerBound()
    {
        Rig rig = await OpenAsync(File(folder: true), measure: new FolderMeasure(1, 1, 10, 2));

        StringAssert.Contains(rig.Dialog.Details, "at least");
        StringAssert.Contains(rig.Dialog.Details, "2 folders could not be read");
    }

    [TestMethod]
    public async Task WhileTheFolderIsStillBeingAdded_ItSaysSoSoFar()
    {
        Mock<IFilePropertiesService> service = new();
        service.Setup(s => s.Read(It.IsAny<string>())).Returns(File(folder: true));
        TaskCompletionSource<FolderMeasure> never = new();
        service.Setup(s => s.MeasureAsync(It.IsAny<string>(), It.IsAny<IProgress<FolderMeasure>?>(), It.IsAny<CancellationToken>())).Returns(never.Task);
        PropertiesDialog dialog = new(service.Object, Panels(), Mock.Of<IBeepService>(), Mock.Of<IDateTimeProviderService>(), TestTheme());
        dialog.Load("x");
        await ShowAsync(dialog);

        StringAssert.Contains(dialog.Details, "so far");
        StringAssert.Contains(dialog.Details, "…");

        dialog.Close();
        never.TrySetCanceled();
    }

    [TestMethod]
    public async Task AnEntryThatCannotBeRead_ShowsTheErrorAndNothingElse()
    {
        Rig rig = await OpenAsync(null, new IOException("gone"));

        Assert.IsNull(rig.Dialog.Properties);
        Assert.AreEqual("Error: gone", rig.Dialog.Error);
        Assert.AreEqual(string.Empty, rig.Dialog.Details);
        AssertScreenContains(Render(rig.Root), "Error: gone");
    }

    [TestMethod]
    public async Task EscapeClosesWithoutWriting()
    {
        Rig rig = await OpenAsync(File());

        await rig.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Escape));

        Assert.IsFalse(rig.Dialog.IsVisible);
        rig.Service.Verify(s => s.Apply(It.IsAny<string>(), It.IsAny<FileAttributes?>(), It.IsAny<UnixFileMode?>()), Times.Never);
    }

    [TestMethod]
    public async Task ApplyingNothingChanged_WritesNothingAndCloses()
    {
        Rig rig = await OpenAsync(File(FileAttributes.Archive, UnixFileMode.UserRead | UnixFileMode.UserWrite));

        Assert.IsTrue(rig.Dialog.Apply());

        Assert.IsFalse(rig.Dialog.IsVisible);
        rig.Service.Verify(s => s.Apply(It.IsAny<string>(), It.IsAny<FileAttributes?>(), It.IsAny<UnixFileMode?>()), Times.Never);
    }

    [TestMethod]
    public async Task ApplyingFromAnEntryThatCouldNotBeRead_JustCloses()
    {
        Rig rig = await OpenAsync(null, new IOException("gone"));

        Assert.IsTrue(rig.Dialog.Apply());
        Assert.IsFalse(rig.Dialog.IsVisible);
    }

    [TestMethod]
    public async Task Attributes_OnWindows_AreTickedFromTheFile_AndWrittenWhenChanged()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        Rig rig = await OpenAsync(File(FileAttributes.ReadOnly | FileAttributes.Archive));
        Assert.IsTrue(rig.Dialog.IsReadOnlyChecked);
        Assert.IsTrue(rig.Dialog.IsArchiveChecked);
        Assert.IsFalse(rig.Dialog.IsHiddenChecked);
        AssertScreenContains(Render(rig.Root), "Read-only");

        rig.Dialog.IsReadOnlyChecked = false;
        rig.Dialog.IsHiddenChecked = true;
        Assert.IsTrue(rig.Dialog.Apply());

        rig.Service.Verify(s => s.Apply(It.IsAny<string>(), FileAttributes.Hidden | FileAttributes.Archive, null), Times.Once);
        Assert.IsFalse(rig.Dialog.IsVisible);
    }

    [TestMethod]
    public async Task EnterApplies_AndADiskRefusalStaysInTheDialogWithTheReason()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        Rig rig = await OpenAsync(File());
        rig.Service.Setup(s => s.Apply(It.IsAny<string>(), It.IsAny<FileAttributes?>(), It.IsAny<UnixFileMode?>())).Throws(new UnauthorizedAccessException("denied"));
        rig.Dialog.IsHiddenChecked = true;

        await rig.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter));

        Assert.IsTrue(rig.Dialog.IsVisible);
        Assert.AreEqual("Error: denied", rig.Dialog.Error);
    }

    [TestMethod]
    public async Task Mode_OffWindows_IsShownAsOctal_ValidatedAndWritten()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        Rig rig = await OpenAsync(File(mode: UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead));
        Assert.AreEqual("644", rig.Dialog.Mode);
        StringAssert.Contains(rig.Dialog.Details, "rw-r--r--");

        rig.Dialog.Mode = "9z";
        Assert.IsFalse(rig.Dialog.Apply());
        StringAssert.Contains(rig.Dialog.Error, "octal");
        Assert.IsTrue(rig.Dialog.IsVisible);

        rig.Dialog.Mode = "755";
        Assert.IsTrue(rig.Dialog.Apply());
        rig.Service.Verify(s => s.Apply(It.IsAny<string>(), null, It.IsAny<UnixFileMode?>()), Times.Once);
    }

    [TestMethod]
    public async Task ClosingStopsTheFolderWalk()
    {
        Mock<IFilePropertiesService> service = new();
        service.Setup(s => s.Read(It.IsAny<string>())).Returns(File(folder: true));
        CancellationToken seen = default;
        TaskCompletionSource<FolderMeasure> never = new();
        service.Setup(s => s.MeasureAsync(It.IsAny<string>(), It.IsAny<IProgress<FolderMeasure>?>(), It.IsAny<CancellationToken>()))
            .Callback<string, IProgress<FolderMeasure>?, CancellationToken>((_, _, token) => seen = token)
            .Returns(never.Task);
        PropertiesDialog dialog = new(service.Object, Panels(), Mock.Of<IBeepService>(), Mock.Of<IDateTimeProviderService>(), TestTheme());
        dialog.Load("x");
        await ShowAsync(dialog);
        await WaitUntilAsync(() => seen.CanBeCanceled, "the walk to start");

        dialog.Close();

        Assert.IsTrue(seen.IsCancellationRequested);
        never.TrySetCanceled();
    }

    [TestMethod]
    public async Task OnASmallTerminal_TheDialogFitsAndStillDraws()
    {
        Size small = new(56, 24);
        Rig rig = await OpenAsync(File(), screen: small);

        Assert.IsTrue(rig.Dialog.ViewSize.Width <= small.Width);
        AssertScreenContains(Render(rig.Root, small), "Properties of report.txt");
    }

    [TestMethod]
    public async Task AfterAChange_TheLocalPanelsAreReRead_AndOthersAreLeftAlone()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        Mock<IContentModel> local = new();
        local.SetupGet(m => m.Url).Returns(new Url(Path.GetTempPath()));
        Mock<IContentModel> other = new();
        other.SetupGet(m => m.Url).Returns(new Url("memdrive://x/"));
        Mock<IFilePropertiesService> service = new();
        service.Setup(s => s.Read(It.IsAny<string>())).Returns(File());
        PropertiesDialog dialog = new(service.Object, Panels(local.Object, other.Object), Mock.Of<IBeepService>(), Mock.Of<IDateTimeProviderService>(), TestTheme());
        dialog.Load("x");
        await ShowAsync(dialog);
        dialog.IsHiddenChecked = true;

        Assert.IsTrue(dialog.Apply());

        local.Verify(m => m.Refresh(true), Times.Once);
        other.Verify(m => m.Refresh(It.IsAny<bool>()), Times.Never);
    }

    [TestMethod]
    public async Task TheDatesAreShownForEditing()
    {
        Rig rig = await OpenAsync(File(mode: Octal644));

        Assert.AreEqual("2025-03-05 05:06:07", rig.Dialog.ModifiedText);
        Assert.AreEqual("2025-03-06 05:06:07", rig.Dialog.AccessedText);
        AssertScreenContains(Render(rig.Root), "2025-03-05 05:06:07");
    }

    [TestMethod]
    public async Task ChangingTheModifiedDate_WritesOnlyThatDate()
    {
        Rig rig = await OpenAsync(File(mode: Octal644));
        rig.Dialog.ModifiedText = "2020-01-02 03:04:05";

        Assert.IsTrue(rig.Dialog.Apply());

        rig.Service.Verify(s => s.SetTimes(It.IsAny<string>(), null, new DateTime(2020, 1, 2, 3, 4, 5), null), Times.Once);
    }

    [TestMethod]
    public async Task ADateThatIsNotADate_IsRefused_AndNothingIsWritten()
    {
        Rig rig = await OpenAsync(File(mode: Octal644));
        rig.Dialog.AccessedText = "yesterday";
        rig.Dialog.ModifiedText = "2020-01-02 03:04:05";

        Assert.IsFalse(rig.Dialog.Apply());

        StringAssert.StartsWith(rig.Dialog.Error, "Error: a date is yyyy-MM-dd HH:mm:ss");
        rig.Service.Verify(s => s.SetTimes(It.IsAny<string>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>()), Times.Never);
    }

    [TestMethod]
    public async Task UnchangedDates_AreNotWritten()
    {
        Rig rig = await OpenAsync(File(mode: Octal644));

        Assert.IsTrue(rig.Dialog.Apply());

        rig.Service.Verify(s => s.SetTimes(It.IsAny<string>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>()), Times.Never);
    }

    [TestMethod]
    public async Task TheColourAndTheTagsAreShown()
    {
        Mock<IFilePropertiesService> service = new();
        service.Setup(s => s.Read(It.IsAny<string>())).Returns(File(mode: Octal644));
        Mock<IFileLabelService> labels = new();
        labels.Setup(l => l.Get(It.IsAny<string>())).Returns(new FileLabel(LabelColour.Red, ["invoice", "2026"]));
        PropertiesDialog dialog = new(service.Object, Panels(), Mock.Of<IBeepService>(), Mock.Of<IDateTimeProviderService>(), TestTheme(), labels.Object);
        dialog.Load(Path.Combine(Path.GetTempPath(), "dir", "report.txt"));
        Control root = await ShowAsync(dialog);
        dialog.OnIdle();

        ScreenBuffer screen = Render(root);
        AssertScreenContains(screen, "Colour     red");
        AssertScreenContains(screen, "Tags       invoice, 2026");
    }

    [TestMethod]
    public async Task AFileWithNoLabel_SaysNoneForBoth()
    {
        Mock<IFilePropertiesService> service = new();
        service.Setup(s => s.Read(It.IsAny<string>())).Returns(File(mode: Octal644));
        Mock<IFileLabelService> labels = new();
        labels.Setup(l => l.Get(It.IsAny<string>())).Returns(FileLabel.None);
        PropertiesDialog dialog = new(service.Object, Panels(), Mock.Of<IBeepService>(), Mock.Of<IDateTimeProviderService>(), TestTheme(), labels.Object);
        dialog.Load(Path.Combine(Path.GetTempPath(), "dir", "report.txt"));
        Control root = await ShowAsync(dialog);
        dialog.OnIdle();

        ScreenBuffer screen = Render(root);
        AssertScreenContains(screen, "Colour     none");
        AssertScreenContains(screen, "Tags       none");
    }
}
