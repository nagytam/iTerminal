using System.Buffers.Binary;
using System.Text;
using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Services.Images;
using Moq;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Test.Dialogs;

[TestClass]
public class StripMetadataDialogTest
{
    private string _folder = string.Empty;

    [TestInitialize]
    public void Initialize()
    {
        _folder = Path.Combine(Path.GetTempPath(), "StripMetadataDialogTest_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_folder);
    }

    [TestCleanup]
    public void Cleanup()
    {
        Directory.Delete(_folder, recursive: true);
    }

    private static byte[] Png()
    {
        MemoryStream ms = new();
        ms.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        byte[] ihdr = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr, 16);
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(4), 16);
        ihdr[8] = 8;
        ihdr[9] = 2;
        Chunk(ms, "IHDR", ihdr);
        Chunk(ms, "IDAT", [0x78, 0x9C, 0x63, 0x00, 0x00, 0x00, 0x01, 0x00, 0x01]);
        Chunk(ms, "IEND", []);
        return ms.ToArray();
    }

    private static void Chunk(Stream stream, string type, byte[] data)
    {
        byte[] length = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, (uint)data.Length);
        stream.Write(length);
        stream.Write(Encoding.ASCII.GetBytes(type));
        stream.Write(data);
        stream.Write(new byte[4]);
    }

    private StripMetadataDialog Dialog() => new(Mock.Of<IContentModelService>(), TestTheme());

    [TestMethod]
    public async Task OnlyTheJpegAndPngFilesAreStripped_AndTheRestAreCounted()
    {
        var png = Path.Combine(_folder, "a.png");
        var text = Path.Combine(_folder, "notes.txt");
        File.WriteAllBytes(png, Png());
        File.WriteAllText(text, "hello");
        StripMetadataDialog dialog = Dialog();
        dialog.Load([png, text]);

        await ShowAsync(dialog);

        Assert.AreEqual(1, dialog.Files.Count);
        StringAssert.Contains(dialog.Details, "1 other skipped");
    }

    [TestMethod]
    public async Task TheFirstEnterArms_TheSecondStrips_AndTheResultIsCounted()
    {
        var withText = Path.Combine(_folder, "with.png");
        var clean = Path.Combine(_folder, "clean.png");
        File.WriteAllBytes(withText, Png());
        File.WriteAllBytes(clean, Png());
        ImageMetadataWriter.Write(withText, new ImageMetadataEdit("Sunset", "Tamas", string.Empty, string.Empty));
        StripMetadataDialog dialog = Dialog();
        dialog.Load([withText, clean]);
        await ShowAsync(dialog);

        await dialog.HandleKeyEventAsync(new KeyEvent('\r', ConsoleKey.Enter, false, false, false));
        Assert.AreEqual(StripPhase.Armed, dialog.Phase);
        Assert.IsTrue(ImageInfoReader.ReadFile(withText).Any(f => f.Group == "Text"), "nothing is removed before the second Enter");

        await dialog.HandleKeyEventAsync(new KeyEvent('\r', ConsoleKey.Enter, false, false, false));
        await dialog.StripTask;
        dialog.OnIdle();

        Assert.AreEqual(StripPhase.Finished, dialog.Phase);
        Assert.AreEqual(1, dialog.Result!.Stripped);
        Assert.AreEqual(1, dialog.Result.Unchanged);
        Assert.AreEqual(0, dialog.Result.Failures.Count);
        Assert.IsFalse(ImageInfoReader.ReadFile(withText).Any(f => f.Group == "Text"));
    }

    [TestMethod]
    public async Task AnyOtherKeyAfterTheFirstEnter_WithdrawsIt()
    {
        var png = Path.Combine(_folder, "a.png");
        File.WriteAllBytes(png, Png());
        StripMetadataDialog dialog = Dialog();
        dialog.Load([png]);
        await ShowAsync(dialog);
        await dialog.HandleKeyEventAsync(new KeyEvent('\r', ConsoleKey.Enter, false, false, false));

        await dialog.HandleKeyEventAsync(new KeyEvent('x', ConsoleKey.X, false, false, false));

        Assert.AreEqual(StripPhase.Choosing, dialog.Phase);
        Assert.IsTrue(dialog.StripTask.IsCompletedSuccessfully, "no strip was started");
    }

    [TestMethod]
    public async Task NothingToStrip_SaysSoAndEnterDoesNothing()
    {
        var text = Path.Combine(_folder, "notes.txt");
        File.WriteAllText(text, "hello");
        StripMetadataDialog dialog = Dialog();
        dialog.Load([text]);
        await ShowAsync(dialog);

        await dialog.HandleKeyEventAsync(new KeyEvent('\r', ConsoleKey.Enter, false, false, false));

        Assert.AreEqual(StripPhase.Choosing, dialog.Phase);
        StringAssert.Contains(dialog.Details, "Nothing to strip");
    }
}
