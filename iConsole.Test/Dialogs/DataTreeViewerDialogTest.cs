using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Interfaces;
using iConsole.Model;
using iConsole.Services;
using iConsole.Test.Common;
using Moq;

namespace iConsole.Test.Dialogs;

[TestClass]
public class DataTreeViewerDialogTest
{
    private static Mock<IContentModelService> ModelsFor(Url document, byte[] content)
    {
        Mock<IMemoryFile> file = new();
        file.SetupGet(f => f.Content).Returns(content);
        Mock<IContentModel> fileSystem = new();
        fileSystem.Setup(f => f.ReadFileAsync(document)).ReturnsAsync(file.Object);
        Mock<IContentModelService> models = new();
        models.Setup(m => m.GetFocusedModel()).Returns(fileSystem.Object);
        return models;
    }

    private static YamlViewerDialog YamlFor(string path, string text)
    {
        var document = new Url(path);
        return new YamlViewerDialog(ModelsFor(document, System.Text.Encoding.UTF8.GetBytes(text)).Object, Mock.Of<IFileToTextConverterService>(), new ThemeService()) { Url = document };
    }

    private static TomlViewerDialog TomlFor(string path, string text)
    {
        var document = new Url(path);
        return new TomlViewerDialog(ModelsFor(document, System.Text.Encoding.UTF8.GetBytes(text)).Object, Mock.Of<IFileToTextConverterService>(), new ThemeService()) { Url = document };
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Yaml_OpensTheDocumentAsATreeWithItsTopLevelItems()
    {
        YamlViewerDialog dialog = YamlFor("file:///config/app.yaml", "a: 1\nb:\n  - x\n  - y\n");

        await dialog.OnInitializeAsync();

        Assert.IsNotNull(dialog.Root);
        Assert.AreEqual(2, dialog.Root.Children.Count);
        Assert.AreEqual("2 top-level item(s)", dialog.Status);
        Assert.IsFalse(dialog.IsTextMode);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Yaml_ASyntaxErrorSaysWhyAndOpensTheText()
    {
        YamlViewerDialog dialog = YamlFor("file:///config/bad.yaml", "a: [1, 2\n");

        await dialog.OnInitializeAsync();

        Assert.IsNull(dialog.Root);
        StringAssert.StartsWith(dialog.Status, "Not valid YAML:");
        Assert.IsTrue(dialog.IsTextMode);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Toml_OpensTheDocumentAsATreeWithItsTables()
    {
        TomlViewerDialog dialog = TomlFor("file:///config/app.toml", "title = \"x\"\n\n[owner]\nname = \"T\"\n");

        await dialog.OnInitializeAsync();

        Assert.IsNotNull(dialog.Root);
        Assert.AreEqual(2, dialog.Root.Children.Count);
        Assert.IsFalse(dialog.IsTextMode);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Toml_ASyntaxErrorSaysWhyAndOpensTheText()
    {
        TomlViewerDialog dialog = TomlFor("file:///config/bad.toml", "a = = 1\n");

        await dialog.OnInitializeAsync();

        Assert.IsNull(dialog.Root);
        StringAssert.StartsWith(dialog.Status, "Not valid TOML:");
        Assert.IsTrue(dialog.IsTextMode);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task F4SwitchesBetweenTheTreeAndTheText()
    {
        YamlViewerDialog dialog = YamlFor("file:///config/app.yaml", "a: 1\n");
        await dialog.OnInitializeAsync();

        await dialog.ToggleTextModeAsync();
        Assert.IsTrue(dialog.IsTextMode);

        await dialog.ToggleTextModeAsync();
        Assert.IsFalse(dialog.IsTextMode);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheExampleFilesOpenAsTrees()
    {
        var yaml = new Url(SampleFiles.Path("example.yaml"));
        var yamlDialog = new YamlViewerDialog(ModelsFor(yaml, SampleFiles.Bytes("example.yaml")).Object, Mock.Of<IFileToTextConverterService>(), new ThemeService()) { Url = yaml };
        var toml = new Url(SampleFiles.Path("example.toml"));
        var tomlDialog = new TomlViewerDialog(ModelsFor(toml, SampleFiles.Bytes("example.toml")).Object, Mock.Of<IFileToTextConverterService>(), new ThemeService()) { Url = toml };

        await yamlDialog.OnInitializeAsync();
        await tomlDialog.OnInitializeAsync();

        Assert.IsNotNull(yamlDialog.Root);
        Assert.IsNotNull(tomlDialog.Root);
        Assert.IsFalse(yamlDialog.IsTextMode);
        Assert.IsFalse(tomlDialog.IsTextMode);
    }
}
