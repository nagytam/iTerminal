using iConsole.Data;
using iConsole.Services;
using iTerminal.Core.Dialogs.Video;
using iTerminal.Core.Services.Images;
using iTerminal.Core.Video;
using Moq;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Test.Dialogs.Video;

[TestClass]
public class ImageFileViewerMetadataTest
{
    [TestMethod]
    public async Task I_ShowsTheMetadataInPlaceOfThePicture_AndAgainHidesIt()
    {
        string path = Path.Combine(Path.GetTempPath(), "meta-" + Guid.NewGuid().ToString("N") + ".png");
        File.WriteAllBytes(path, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0, 0, 0, 0, 0]);
        try
        {
            var dialog = new ImageFileViewerDialog(Mock.Of<IVideoDecoderFactory>(), Mock.Of<IBeepService>(), new DateTimeProviderService(), TestTheme()) { FilePath = path };
            await ShowAsync(dialog);

            await dialog.HandleKeyEventAsync(new KeyEvent('i', ConsoleKey.I, false, false, false));

            Assert.IsTrue(dialog.ShowingMetadata);
            CollectionAssert.Contains(dialog.MetadataLines(), "── File ──────────────────────────────");

            await dialog.HandleKeyEventAsync(new KeyEvent('i', ConsoleKey.I, false, false, false));
            Assert.IsFalse(dialog.ShowingMetadata);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public async Task AFileWithNoMetadata_SaysSoInItsView()
    {
        string path = Path.Combine(Path.GetTempPath(), "meta-" + Guid.NewGuid().ToString("N") + ".png");
        File.WriteAllBytes(path, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        try
        {
            var dialog = new ImageFileViewerDialog(Mock.Of<IVideoDecoderFactory>(), Mock.Of<IBeepService>(), new DateTimeProviderService(), TestTheme()) { FilePath = path };
            await ShowAsync(dialog);

            CollectionAssert.Contains(dialog.MetadataLines().Where(l => !string.IsNullOrEmpty(l)).ToList(), "  Format           PNG");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public async Task E_OutsideTheMetadataView_IsNotHandled()
    {
        string path = Path.Combine(Path.GetTempPath(), "meta-" + Guid.NewGuid().ToString("N") + ".png");
        File.WriteAllBytes(path, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        try
        {
            var dialog = new ImageFileViewerDialog(Mock.Of<IVideoDecoderFactory>(), Mock.Of<IBeepService>(), new DateTimeProviderService(), TestTheme()) { FilePath = path };
            await ShowAsync(dialog);

            Assert.IsFalse(await dialog.HandleKeyEventAsync(new KeyEvent('e', ConsoleKey.E, false, false, false)));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public async Task E_InTheMetadataView_OpensTheTextEditor()
    {
        string path = Path.Combine(Path.GetTempPath(), "meta-" + Guid.NewGuid().ToString("N") + ".png");
        File.WriteAllBytes(path, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        try
        {
            var dialog = new ImageFileViewerDialog(Mock.Of<IVideoDecoderFactory>(), Mock.Of<IBeepService>(), new DateTimeProviderService(), TestTheme()) { FilePath = path };
            await ShowAsync(dialog);
            await dialog.HandleKeyEventAsync(new KeyEvent('i', ConsoleKey.I, false, false, false));

            Assert.IsTrue(await dialog.HandleKeyEventAsync(new KeyEvent('e', ConsoleKey.E, false, false, false)));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
