using iTerminal.Core.Services.AccessControl;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace iTerminal.Core.Test.Services.AccessControl;

/// <summary>NTFS lists, against real files in a temporary folder the test owns. Skipped off Windows.</summary>
[TestClass]
[SupportedOSPlatform("windows")]
public class WindowsAccessControlServiceTest
{
    private static string Users() =>
        new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null).Translate(typeof(NTAccount)).Value;

    private static string Guests() =>
        new SecurityIdentifier(WellKnownSidType.BuiltinGuestsSid, null).Translate(typeof(NTAccount)).Value;

    private static void OnWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("NTFS access control lists exist on Windows only.");
        }
    }

    private static string NewFile()
    {
        var file = Path.Combine(Directory.CreateTempSubdirectory("acl").FullName, "f.txt");
        File.WriteAllText(file, "x");
        return file;
    }

    private static void Cleanup(string file) => Directory.Delete(Path.GetDirectoryName(file)!, recursive: true);

    [Timeout(60_000)]
    [TestMethod]
    public void ARealFileListsItsOwnerAndEntries()
    {
        OnWindows();
        var file = NewFile();
        try
        {
            var info = new WindowsAccessControlService().Read(file);

            Assert.IsFalse(string.IsNullOrEmpty(info.Owner));
            Assert.IsTrue(info.Entries.Count > 0);
            Assert.IsTrue(info.SupportsDeny);
            Assert.IsTrue(info.CanChangeInheritance);
            CollectionAssert.AreEqual(new[] { "Full control", "Modify", "Read & execute", "Read", "Write" }, info.Presets.ToArray());
        }
        finally
        {
            Cleanup(file);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public void SetAddsAnExplicitEntryAndASecondSetReplacesIt()
    {
        OnWindows();
        var file = NewFile();
        try
        {
            var service = new WindowsAccessControlService();
            var guests = Guests();

            service.Set(file, guests, AclKind.Allow, "Read", inheritToChildren: false);
            var first = service.Read(file).Entries.Single(e => e.Principal == guests && !e.Inherited && e.Kind == AclKind.Allow);
            service.Set(file, guests, AclKind.Allow, "Modify", inheritToChildren: false);
            var entries = service.Read(file).Entries.Where(e => e.Principal == guests && !e.Inherited && e.Kind == AclKind.Allow).ToList();

            Assert.AreEqual("Read", first.Rights);
            Assert.AreEqual(1, entries.Count);
            Assert.AreEqual("Modify", entries[0].Rights);
            Assert.IsTrue(entries[0].IsPreset);
        }
        finally
        {
            Cleanup(file);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ADenyEntryIsKeptApartFromAnAllowOneAndRemoveTakesOneKind()
    {
        OnWindows();
        var file = NewFile();
        try
        {
            var service = new WindowsAccessControlService();
            var guests = Guests();
            service.Set(file, guests, AclKind.Allow, "Read", false);
            service.Set(file, guests, AclKind.Deny, "Write", false);

            service.Remove(file, guests, AclKind.Deny);

            var remaining = service.Read(file).Entries.Where(e => e.Principal == guests && !e.Inherited).ToList();
            Assert.AreEqual(1, remaining.Count);
            Assert.AreEqual(AclKind.Allow, remaining[0].Kind);
        }
        finally
        {
            Cleanup(file);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ARemovalOfWhatIsNotThereSaysSo()
    {
        OnWindows();
        var file = NewFile();
        try
        {
            var exception = Assert.ThrowsExactly<ArgumentException>(
                () => new WindowsAccessControlService().Remove(file, Guests(), AclKind.Allow));

            StringAssert.Contains(exception.Message, "no explicit");
        }
        finally
        {
            Cleanup(file);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AFolderEntryCanReachWhatIsInsideIt()
    {
        OnWindows();
        var folder = Directory.CreateTempSubdirectory("acl").FullName;
        try
        {
            var service = new WindowsAccessControlService();
            var guests = Guests();

            service.Set(folder, guests, AclKind.Allow, "Read & execute", inheritToChildren: true);

            var entry = service.Read(folder).Entries.Single(e => e.Principal == guests && !e.Inherited);
            Assert.AreEqual("folder, subfolders, files", entry.Scope);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TurningInheritanceOffKeepsTheInheritedEntriesAsCopies()
    {
        OnWindows();
        var file = NewFile();
        try
        {
            var service = new WindowsAccessControlService();
            var before = service.Read(file);
            Assert.IsTrue(before.Inherits);
            var count = before.Entries.Count;

            service.SetInheritance(file, inherit: false);
            var after = service.Read(file);

            Assert.IsFalse(after.Inherits);
            Assert.AreEqual(count, after.Entries.Count);
            Assert.IsTrue(after.Entries.All(e => !e.Inherited));
        }
        finally
        {
            Cleanup(file);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AnUnknownAccountIsRefusedInWords()
    {
        OnWindows();
        var file = NewFile();
        try
        {
            var exception = Assert.ThrowsExactly<ArgumentException>(
                () => new WindowsAccessControlService().Set(file, "no-such-account-xyz", AclKind.Allow, "Read", false));

            StringAssert.Contains(exception.Message, "not a user or group");
        }
        finally
        {
            Cleanup(file);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public void APresetIsRecognisedWhateverTheSynchroniseBit()
    {
        OnWindows();
        Assert.AreEqual("Full control", WindowsAccessControlService.PresetFor(FileSystemRights.FullControl));
        Assert.AreEqual("Modify", WindowsAccessControlService.PresetFor(FileSystemRights.Modify & ~FileSystemRights.Synchronize));
        Assert.AreEqual("Read & execute", WindowsAccessControlService.PresetFor(FileSystemRights.ReadAndExecute));
        Assert.AreEqual("Full control", WindowsAccessControlService.PresetFor((FileSystemRights)0x10000000));
        Assert.IsNull(WindowsAccessControlService.PresetFor(FileSystemRights.Delete));
    }
}
