using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Services;
using iTerminal.Core.Services.FileAssociations;
using Moq;

namespace iTerminal.Core.Test.Services.FileAssociations;

[TestClass]
public class FileAssociationTest
{
    private static readonly string FilePath = Path.Combine(Path.GetTempPath(), "My Photos", "summer day.JPG");

    private static FileAssociation Create(
        string name,
        string patterns,
        FileAssociationKind kind = FileAssociationKind.Open,
        string command = "viewer",
        bool inTerminal = false) =>
        new(Guid.NewGuid().ToString("N"), name, patterns, command, kind, inTerminal);

    #region The command line
    [TestMethod]
    public void BuildCommandLine_WithoutAPlaceholder_PutsTheFileLast_AsOneArgument()
    {
        var commandLine = FileAssociation.BuildCommandLine("viewer --fit", FilePath);

        Assert.AreEqual("viewer", commandLine!.Value.FileName);
        CollectionAssert.AreEqual(new[] { "--fit", FilePath }, commandLine.Value.Arguments.ToArray());
    }

    [TestMethod]
    public void BuildCommandLine_ReplacesThePlaceholders_InsideTheirWords()
    {
        var commandLine = FileAssociation.BuildCommandLine("tool --in=%f --dir %d --name %n --out %b.png 100%%", FilePath);

        CollectionAssert.AreEqual(
            new[]
            {
                "--in=" + FilePath,
                "--dir", Path.GetDirectoryName(FilePath)!,
                "--name", "summer day.JPG",
                "--out", "summer day.png",
                "100%",
            },
            commandLine!.Value.Arguments.ToArray());
    }

    [TestMethod]
    public void BuildCommandLine_QuotesKeepAProgramPathWithSpacesTogether_AndBackslashesAreLiteral()
    {
        var commandLine = FileAssociation.BuildCommandLine(@"""C:\Program Files\Paint\paint.exe"" ""a b"" %f", FilePath);

        Assert.AreEqual(@"C:\Program Files\Paint\paint.exe", commandLine!.Value.FileName);
        CollectionAssert.AreEqual(new[] { "a b", FilePath }, commandLine.Value.Arguments.ToArray());
    }

    [TestMethod]
    public void BuildCommandLine_AFileNameCannotAddArguments()
    {
        // No shell reads the command: a name with quotes, blanks and an ampersand is one argument.
        var hostile = Path.Combine(Path.GetTempPath(), "a\" --delete & calc.txt");

        var commandLine = FileAssociation.BuildCommandLine("viewer %f", hostile);

        CollectionAssert.AreEqual(new[] { hostile }, commandLine!.Value.Arguments.ToArray());
    }

    [TestMethod]
    public void BuildCommandLine_AnUnknownPercentSequence_IsLeftAsWritten()
    {
        var commandLine = FileAssociation.BuildCommandLine("tool %x %", FilePath);

        CollectionAssert.AreEqual(new[] { "%x", "%", FilePath }, commandLine!.Value.Arguments.ToArray());
    }

    [TestMethod]
    public void BuildCommandLine_NothingToRun_IsNull()
    {
        Assert.IsNull(FileAssociation.BuildCommandLine("   ", FilePath));
        Assert.IsNull(FileAssociation.BuildCommandLine(null, FilePath));
    }
    #endregion

    #region Matching
    [TestMethod]
    public void Matches_ByPattern_IgnoringCase()
    {
        var association = Create("Viewer", "*.jpg;*.png");

        Assert.IsTrue(association.Matches("summer day.JPG"));
        Assert.IsTrue(association.Matches("icon.png"));
        Assert.IsFalse(association.Matches("notes.txt"));
        Assert.IsTrue(Create("Anything", "*").Matches("Makefile"));
    }

    [TestMethod]
    public void PatternFor_IsTheExtension_OrTheWholeNameWithoutOne()
    {
        Assert.AreEqual("*.jpg", FileAssociation.PatternFor("summer day.JPG"));
        Assert.AreEqual("Makefile", FileAssociation.PatternFor("Makefile"));
    }
    #endregion

    #region The service
    [TestMethod]
    public void Save_PutsTheNewProgramFirst_SoItIsTheDefault()
    {
        var service = new FileAssociationService(new SettingsService());
        var first = Create("Paint", "*.jpg");
        var second = Create("Viewer", "*.jpg;*.png");

        service.Save(first);
        service.Save(second);

        Assert.AreEqual(second, service.GetDefault("a.jpg", FileAssociationKind.Open));
        CollectionAssert.AreEqual(new[] { second, first }, service.GetFor("a.jpg", FileAssociationKind.Open).ToArray());
        CollectionAssert.AreEqual(new[] { second }, service.GetFor("a.png", FileAssociationKind.Open).ToArray());
        Assert.IsNull(service.GetDefault("a.txt", FileAssociationKind.Open));
    }

    [TestMethod]
    public void OpenAndEdit_AreSeparateLists()
    {
        var service = new FileAssociationService(new SettingsService());
        var editor = Create("Vim", "*.txt", FileAssociationKind.Edit, "vim", inTerminal: true);
        service.Save(editor);

        Assert.IsNull(service.GetDefault("a.txt", FileAssociationKind.Open));
        Assert.AreEqual(editor, service.GetDefault("a.txt", FileAssociationKind.Edit));
    }

    [TestMethod]
    public void Save_WithAnExistingId_ReplacesItWhereItStands()
    {
        var service = new FileAssociationService(new SettingsService());
        var first = Create("Paint", "*.jpg");
        var second = Create("Viewer", "*.jpg");
        service.Save(first);
        service.Save(second);

        service.Save(first with { Name = "Paint 3D", Command = "paint3d" });

        CollectionAssert.AreEqual(new[] { "Viewer", "Paint 3D" }, service.GetAll().Select(a => a.Name).ToArray());
    }

    [TestMethod]
    public void MakeDefault_MovesItAheadOfTheOthers_AndRemove_TakesItOut()
    {
        var service = new FileAssociationService(new SettingsService());
        var first = Create("Paint", "*.jpg");
        var second = Create("Viewer", "*.jpg");
        service.Save(first);
        service.Save(second);
        var version = service.Version;

        Assert.IsTrue(service.MakeDefault(first.Id));
        Assert.AreEqual(first, service.GetDefault("a.jpg", FileAssociationKind.Open));
        Assert.AreNotEqual(version, service.Version);

        Assert.IsTrue(service.Remove(first.Id));
        Assert.IsFalse(service.Remove(first.Id));
        Assert.AreEqual(second, service.GetDefault("a.jpg", FileAssociationKind.Open));
        Assert.IsFalse(service.MakeDefault("no such id"));
    }

    [TestMethod]
    public void TheList_IsKeptInTheSettings_AndReadBackByAnotherInstance()
    {
        var settings = new SettingsService();
        var association = Create("Vim", "*.txt;*.md", FileAssociationKind.Edit, "vim %f", inTerminal: true);
        new FileAssociationService(settings).Save(association);

        var reopened = new FileAssociationService(settings);

        Assert.AreEqual(association, reopened.GetDefault("readme.md", FileAssociationKind.Edit));
    }

    [TestMethod]
    public void AnUnreadableSetting_IsNoAssociations_NotAnException()
    {
        var settings = new SettingsService();
        settings.Set(FileAssociationService.SettingsKey, "{ not json");
        var service = new FileAssociationService(settings);

        Assert.AreEqual(0, service.GetAll().Count);
        Assert.IsNull(service.GetDefault("a.txt", FileAssociationKind.Open));

        service.Save(Create("Viewer", "*.txt"));
        Assert.AreEqual(1, service.GetAll().Count);
    }
    #endregion

    #region The runner
    private sealed class RecordingLauncher(LaunchOutcome outcome) : IFileLauncher
    {
        public List<(FileAssociation? Association, string Path)> Launches { get; } = [];

        public Task<LaunchOutcome> LaunchAsync(FileAssociation association, string filePath, CancellationToken cancellationToken = default)
        {
            Launches.Add((association, filePath));
            return Task.FromResult(outcome);
        }

        public Task<LaunchOutcome> OpenWithSystemDefaultAsync(string filePath, CancellationToken cancellationToken = default)
        {
            Launches.Add((null, filePath));
            return Task.FromResult(outcome);
        }

        public Task<LaunchOutcome> OpenUrlAsync(string url, CancellationToken cancellationToken = default)
        {
            Launches.Add((null, url));
            return Task.FromResult(outcome);
        }
    }

    private static IContentModelService NoPanels()
    {
        var service = new Mock<IContentModelService>();
        service.Setup(s => s.ContentModels).Returns([]);
        return service.Object;
    }

    [TestMethod]
    public async Task Start_AProgramThatCannotStart_IsReportedAsANotification()
    {
        var launcher = new RecordingLauncher(LaunchOutcome.Failed("viewer was not found on the PATH."));
        var notifications = new NotificationService();
        var runner = new FileAssociationRunner(new FileAssociationService(new SettingsService()), launcher, NoPanels(), notifications);

        await runner.Start(Create("Viewer", "*.jpg"), FilePath);

        var notification = notifications.Active.Single();
        Assert.AreEqual(NotificationSeverity.Error, notification.Severity);
        Assert.AreEqual("Could not open summer day.JPG", notification.Title);
        Assert.AreEqual("viewer was not found on the PATH.", notification.Message);
    }

    [TestMethod]
    public async Task Start_AProgramThatStarts_SaysNothing()
    {
        var launcher = new RecordingLauncher(LaunchOutcome.Success);
        var notifications = new NotificationService();
        var runner = new FileAssociationRunner(new FileAssociationService(new SettingsService()), launcher, NoPanels(), notifications);

        await runner.Start(Create("Viewer", "*.jpg"), FilePath);
        await runner.StartSystemDefault(FilePath);

        Assert.AreEqual(2, launcher.Launches.Count);
        Assert.IsNull(launcher.Launches[1].Association);
        Assert.AreEqual(0, notifications.History.Count);
    }

    [TestMethod]
    public void DefaultFor_IsOnlyForAFileOnTheLocalDisk()
    {
        var folder = Directory.CreateTempSubdirectory("iTerminal.Assoc.").FullName;
        try
        {
            var file = Path.Combine(folder, "a.txt");
            File.WriteAllText(file, "x");
            var associations = new FileAssociationService(new SettingsService());
            var association = Create("Viewer", "*.txt");
            associations.Save(association);
            var runner = new FileAssociationRunner(associations, new RecordingLauncher(LaunchOutcome.Success), NoPanels());

            Assert.AreEqual(association, runner.DefaultFor(new Url("file://" + file), FileAssociationKind.Open));
            Assert.IsNull(runner.DefaultFor(new Url("file://" + Path.Combine(folder, "gone.txt")), FileAssociationKind.Open), "a file that is not there");
            Assert.IsNull(runner.DefaultFor(new Url("file://" + folder + Path.DirectorySeparatorChar), FileAssociationKind.Open), "a folder");
            Assert.IsNull(runner.DefaultFor(new Url("memdrive://a.txt"), FileAssociationKind.Open), "another drive");
            Assert.IsNull(runner.DefaultFor(null, FileAssociationKind.Open));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [TestMethod]
    public async Task Launcher_StartsARealProgram_AndSaysWhyItCouldNotStartAMissingOne()
    {
        var launcher = new FileLauncher(Mock.Of<iTerminal.Core.Services.Shell.IInteractiveShellService>());
        var harmless = OperatingSystem.IsWindows() ? "cmd /c rem %f" : "true";

        var started = await launcher.LaunchAsync(Create("Harmless", "*", command: harmless), FilePath);
        var missing = await launcher.LaunchAsync(Create("Missing", "*", command: "no-such-program-4f1c %f"), FilePath);

        Assert.IsTrue(started.Started, started.Failure);
        Assert.IsFalse(missing.Started);
        Assert.AreEqual("no-such-program-4f1c was not found on the PATH.", missing.Failure);
    }

    [TestMethod]
    public async Task Launcher_ATerminalProgram_IsLentTheTerminal_WithoutTheShellsBanner()
    {
        iTerminal.Core.Services.Shell.ShellLaunch? launch = null;
        var shell = new Mock<iTerminal.Core.Services.Shell.IInteractiveShellService>();
        shell.Setup(s => s.RunAsync(It.IsAny<iTerminal.Core.Services.Shell.ShellLaunch>(), It.IsAny<CancellationToken>()))
            .Callback<iTerminal.Core.Services.Shell.ShellLaunch, CancellationToken>((l, _) => launch = l)
            .ReturnsAsync(new iTerminal.Core.Services.Shell.ShellOutcome(0, null));
        var program = OperatingSystem.IsWindows() ? "cmd" : "sh";

        var outcome = await new FileLauncher(shell.Object).LaunchAsync(Create("Editor", "*", FileAssociationKind.Edit, program + " -x", inTerminal: true), FilePath);

        Assert.IsTrue(outcome.Started);
        Assert.IsNotNull(launch);
        CollectionAssert.AreEqual(new[] { "-x", FilePath }, launch.Arguments.ToArray());
        Assert.AreEqual(string.Empty, launch.Banner, "an editor has no exit to type");
        Assert.AreEqual(Path.GetDirectoryName(FilePath), launch.WorkingDirectory);
    }

    [TestMethod]
    public void ResolveProgram_FindsAProgramOnThePath_AndNotOneThatIsNotThere()
    {
        var known = OperatingSystem.IsWindows() ? "cmd" : "sh";

        Assert.IsNotNull(FileLauncher.ResolveProgram(known));
        Assert.IsNull(FileLauncher.ResolveProgram("no-such-program-" + Guid.NewGuid().ToString("N")));
        Assert.IsNull(FileLauncher.ResolveProgram(" "));
    }
    #endregion
}
