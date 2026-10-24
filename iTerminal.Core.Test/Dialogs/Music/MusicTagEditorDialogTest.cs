using System.Text;
using iConsole.Data;
using iConsole.Services;
using iTerminal.Core.Data.Music;
using iTerminal.Core.Dialogs.Music;
using iTerminal.Core.Services.Music;
using Moq;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Test.Dialogs.Music;

/// <summary>The tag editor: prefilled fields, validation, saving through the writer, and the reload after a save.</summary>
[TestClass]
public class MusicTagEditorDialogTest
{
    private string _directory = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _directory = Path.Combine(Path.GetTempPath(), "tagedit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            Directory.Delete(_directory, true);
        }
        catch
        {
        }
    }

    private string Mp3(string title)
    {
        string path = Path.Combine(_directory, "song.mp3");
        byte[] body = Encoding.Unicode.GetBytes(title);
        byte[] frame = [.. "TIT2"u8, 0, 0, 0, (byte)(body.Length + 3), 0, 0, 1, 0xFF, 0xFE, .. body];
        byte[] tag = [.. "ID3"u8, 3, 0, 0, 0, 0, 0, (byte)(frame.Length >> 7 & 0x7F), (byte)(frame.Length & 0x7F), .. frame];
        File.WriteAllBytes(path, [.. tag, 1, 2, 3, 4, 5]);
        return path;
    }

    private static MusicTagSet Tags(params (string Name, string Value)[] tags) => new()
    {
        Title = tags.FirstOrDefault(t => t.Name == "Title").Value ?? string.Empty,
        Artist = tags.FirstOrDefault(t => t.Name == "Artist").Value ?? string.Empty,
        Tags = tags.Select(t => new MusicTag(t.Name, t.Value)).ToList(),
    };

    private static MusicTagEditorDialog Editor(string path, MusicTagSet current, Action? saved = null) =>
        new(path, current, Mock.Of<IBeepService>(), new DateTimeProviderService(), TestTheme(), saved);

    [TestMethod]
    public void TheFieldsArePrefilledWithTheFilesTags()
    {
        var dialog = Editor("x.mp3", Tags(("Title", "Dune"), ("Artist", "Ann"), ("Recorded", "2019-07-01"), ("Track", "4/9"), ("Genre", "Jazz"), ("Comment", "live")));

        var edit = dialog.Edit();

        Assert.AreEqual("Dune", edit.Title);
        Assert.AreEqual("Ann", edit.Artist);
        Assert.AreEqual("2019-07-01", edit.Year, "a file with only a recording date shows it as the year");
        Assert.AreEqual("4/9", edit.Track);
        Assert.AreEqual("Jazz", edit.Genre);
        Assert.AreEqual("live", edit.Comment);
    }

    [TestMethod]
    public void AYearThatIsNotFourDigits_CannotBeSaved()
    {
        var dialog = Editor("x.mp3", MusicTagSet.Empty);
        SetField(dialog, "Year", "20x4");

        Assert.IsFalse(dialog.StartSave());
        Assert.AreEqual("The year is four digits, or blank.", dialog.Message);
    }

    [TestMethod]
    public void ATrackThatIsNotANumber_CannotBeSaved()
    {
        var dialog = Editor("x.mp3", MusicTagSet.Empty);
        SetField(dialog, "Track", "third");

        Assert.IsFalse(dialog.StartSave());
        StringAssert.Contains(dialog.Message, "The track is a number");
    }

    [TestMethod]
    public async Task Enter_SavesThroughTheWriter_AndTheEditorThenCloses_AndTheViewerReloads()
    {
        string path = Mp3("Old");
        bool reloaded = false;
        var dialog = Editor(path, Tags(("Title", "Old")), saved: () => reloaded = true);
        SetField(dialog, "Title", "New title");
        await ShowAsync(dialog);

        await dialog.HandleKeyEventAsync(new KeyEvent('\r', ConsoleKey.Enter, false, false, false));
        await dialog.SaveTask!;
        dialog.OnIdle();

        Assert.AreEqual("New title", MusicTagReader.ReadAll(path).Title);
        Assert.IsTrue(reloaded, "the viewer is told to read the tags again");
        Assert.IsFalse(dialog.IsVisible);
    }

    [TestMethod]
    public async Task AFailedSave_SaysWhy_AndKeepsTheEditorOpen()
    {
        string missing = Path.Combine(_directory, "gone.mp3");
        var dialog = Editor(missing, MusicTagSet.Empty);
        SetField(dialog, "Title", "x");
        await ShowAsync(dialog);

        dialog.StartSave();
        await dialog.SaveTask!;
        dialog.OnIdle();

        StringAssert.StartsWith(dialog.Message, "Could not save the tags");
        Assert.IsTrue(dialog.IsVisible);
    }

    [TestMethod]
    public void TheDialogFieldsAreEditableTextBoxes()
    {
        var dialog = Editor("x.mp3", MusicTagSet.Empty);

        Assert.AreEqual(7, dialog.Controls.OfType<iConsole.Controls.TextBox>().Count());
    }

    private static void SetField(MusicTagEditorDialog dialog, string label, string text) =>
        dialog.Controls.OfType<iConsole.Controls.TextBox>().Single(b => b.Label == label).Text = text;

    [TestMethod]
    public async Task E_OnAFileTagsCannotBeWritten_SaysSoAndOpensNoEditor()
    {
        string wav = Path.Combine(_directory, "song.wav");
        File.WriteAllBytes(wav, [0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0]);
        var viewer = new MusicFileViewerDialog(
            Mock.Of<IMusicPlayerService>(),
            Mock.Of<IAudioDeviceService>(),
            new MusicMetadataService(),
            Mock.Of<IBeepService>(),
            new DateTimeProviderService(),
            TestTheme())
        {
            FilePath = wav,
        };
        await ShowAsync(viewer);

        await viewer.HandleKeyEventAsync(new KeyEvent('e', ConsoleKey.E, false, false, false));

        Assert.AreEqual("Tags can be written to MP3 and FLAC files.", viewer.StatusForTest);
    }
}
