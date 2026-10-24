using iConsole.Data;
using iConsole.Display;
using iConsole.Interfaces;
using iTerminal.Core.Controls.Lenses;
using iTerminal.Core.Services.Labels;
using Moq;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Test.Controls.Lenses;

[TestClass]
public class FileLabelLensTest
{
    private static readonly string Path1 = Path.Combine(Path.GetTempPath(), "labelled.txt");

    private static Mock<IFileLabelService> Labels(string path, LabelColour colour)
    {
        Mock<IFileLabelService> labels = new();
        labels.Setup(l => l.Get(It.IsAny<string>())).Returns(new FileLabel(colour, []));
        labels.Setup(l => l.Get(path)).Returns(new FileLabel(colour, []));
        return labels;
    }

    private static IEntry Entry(string path)
    {
        Mock<IEntry> entry = new();
        entry.SetupGet(e => e.Url).Returns(new Url(path));
        return entry.Object;
    }

    private static ConxelString Row(string text) => new(text, Color.White, Color.Black);

    [TestMethod]
    public void ALabelledFile_HasItsNameInTheLabelsColour()
    {
        var lens = new FileLabelLens(Labels(Path1, LabelColour.Red).Object);
        var row = Row("labelled.txt");

        lens.DecorateRow(Mock.Of<IContentModel>(), Entry(Path1), row, cursor: false, TestTheme().CurrentColorScheme, VisualState.Normal);

        Assert.IsTrue(row.String.All(cell => cell.ForegroundColor == Color.Red));
    }

    [TestMethod]
    public void AnUnlabelledFile_IsLeftAsItWas()
    {
        var lens = new FileLabelLens(Labels(Path1, LabelColour.None).Object);
        var row = Row("labelled.txt");

        lens.DecorateRow(Mock.Of<IContentModel>(), Entry(Path1), row, cursor: false, TestTheme().CurrentColorScheme, VisualState.Normal);

        Assert.IsTrue(row.String.All(cell => cell.ForegroundColor == Color.White));
    }

    [TestMethod]
    public void TheCursorRow_KeepsItsOwnColours()
    {
        var lens = new FileLabelLens(Labels(Path1, LabelColour.Red).Object);
        var row = Row("labelled.txt");

        lens.DecorateRow(Mock.Of<IContentModel>(), Entry(Path1), row, cursor: true, TestTheme().CurrentColorScheme, VisualState.Normal);

        Assert.IsTrue(row.String.All(cell => cell.ForegroundColor == Color.White));
    }
}
