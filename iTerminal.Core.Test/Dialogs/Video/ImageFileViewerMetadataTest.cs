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
    [Timeout(60_000)]
    [TestMethod]
    public async Task I_ShowsTheMetadataInPlaceOfThePicture_AndAgainHidesIt()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + new string('m', 42) + ".png");
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

    [Timeout(60_000)]
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

    [Timeout(60_000)]
    [TestMethod]
    [DoNotParallelize]
    public async Task TheGroupAndFieldNamesFollowTheChosenLanguage()
    {
        iConsole.Localization.Localizer localizer = new([new iConsole.Localization.ResourceLocalizationSource(
            new System.Resources.ResourceManager("iTerminal.Core.Localization.Resources.AppStrings", typeof(ImageFileViewerDialog).Assembly))]);
        Assert.IsTrue(localizer.SetLanguage("de"));
        using IDisposable language = iConsole.Localization.UiText.Scope(localizer);
        string path = Path.Combine(Path.GetTempPath(), "meta-" + Guid.NewGuid().ToString("N") + ".png");
        File.WriteAllBytes(path, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0, 0, 0, 0, 0]);
        try
        {
            var dialog = new ImageFileViewerDialog(Mock.Of<IVideoDecoderFactory>(), Mock.Of<IBeepService>(), new DateTimeProviderService(), TestTheme()) { FilePath = path };
            await ShowAsync(dialog);

            Assert.IsTrue(dialog.MetadataLines().Any(l => l.StartsWith("── Datei ", StringComparison.Ordinal)), string.Join("|", dialog.MetadataLines()));
            Assert.IsTrue(dialog.MetadataLines().Any(l => l.StartsWith("  Größe ", StringComparison.Ordinal)), string.Join("|", dialog.MetadataLines()));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheMetadataHeaderAndEditHintFollowEveryChosenLanguage()
    {
        string path = Path.Combine(Path.GetTempPath(), "m" + Guid.NewGuid().ToString("N")[..8] + ".png");
        File.WriteAllBytes(path, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0, 0, 0, 0, 0]);
        try
        {
            foreach (string code in new[] { "de", "fr", "es", "it", "hu" })
            {
                iConsole.Localization.Localizer localizer = new([new iConsole.Localization.ResourceLocalizationSource(
                    new System.Resources.ResourceManager("iTerminal.Core.Localization.Resources.AppStrings", typeof(ImageFileViewerDialog).Assembly))]);
                Assert.IsTrue(localizer.SetLanguage(code));
                using IDisposable language = iConsole.Localization.UiText.Scope(localizer);

                Assert.IsTrue(localizer.TryGet("Image.Metadata", out string metadata));
                Assert.IsTrue(localizer.TryGet("Image.EditText", out string editText));
                Assert.AreNotEqual("metadata", metadata, code);
                Assert.AreNotEqual("e:edit text", editText, code);

                ImageFileViewerDialog dialog = new(Mock.Of<IVideoDecoderFactory>(), Mock.Of<IBeepService>(), new DateTimeProviderService(), TestTheme()) { FilePath = path };
                iConsole.Controls.Control root = await ShowAsync(dialog);
                try
                {
                    await dialog.HandleKeyEventAsync(new KeyEvent('i', ConsoleKey.I, false, false, false));

                    iConsole.Test.Core.ScreenBuffer screen = iConsole.Test.Core.ScreenBuffer.Render(root, 120, 40);
                    Assert.IsTrue(screen.Contains(Path.GetFileName(path)), $"{code}: {screen}");
                    Assert.IsTrue(screen.Contains(metadata), $"{code}: {screen}");
                    Assert.IsTrue(screen.Contains(editText), $"{code}: {screen}");
                }
                finally
                {
                    dialog.Close();
                }
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Timeout(60_000)]
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

    [Timeout(60_000)]
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
