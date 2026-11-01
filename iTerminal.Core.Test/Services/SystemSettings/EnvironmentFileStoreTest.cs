using iTerminal.Core.Services.SystemSettings;

namespace iTerminal.Core.Test.Services.SystemSettings;

[TestClass]
public class EnvironmentFileStoreTest
{
    private string _folder = null!;
    private string _path = null!;

    [TestInitialize]
    public void Create()
    {
        _folder = Path.Combine(Path.GetTempPath(), "iterminal-env-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
        _path = Path.Combine(_folder, "environment");
    }

    [TestCleanup]
    public void Delete() => Directory.Delete(_folder, recursive: true);

    [Timeout(60_000)]
    [TestMethod]
    public void AMissingFileHasNoVariables()
    {
        Assert.AreEqual(0, new EnvironmentFileStore(_path).Read().Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ReadingSkipsCommentsAndBlankLinesAndUnquotes()
    {
        File.WriteAllText(_path, "# a comment\n\nPATH=\"/usr/bin:/bin\"\nexport EDITOR='vim'\nPLAIN=value with spaces\nnonsense line\n");

        var variables = new EnvironmentFileStore(_path).Read();

        CollectionAssert.AreEqual(
            new[] { "EDITOR=vim", "PATH=/usr/bin:/bin", "PLAIN=value with spaces" },
            variables.Select(v => $"{v.Name}={v.Value}").ToArray());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void SettingANewVariableKeepsEveryOtherLineAsItWas()
    {
        File.WriteAllText(_path, "# set by the distribution\nPATH=\"/usr/bin\"\n\nLANG=en_US.UTF-8\n");

        new EnvironmentFileStore(_path).Set("EDITOR", "vim");

        Assert.AreEqual("# set by the distribution\nPATH=\"/usr/bin\"\n\nLANG=en_US.UTF-8\nEDITOR=vim\n", File.ReadAllText(_path));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void SettingAnExistingVariableReplacesItsLineInPlace()
    {
        File.WriteAllText(_path, "A=1\nB=2\nC=3\n");

        new EnvironmentFileStore(_path).Set("B", "two");

        Assert.AreEqual("A=1\nB=two\nC=3\n", File.ReadAllText(_path));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ARepeatedDefinitionIsCollapsedToOne()
    {
        File.WriteAllText(_path, "A=1\nB=2\nA=3\n");

        new EnvironmentFileStore(_path).Set("A", "9");

        Assert.AreEqual("A=9\nB=2\n", File.ReadAllText(_path));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AValueWithSpacesQuotesOrHashesIsQuotedAndReadsBackIdentical()
    {
        var store = new EnvironmentFileStore(_path);
        string[] values = ["has space", "say \"hi\"", "back\\slash", "# not a comment", "it's", string.Empty];

        foreach (var (value, index) in values.Select((v, i) => (v, i)))
        {
            store.Set("V" + index, value);
        }

        var read = store.Read().ToDictionary(v => v.Name, v => v.Value);
        for (var i = 0; i < values.Length; i++)
        {
            Assert.AreEqual(values[i], read["V" + i], $"value {i}");
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public void RemovingDeletesOnlyThatVariable()
    {
        File.WriteAllText(_path, "# keep\nA=1\nB=2\n");
        var store = new EnvironmentFileStore(_path);

        store.Remove("A");

        Assert.AreEqual("# keep\nB=2\n", File.ReadAllText(_path));
        store.Remove("MISSING");
        Assert.AreEqual("# keep\nB=2\n", File.ReadAllText(_path), "removing nothing leaves the file alone");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheFolderIsCreatedOnTheFirstWrite()
    {
        var nested = Path.Combine(_folder, "environment.d", "60-test.conf");

        new EnvironmentFileStore(nested).Set("A", "1");

        Assert.AreEqual("A=1\n", File.ReadAllText(nested));
        Assert.IsFalse(File.Exists(nested + ".tmp"), "the temporary file is gone");
    }
}
