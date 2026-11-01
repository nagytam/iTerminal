using iConsole.Data;
using iConsole.Helper;
using StringAssert = Microsoft.VisualStudio.TestTools.UnitTesting.StringAssert;
using iTerminal.Core.Model.ContentModels;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Links;
using iTerminal.Core.Tasks;
using System.Diagnostics;

namespace iTerminal.Core.Test.Services.Links;

/// <summary>
/// Links on a real disk: making them, showing them, and — the part that matters most — never
/// mistaking one for the folder it leads to when deleting, copying or counting.
/// </summary>
[TestClass]
public class LinkServiceTest
{
    private string _root = null!;

    [TestInitialize]
    public void CreateSandbox()
    {
        _root = Directory.CreateTempSubdirectory("iTerminal.Links.").FullName;
    }

    [TestCleanup]
    public void DeleteSandbox()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string PathOf(params string[] parts) => Path.Combine([_root, .. parts]);

    private string CreateFile(string name, string content = "content")
    {
        var path = PathOf(name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    /// <summary>
    /// A link to a folder that every machine can make: a symbolic link where the account may, and
    /// on Windows without Developer Mode a junction, which needs no privilege and is the same thing
    /// to everything under test.
    /// </summary>
    internal static void CreateFolderLink(string linkPath, string targetPath)
    {
        try
        {
            Directory.CreateSymbolicLink(linkPath, targetPath);
            return;
        }
        catch (Exception exception) when (OperatingSystem.IsWindows() && exception is IOException or UnauthorizedAccessException)
        {
        }

        var startInfo = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "/c", "mklink", "/J", linkPath, targetPath })
        {
            startInfo.ArgumentList.Add(argument);
        }
        using var process = Process.Start(startInfo)!;
        process.WaitForExit();
        Assert.AreEqual(0, process.ExitCode, "mklink /J failed: " + process.StandardError.ReadToEnd());
    }

    /// <summary>Whether this account may create symbolic links; Windows without Developer Mode may not.</summary>
    private bool CanCreateSymbolicLinks()
    {
        try
        {
            var probe = PathOf("probe-link");
            File.CreateSymbolicLink(probe, CreateFile("probe-target"));
            File.Delete(probe);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    #region Creating
    [Timeout(60_000)]
    [TestMethod]
    public async Task HardLink_IsASecondNameForTheSameFile()
    {
        var file = CreateFile("a.txt", "one");
        var link = PathOf("b.txt");

        await new LinkService().CreateAsync(LinkKind.Hard, link, file);

        Assert.AreEqual("one", File.ReadAllText(link));
        File.WriteAllText(link, "two");
        Assert.AreEqual("two", File.ReadAllText(file), "written through one name, read through the other");
        Assert.IsFalse(FileLinkHelper.IsLink(link), "a hard link is a file like any other, not a link to one");

        File.Delete(file);
        Assert.AreEqual("two", File.ReadAllText(link), "the file lives on under its other name");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task HardLink_ToAFolder_IsRefusedInWords()
    {
        Directory.CreateDirectory(PathOf("folder"));

        var exception = await Assert.ThrowsExactlyAsync<IOException>(
            () => new LinkService().CreateAsync(LinkKind.Hard, PathOf("link"), PathOf("folder")));

        StringAssert.Contains(exception.Message, "symbolic link");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ALink_IsNotMadeOverSomethingThatExists_NorToSomethingThatDoesNot()
    {
        var file = CreateFile("a.txt");
        var taken = CreateFile("taken.txt", "keep me");
        var service = new LinkService();

        var overExisting = await Assert.ThrowsExactlyAsync<IOException>(() => service.CreateAsync(LinkKind.Hard, taken, file));
        var toNothing = await Assert.ThrowsExactlyAsync<IOException>(() => service.CreateAsync(LinkKind.Hard, PathOf("b.txt"), PathOf("missing.txt")));
        var intoNoFolder = await Assert.ThrowsExactlyAsync<IOException>(() => service.CreateAsync(LinkKind.Hard, PathOf("no-folder", "b.txt"), file));

        StringAssert.Contains(overExisting.Message, "already exists");
        StringAssert.Contains(toNothing.Message, "does not exist");
        StringAssert.Contains(intoNoFolder.Message, "does not exist");
        Assert.AreEqual("keep me", File.ReadAllText(taken));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task SymbolicLink_ToAFileAndToAFolder_AbsoluteAndRelative()
    {
        if (!CanCreateSymbolicLinks())
        {
            Assert.Inconclusive("This account may not create symbolic links (Windows without Developer Mode).");
        }

        var file = CreateFile(Path.Combine("data", "a.txt"), "one");
        var service = new LinkService();

        await service.CreateAsync(LinkKind.Symbolic, PathOf("file-link.txt"), file);
        await service.CreateAsync(LinkKind.Symbolic, PathOf("folder-link"), PathOf("data"), relative: true);

        Assert.AreEqual("one", File.ReadAllText(PathOf("file-link.txt")));
        Assert.AreEqual(file, FileLinkHelper.GetLinkTarget(PathOf("file-link.txt")));
        Assert.AreEqual("data", FileLinkHelper.GetLinkTarget(PathOf("folder-link")), "relative to the link's own folder");
        Assert.AreEqual("one", File.ReadAllText(PathOf("folder-link", "a.txt")));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheWindowsPrivilegeFailure_SaysWhatToDoAboutIt()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Windows only.");
        }

        var message = LinkService.DescribeSymbolicLinkFailure(new UnauthorizedAccessException("A required privilege is not held by the client."), targetIsFolder: false);

        StringAssert.Contains(message, "Developer Mode");
        StringAssert.Contains(message, "hard link");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void DefaultLinkPath_IsTheOtherPanelsFolder_OrBesideTheTargetUnderAnotherName()
    {
        var target = PathOf("left", "report.pdf");

        Assert.AreEqual(PathOf("right", "report.pdf"), iTerminal.Core.Dialogs.CreateLinkDialog.DefaultLinkPath(target, PathOf("right") + Path.DirectorySeparatorChar));
        Assert.AreEqual(PathOf("left", "report - link.pdf"), iTerminal.Core.Dialogs.CreateLinkDialog.DefaultLinkPath(target, PathOf("left")));
        Assert.AreEqual(PathOf("left", "report - link.pdf"), iTerminal.Core.Dialogs.CreateLinkDialog.DefaultLinkPath(target, null));
    }
    #endregion

    #region Showing
    [Timeout(60_000)]
    [TestMethod]
    public void AListedLink_KnowsWhereItLeads_AndAnOrdinaryEntryDoesNot()
    {
        CreateFile(Path.Combine("data", "a.txt"));
        CreateFolderLink(PathOf("link"), PathOf("data"));

        var listed = new DirectoryInfo(_root).EnumerateFileSystemInfos().Select(info => new Url(info)).ToDictionary(url => url.Name);

        Assert.IsTrue(listed["link"].IsLink);
        Assert.AreEqual(PathType.Folder, listed["link"].PathType, "a link to a folder is still entered like one");
        StringAssert.EndsWith(Path.TrimEndingDirectorySeparator(listed["link"].LinkTarget!), "data");
        Assert.IsFalse(listed["data"].IsLink);

        // Rows restored from the folder cache are built from the path, not from the enumeration.
        Assert.IsTrue(new Url("file://" + PathOf("link") + Path.DirectorySeparatorChar).IsLink);
        Assert.IsFalse(new Url("file://" + PathOf("data") + Path.DirectorySeparatorChar).IsLink);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ALinkRow_ShowsItsTargetInTheNameColumn_AndInTheInfoStrip()
    {
        Directory.CreateDirectory(PathOf("data"));
        CreateFolderLink(PathOf("link"), PathOf("data"));
        var entry = new FileSystemEntry(new DirectoryInfo(PathOf("link")));
        var theme = new iConsole.Test.Core.TestThemeService();

        var row = string.Concat(entry.FormatItemName(120, cursor: false, theme.CurrentColorScheme, VisualState.Normal).Select(c => c.Char));

        StringAssert.StartsWith(row, "link → ");
        StringAssert.Contains(row, "data");
        StringAssert.StartsWith(entry.GetDisplayName(120), "Link: link → ");
    }
    #endregion

    #region Deleting, copying, counting
    [Timeout(60_000)]
    [TestMethod]
    public void DeletingALinkedFolder_RemovesTheLink_AndLeavesWhatItLeadsTo()
    {
        var kept = CreateFile(Path.Combine("data", "keep.txt"), "precious");
        CreateFolderLink(PathOf("link"), PathOf("data"));
        var service = new FileSystemService();

        // As a folder, recursively — what F8 with "Recursively delete folder" ticked asks for.
        var deleted = service.DeleteFolder(new Url("file://" + PathOf("link") + Path.DirectorySeparatorChar), recursive: true, moveToRecycleBin: false);

        Assert.IsTrue(deleted);
        Assert.IsFalse(Directory.Exists(PathOf("link")));
        Assert.AreEqual("precious", File.ReadAllText(kept));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void DeletingALinkedFolder_AsOneEntryOfARecursiveDelete_RemovesOnlyTheLink()
    {
        var kept = CreateFile(Path.Combine("data", "keep.txt"), "precious");
        CreateFolderLink(PathOf("link"), PathOf("data"));

        // DeleteFile is what the delete pipeline calls for every entry the discovery hands it.
        var deleted = new FileSystemService().DeleteFile(new Url(new DirectoryInfo(PathOf("link"))), moveToRecycleBin: false);

        Assert.IsTrue(deleted);
        Assert.IsFalse(Directory.Exists(PathOf("link")));
        Assert.AreEqual("precious", File.ReadAllText(kept));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task DeleteDiscovery_DoesNotWalkIntoALinkedFolder()
    {
        CreateFile(Path.Combine("outside", "keep.txt"), "precious");
        CreateFile(Path.Combine("doomed", "own.txt"));
        CreateFolderLink(PathOf("doomed", "link"), PathOf("outside"));
        var discovery = new FolderDeleteDiscoveryTask(new FileSystemService(), new Url("file://" + PathOf("doomed") + Path.DirectorySeparatorChar));

        await discovery.RunAsync();
        var handedOver = new List<string>();
        await foreach (var url in discovery.Reader.ReadAllAsync())
        {
            handedOver.Add(url.Name);
        }

        // The folder's own file and the link itself — and nothing from behind the link.
        CollectionAssert.AreEquivalent(new[] { "own.txt", "link" }, handedOver);
        Assert.AreEqual(1, discovery.Folders.Count, "the link is not a folder to walk");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task DeleteDiscovery_OfAFolderThatIsItselfALink_FindsNothingToDelete()
    {
        CreateFile(Path.Combine("outside", "keep.txt"), "precious");
        CreateFolderLink(PathOf("link"), PathOf("outside"));
        var discovery = new FolderDeleteDiscoveryTask(new FileSystemService(), new Url("file://" + PathOf("link") + Path.DirectorySeparatorChar));

        await discovery.RunAsync();

        Assert.IsFalse(discovery.Reader.TryRead(out _), "nothing behind the link is the link's to delete");
        Assert.AreEqual(1, discovery.Folders.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ALinkBackToAParent_IsRecognised_AndAFolderCopyThroughItEnds()
    {
        CreateFile(Path.Combine("tree", "a.txt"));
        Directory.CreateDirectory(PathOf("elsewhere"));
        CreateFolderLink(PathOf("tree", "loop"), PathOf("tree"));
        CreateFolderLink(PathOf("tree", "side"), PathOf("elsewhere"));

        Assert.IsTrue(FileLinkHelper.LeadsToAnAncestor(PathOf("tree", "loop")));
        Assert.IsFalse(FileLinkHelper.LeadsToAnAncestor(PathOf("tree", "side")));

        new FileSystemService().CopyAsync(
            new Url("file://" + PathOf("tree") + Path.DirectorySeparatorChar),
            new Url("file://" + PathOf("copy") + Path.DirectorySeparatorChar),
            overwrite: false).Wait(TimeSpan.FromSeconds(30));

        Assert.IsTrue(File.Exists(PathOf("copy", "a.txt")));
        Assert.IsFalse(Directory.Exists(PathOf("copy", "loop")), "the loop is left out rather than copied into itself");
        Assert.IsTrue(Directory.Exists(PathOf("copy", "side")), "any other link is copied as what it leads to");
    }
    #endregion
}
