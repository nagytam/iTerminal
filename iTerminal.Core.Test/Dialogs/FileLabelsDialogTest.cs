using iConsole.Data;
using iConsole.Services;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Services.Labels;
using Moq;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Test.Dialogs;

[TestClass]
public class FileLabelsDialogTest
{
    private static readonly string File1 = Path.Combine(Path.GetTempPath(), "dialog-a.txt");
    private static readonly string File2 = Path.Combine(Path.GetTempPath(), "dialog-b.txt");

    private static FileLabelsDialog Dialog(IFileLabelService labels) =>
        new(labels, Mock.Of<IBeepService>(), new DateTimeProviderService(), TestTheme());

    private static Mock<IFileLabelService> Labels(params (string Path, FileLabel Label)[] existing)
    {
        Mock<IFileLabelService> labels = new();
        labels.Setup(l => l.WhenLoadedAsync()).Returns(Task.CompletedTask);
        labels.Setup(l => l.Get(It.IsAny<string>())).Returns(FileLabel.None);

        // Moq uses the last setup that matches, so the specific ones come after the default.
        foreach (var (path, label) in existing)
        {
            labels.Setup(l => l.Get(path)).Returns(label);
        }

        return labels;
    }

    [TestMethod]
    public async Task ASingleItem_ShowsWhatItHas()
    {
        var labels = Labels((File1, new FileLabel(LabelColour.Green, ["draft", "2026"])));
        var dialog = Dialog(labels.Object);
        dialog.Load([File1]);

        await ShowAsync(dialog);

        Assert.AreEqual("green", dialog.ColourChoice);
        Assert.AreEqual("draft, 2026", dialog.Tags);
    }

    [TestMethod]
    public async Task Apply_SetsTheColourAndTheTags_ForEachItem()
    {
        var labels = Labels();
        Func<FileLabel, FileLabel>? applied = null;
        labels.Setup(l => l.UpdateAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<Func<FileLabel, FileLabel>>()))
            .Callback<IReadOnlyList<string>, Func<FileLabel, FileLabel>>((_, change) => applied = change)
            .Returns(Task.CompletedTask);
        var dialog = Dialog(labels.Object);
        dialog.Load([File1]);
        await ShowAsync(dialog);
        dialog.ChooseColour("red");
        dialog.Tags = "invoice, 2026";

        dialog.StartSave();
        await dialog.SaveTask!;
        dialog.OnIdle();

        Assert.IsNotNull(applied);
        var label = applied!(FileLabel.None);
        Assert.AreEqual(LabelColour.Red, label.Colour);
        CollectionAssert.AreEqual(new[] { "invoice", "2026" }, label.Tags.ToList());
    }

    [TestMethod]
    public async Task SeveralItems_KeepWhatIsNotChanged()
    {
        var labels = Labels();
        Func<FileLabel, FileLabel>? applied = null;
        labels.Setup(l => l.UpdateAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<Func<FileLabel, FileLabel>>()))
            .Callback<IReadOnlyList<string>, Func<FileLabel, FileLabel>>((_, change) => applied = change)
            .Returns(Task.CompletedTask);
        var dialog = Dialog(labels.Object);
        dialog.Load([File1, File2]);
        await ShowAsync(dialog);
        dialog.ChooseColour("blue");

        dialog.StartSave();
        await dialog.SaveTask!;

        var label = applied!(new FileLabel(LabelColour.Red, ["kept"]));
        Assert.AreEqual(LabelColour.Blue, label.Colour);
        CollectionAssert.AreEqual(new[] { "kept" }, label.Tags.ToList(), "the blank tags keep each item's own");
    }

    [TestMethod]
    public async Task InvalidTags_AreRefusedWithTheReason_AndNothingIsSaved()
    {
        var labels = Labels();
        var dialog = Dialog(labels.Object);
        dialog.Load([File1]);
        await ShowAsync(dialog);
        dialog.Tags = new string('x', FileLabelTags.MaxTagLength + 1);

        Assert.IsFalse(dialog.StartSave());

        StringAssert.Contains(dialog.Message, "at most");
        labels.Verify(l => l.UpdateAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<Func<FileLabel, FileLabel>>()), Times.Never);
    }
}
