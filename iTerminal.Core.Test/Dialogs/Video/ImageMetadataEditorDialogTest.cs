using System.Buffers.Binary;
using System.Text;
using iConsole.Data;
using iConsole.Services;
using iTerminal.Core.Dialogs.Video;
using iTerminal.Core.Services.Images;
using Moq;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Test.Dialogs.Video;

[TestClass]
public class ImageMetadataEditorDialogTest
{
    private string _path = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _path = Path.Combine(Path.GetTempPath(), "imageedit-" + Guid.NewGuid().ToString("N") + ".png");
        File.WriteAllBytes(_path, Png());
    }

    [TestCleanup]
    public void Cleanup()
    {
        foreach (string leftover in Directory.GetFiles(Path.GetTempPath(), Path.GetFileNameWithoutExtension(_path) + "*"))
        {
            File.Delete(leftover);
        }
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

    private ImageMetadataEditorDialog Editor(ImageMetadataEdit current, Action? saved = null) =>
        new(_path, current, Mock.Of<IBeepService>(), new DateTimeProviderService(), TestTheme(), saved);

    [Timeout(60_000)]
    [TestMethod]
    public void TheFieldsArePrefilledWithTheFilesText()
    {
        ImageMetadataEditorDialog dialog = Editor(new ImageMetadataEdit("Sunset", "Tamas", "© 2026", "Lake"));

        Assert.AreEqual(new ImageMetadataEdit("Sunset", "Tamas", "© 2026", "Lake"), dialog.Edit());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Enter_WritesTheTextAndCallsBackThenCloses()
    {
        bool saved = false;
        ImageMetadataEditorDialog dialog = Editor(new ImageMetadataEdit("Sunset", "Tamas", string.Empty, string.Empty), () => saved = true);
        await ShowAsync(dialog);

        await dialog.HandleKeyEventAsync(new KeyEvent('\r', ConsoleKey.Enter, false, false, false));
        await dialog.SaveTask!;
        dialog.OnIdle();

        Assert.IsTrue(saved);
        Assert.IsFalse(dialog.IsSaving);
        Assert.AreEqual(string.Empty, dialog.Message);
        Assert.AreEqual("Sunset", ImageInfoReader.ReadFile(_path).Single(f => f.Name == "Description").Value);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AFailedSave_SaysSoAndDoesNotCallBack()
    {
        string missing = Path.Combine(Path.GetTempPath(), "missing-" + Guid.NewGuid().ToString("N") + ".png");
        bool saved = false;
        ImageMetadataEditorDialog dialog = new(missing, ImageMetadataEdit.Empty, Mock.Of<IBeepService>(), new DateTimeProviderService(), TestTheme(), () => saved = true);
        await ShowAsync(dialog);

        await dialog.HandleKeyEventAsync(new KeyEvent('\r', ConsoleKey.Enter, false, false, false));
        await dialog.SaveTask!;
        dialog.OnIdle();

        Assert.IsFalse(saved);
        StringAssert.StartsWith(dialog.Message, "Could not save the text:");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AFileThatCannotTakeText_IsRefusedWithAMessage()
    {
        string gif = Path.Combine(Path.GetTempPath(), "imageedit-" + Guid.NewGuid().ToString("N") + ".gif");
        File.WriteAllBytes(gif, Encoding.ASCII.GetBytes("GIF89a"));
        try
        {
            ImageMetadataEditorDialog dialog = new(gif, ImageMetadataEdit.Empty, Mock.Of<IBeepService>(), new DateTimeProviderService(), TestTheme());
            await ShowAsync(dialog);

            Assert.IsFalse(dialog.StartSave());
            Assert.AreEqual("Text can only be written for JPEG and PNG files.", dialog.Message);
            CollectionAssert.AreEqual(Encoding.ASCII.GetBytes("GIF89a"), File.ReadAllBytes(gif));
        }
        finally
        {
            File.Delete(gif);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Escape_ClosesWithoutSaving()
    {
        ImageMetadataEditorDialog dialog = Editor(new ImageMetadataEdit("Changed", string.Empty, string.Empty, string.Empty));
        await ShowAsync(dialog);
        byte[] before = File.ReadAllBytes(_path);

        await dialog.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.Escape, false, false, false));

        Assert.IsNull(dialog.SaveTask);
        CollectionAssert.AreEqual(before, File.ReadAllBytes(_path));
    }
}
