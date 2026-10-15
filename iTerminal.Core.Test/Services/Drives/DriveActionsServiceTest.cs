using iTerminal.Core.Services.Drives;
using iTerminal.Core.Services.Processes;
using iTerminal.Core.Test.Helper;

namespace iTerminal.Core.Test.Services.Drives;

[TestClass]
public class DriveActionsServiceTest
{
    private static DriveActionsService Service(FakeProcessRunner runner) => new(runner, "pwsh");

    private static void RequireWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The PowerShell actions are Windows ones.");
        }
    }

    [TestMethod]
    public void OnlyDiskImagesAreMountable()
    {
        Assert.IsTrue(DriveActionsService.IsImage(@"C:\x\disc.ISO"));
        Assert.IsTrue(DriveActionsService.IsImage("a.vhdx"));
        Assert.IsFalse(DriveActionsService.IsImage("a.zip"));
    }

    [TestMethod]
    [DataRow(@"E:\", 'E')]
    [DataRow("e:", 'E')]
    [DataRow("e", 'E')]
    public void ARootNamesItsLetter(string root, char expected)
    {
        Assert.IsTrue(DriveActionsService.TryLetter(root, out var letter));
        Assert.AreEqual(expected, letter);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow(@"E:\folder")]
    [DataRow("E:'; calc; '")]
    [DataRow("/media/usb")]
    public void AnythingElseIsNotARoot(string root)
    {
        Assert.IsFalse(DriveActionsService.TryLetter(root, out _));
    }

    [TestMethod]
    public void QuotingDoublesTheQuoteAndFlattensNewlines()
    {
        Assert.AreEqual("'it''s'", DriveActionsService.Quote("it's"));
        Assert.AreEqual("'a b'", DriveActionsService.Quote("a\r\nb").Replace("  ", " "));
    }

    [TestMethod]
    public void BitLockerValuesAreNamed()
    {
        var on = DriveActionsService.ParseBitLocker(new ProcessOutput(0, "BL=1\r\n", ""));
        var off = DriveActionsService.ParseBitLocker(new ProcessOutput(0, "BL=0", ""));
        var odd = DriveActionsService.ParseBitLocker(new ProcessOutput(0, "BL=3", ""));
        var empty = DriveActionsService.ParseBitLocker(new ProcessOutput(0, "BL=", ""));
        var failed = DriveActionsService.ParseBitLocker(new ProcessOutput(1, "", "boom"));

        Assert.AreEqual(BitLockerState.On, on.State);
        Assert.AreEqual(BitLockerState.Off, off.State);
        Assert.AreEqual(BitLockerState.Unknown, odd.State);
        StringAssert.Contains(odd.Text, "3");
        Assert.AreEqual(BitLockerState.Unavailable, empty.State);
        Assert.AreEqual(BitLockerState.Unavailable, failed.State);
    }

    [TestMethod]
    public async Task BitLockerIsReadThroughTheShellProperty()
    {
        RequireWindows();
        var runner = new FakeProcessRunner().Answer("pwsh", "BL=1");

        var status = await Service(runner).GetBitLockerStatusAsync(@"D:\");

        Assert.AreEqual(BitLockerState.On, status.State);
        var script = runner.Calls.Single().Arguments.Last();
        StringAssert.Contains(script, "System.Volume.BitLockerProtection");
        StringAssert.Contains(script, "NameSpace('D:\\')");
    }

    [TestMethod]
    public async Task EjectReportsASafeRemoval()
    {
        RequireWindows();
        var runner = new FakeProcessRunner().Answer("pwsh", "EJECTED");

        var result = await Service(runner).EjectAsync(@"E:\");

        Assert.IsTrue(result.Success, result.Message);
        StringAssert.Contains(result.Message, "E:");
        StringAssert.Contains(runner.Calls.Single().Arguments.Last(), "InvokeVerb('Eject')");
    }

    [TestMethod]
    public async Task EjectOfAFixedDiskIsRefused()
    {
        RequireWindows();
        var runner = new FakeProcessRunner().Answer("pwsh", "NOTREMOVABLE", exitCode: 3);

        var result = await Service(runner).EjectAsync(@"C:\");

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.Message, "not a removable drive");
    }

    [TestMethod]
    public async Task EjectOfADriveInUseSaysSo()
    {
        RequireWindows();
        var runner = new FakeProcessRunner().Answer("pwsh", "BUSY", exitCode: 4);

        var result = await Service(runner).EjectAsync(@"E:\");

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.Message, "in use");
    }

    [TestMethod]
    public async Task EjectRefusesAnythingButALetter()
    {
        RequireWindows();
        var runner = new FakeProcessRunner();

        var result = await Service(runner).EjectAsync(@"\\server\share");

        Assert.IsFalse(result.Success);
        Assert.AreEqual(0, runner.Calls.Count, "nothing is run for a path that is not a drive");
    }

    [TestMethod]
    public async Task FormatPassesTheLetterFileSystemAndLabel()
    {
        RequireWindows();
        var runner = new FakeProcessRunner().Answer("pwsh", "FORMATTED");

        var result = await Service(runner).FormatAsync(@"F:\", new DriveFormatOptions("exFAT", "it's mine", Quick: true));

        Assert.IsTrue(result.Success, result.Message);
        var script = runner.Calls.Single().Arguments.Last();
        StringAssert.Contains(script, "-DriveLetter 'F'");
        StringAssert.Contains(script, "-FileSystem exFAT");
        StringAssert.Contains(script, "-NewFileSystemLabel 'it''s mine'");
        Assert.IsFalse(script.Contains("-Full"), "a quick format does not ask for the full pass");
    }

    [TestMethod]
    public async Task AFullFormatAsksForTheFullPass()
    {
        RequireWindows();
        var runner = new FakeProcessRunner().Answer("pwsh", "FORMATTED");

        await Service(runner).FormatAsync(@"F:\", new DriveFormatOptions("NTFS", "", Quick: false));

        StringAssert.Contains(runner.Calls.Single().Arguments.Last(), "-Full");
    }

    [TestMethod]
    public async Task FormatRefusesAFileSystemItDoesNotKnow()
    {
        RequireWindows();
        var runner = new FakeProcessRunner();

        var result = await Service(runner).FormatAsync(@"F:\", new DriveFormatOptions("NTFS; calc", ""));

        Assert.IsFalse(result.Success);
        Assert.AreEqual(0, runner.Calls.Count);
    }

    [TestMethod]
    public async Task AnAccessDeniedFormatSaysItNeedsAnAdministrator()
    {
        RequireWindows();
        var runner = new FakeProcessRunner().Answer("pwsh", "", exitCode: 1, stderr: "Format-Volume : Access is denied.");

        var result = await Service(runner).FormatAsync(@"F:\", new DriveFormatOptions("NTFS", ""));

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.Message, "administrator");
    }

    [TestMethod]
    public void TheSystemDriveAndTheOnesInUseCannotBeFormatted()
    {
        RequireWindows();
        var service = Service(new FakeProcessRunner());
        var system = Path.GetPathRoot(Environment.SystemDirectory)!;

        StringAssert.Contains(service.WhyCannotFormat(system, []), "system drive");
        StringAssert.Contains(service.WhyCannotFormat(system[..1] + ":", []), "system drive");
        Assert.IsNotNull(service.WhyCannotFormat(@"\\server\share", []));
    }

    [TestMethod]
    public async Task MountReportsTheLettersTheImageGot()
    {
        RequireWindows();
        var runner = new FakeProcessRunner().Answer("pwsh", "MOUNTED=G,H");

        var result = await Service(runner).MountImageAsync(@"C:\isos\disc.iso");

        Assert.IsTrue(result.Success, result.Message);
        StringAssert.Contains(result.Message, "G:, H:");
        StringAssert.Contains(runner.Calls.Single().Arguments.Last(), "Mount-DiskImage -ImagePath 'C:\\isos\\disc.iso'");
    }

    [TestMethod]
    public async Task MountRefusesAFileThatIsNotAnImage()
    {
        var runner = new FakeProcessRunner();

        var result = await Service(runner).MountImageAsync(@"C:\isos\notes.txt");

        Assert.IsFalse(result.Success);
        Assert.AreEqual(0, runner.Calls.Count);
    }

    [TestMethod]
    public async Task UnmountDismountsByPath()
    {
        RequireWindows();
        var runner = new FakeProcessRunner().Answer("pwsh", "DISMOUNTED");

        var result = await Service(runner).UnmountImageAsync(@"C:\isos\disc.iso");

        Assert.IsTrue(result.Success, result.Message);
        StringAssert.Contains(runner.Calls.Single().Arguments.Last(), "Dismount-DiskImage -ImagePath 'C:\\isos\\disc.iso'");
    }
}
