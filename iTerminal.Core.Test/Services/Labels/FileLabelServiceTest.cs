using iTerminal.Core.Data;
using iTerminal.Core.Services.Labels;
using Microsoft.EntityFrameworkCore;

namespace iTerminal.Core.Test.Services.Labels;

[TestClass]
public class FileLabelServiceTest
{
    private string _folder = string.Empty;
    private string _file = string.Empty;
    private TestDbContextFactory _factory = null!;

    [TestInitialize]
    public async Task Initialize()
    {
        _folder = Path.Combine(Path.GetTempPath(), "FileLabelServiceTest_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_folder);
        _file = Path.Combine(_folder, "report.txt");
        File.WriteAllText(_file, "x");
        _factory = new TestDbContextFactory(Path.Combine(_folder, "test.sqlite"));
        await using var context = _factory.CreateDbContext();
        await context.Database.EnsureCreatedAsync();
    }

    [TestCleanup]
    public void Cleanup()
    {
        // Windows will not delete a SQLite file while a pooled connection to it is open: release the pool first.
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_folder, recursive: true);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task UpdateAsync_GivesTheLabelToTheFile_AndGetReturnsIt()
    {
        var service = new FileLabelService(_factory);

        await service.UpdateAsync([_file], _ => new FileLabel(LabelColour.Red, ["Invoice", "2026"]));
        await service.WhenLoadedAsync();

        var label = service.Get(_file);
        Assert.AreEqual(LabelColour.Red, label.Colour);
        CollectionAssert.AreEqual(new[] { "Invoice", "2026" }, label.Tags.ToList());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheLabelIsKeptInTheDatabase_AndReadBackByAFreshService()
    {
        await new FileLabelService(_factory).UpdateAsync([_file], _ => new FileLabel(LabelColour.Blue, ["draft"]));

        var fresh = new FileLabelService(_factory);
        await fresh.WhenLoadedAsync();

        Assert.AreEqual(LabelColour.Blue, fresh.Get(_file).Colour);
        CollectionAssert.AreEqual(new[] { "draft" }, fresh.Get(_file).Tags.ToList());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AnEmptyLabel_RemovesTheRow()
    {
        var service = new FileLabelService(_factory);
        await service.UpdateAsync([_file], _ => new FileLabel(LabelColour.Green, []));

        await service.UpdateAsync([_file], _ => FileLabel.None);

        Assert.IsTrue(service.Get(_file).IsEmpty);
        await using var context = _factory.CreateDbContext();
        Assert.AreEqual(0, await context.FileLabels.CountAsync());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheChangeSeesWhatTheFileHasNow()
    {
        var service = new FileLabelService(_factory);
        await service.UpdateAsync([_file], _ => new FileLabel(LabelColour.Red, ["keep"]));

        // Only the colour changes; the tags the file had come back in the change.
        await service.UpdateAsync([_file], old => new FileLabel(LabelColour.Yellow, old.Tags));

        var label = service.Get(_file);
        Assert.AreEqual(LabelColour.Yellow, label.Colour);
        CollectionAssert.AreEqual(new[] { "keep" }, label.Tags.ToList());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AFolderAndAPathWithATrailingSeparator_AreTheSameLabel()
    {
        var service = new FileLabelService(_factory);
        await service.UpdateAsync([_folder], _ => new FileLabel(LabelColour.Purple, []));

        Assert.AreEqual(LabelColour.Purple, service.Get(_folder + Path.DirectorySeparatorChar).Colour);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AnUnlabelledPath_IsNone()
    {
        Assert.IsTrue(new FileLabelService(_factory).Get(Path.Combine(_folder, "nothing.txt")).IsEmpty);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Tags_ParseTrimsDropsBlanksAndRepeats()
    {
        CollectionAssert.AreEqual(
            new[] { "Invoice", "2026" },
            FileLabelTags.Parse(" Invoice ,, invoice , 2026 ,").ToList());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Tags_TooMany_OrTooLong_AreRefused()
    {
        Assert.IsNull(FileLabelTags.Validate("a, b"));
        StringAssert.Contains(FileLabelTags.Validate(string.Join(",", Enumerable.Range(0, FileLabelTags.MaxTags + 1).Select(i => "t" + i)))!, "At most");
        StringAssert.Contains(FileLabelTags.Validate(new string('x', FileLabelTags.MaxTagLength + 1))!, "at most");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Colours_ParseAStoredNameAndRefuseAnUnknownOne()
    {
        Assert.AreEqual(LabelColour.Orange, LabelColours.Parse("orange"));
        Assert.AreEqual(LabelColour.None, LabelColours.Parse("magenta-ish"));
        Assert.AreEqual(LabelColour.None, LabelColours.Parse("5"));
    }

    private sealed class TestDbContextFactory(string databasePath) : Microsoft.EntityFrameworkCore.IDbContextFactory<iTerminalDbContext>
    {
        private readonly DbContextOptions<iTerminalDbContext> _options =
            new DbContextOptionsBuilder<iTerminalDbContext>()
                .UseSqlite($"Data Source={databasePath}")
                .Options;

        public iTerminalDbContext CreateDbContext() => new(_options);

        public Task<iTerminalDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}
