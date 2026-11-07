using iTerminal.Core.Services.Processes;
using iTerminal.Core.Services.SystemSettings;
using Moq;

namespace iTerminal.Core.Test.Services.SystemSettings;

[TestClass]
public class ProfileEnvironmentStoreTest
{
    private string _folder = null!;

    [TestInitialize]
    public void CreateFolder()
    {
        _folder = Path.Combine(Path.GetTempPath(), "iterminal-profile-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
    }

    [TestCleanup]
    public void DeleteFolder() => Directory.Delete(_folder, recursive: true);

    private string ProfilePath => Path.Combine(_folder, ".zprofile");

    [Timeout(60_000)]
    [TestMethod]
    public void AMissingProfileAndItsFolderAreCreatedOnTheFirstSet()
    {
        var store = new ProfileEnvironmentStore(Path.Combine(_folder, "sub", ".zprofile"));

        Assert.AreEqual(0, store.Read().Count);
        store.Set("EDITOR", "vim");

        CollectionAssert.AreEqual(new[] { new SystemEnvironmentVariable("EDITOR", "vim") }, store.Read().ToArray());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void QuotesSpacesDollarsAndBackticksRoundTripAndAreWrittenSingleQuoted()
    {
        var store = new ProfileEnvironmentStore(ProfilePath);
        const string Awkward = "it's a $HOME `date` \"x\" \\ # done";

        store.Set("AWKWARD", Awkward);

        Assert.AreEqual(Awkward, store.Read().Single().Value);
        StringAssert.Contains(File.ReadAllText(ProfilePath), "export AWKWARD='it'\\''s a $HOME `date` \"x\" \\ # done'");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheRestOfTheFileItsLineEndingsAndTheMissingFinalNewlineSurvive()
    {
        File.WriteAllText(ProfilePath, "export PATH=\"/usr/bin\"\r\nalias ll='ls -l'");
        var store = new ProfileEnvironmentStore(ProfilePath);

        store.Set("A_VAR", "1");
        store.Set("A_VAR", "2");
        store.Set("B_VAR", "3");

        Assert.AreEqual(
            "export PATH=\"/usr/bin\"\r\nalias ll='ls -l'\r\n" + ProfileEnvironmentStore.BeginMarker + "\r\nexport A_VAR='2'\r\nexport B_VAR='3'\r\n" + ProfileEnvironmentStore.EndMarker,
            File.ReadAllText(ProfilePath));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TextAfterTheBlockIsKept()
    {
        File.WriteAllText(
            ProfilePath,
            "before\n" + ProfileEnvironmentStore.BeginMarker + "\nexport A='1'\n" + ProfileEnvironmentStore.EndMarker + "\nafter\n");
        var store = new ProfileEnvironmentStore(ProfilePath);

        store.Set("B", "2");

        Assert.AreEqual(
            "before\n" + ProfileEnvironmentStore.BeginMarker + "\nexport A='1'\nexport B='2'\n" + ProfileEnvironmentStore.EndMarker + "\nafter\n",
            File.ReadAllText(ProfilePath));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void RemovingTheLastVariableRemovesTheBlockAndNothingElse()
    {
        File.WriteAllText(ProfilePath, "keep me\n");
        var store = new ProfileEnvironmentStore(ProfilePath);
        store.Set("ONLY", "1");

        store.Remove("ONLY");

        Assert.AreEqual("keep me\n", File.ReadAllText(ProfilePath));
        Assert.AreEqual(0, store.Read().Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void RemovingAVariableThatIsNotThereLeavesTheFileUntouched()
    {
        File.WriteAllText(ProfilePath, "keep me\n");
        var before = File.GetLastWriteTimeUtc(ProfilePath);

        new ProfileEnvironmentStore(ProfilePath).Remove("MISSING");

        Assert.AreEqual("keep me\n", File.ReadAllText(ProfilePath));
        Assert.AreEqual(before, File.GetLastWriteTimeUtc(ProfilePath));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void SettingTheSameValueTwiceIsIdempotent()
    {
        var store = new ProfileEnvironmentStore(ProfilePath);
        store.Set("A", "1");
        var once = File.ReadAllText(ProfilePath);

        store.Set("A", "1");

        Assert.AreEqual(once, File.ReadAllText(ProfilePath));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ABeginMarkerThatNeverEndsIsLeftAloneAndAFreshBlockFollowsIt()
    {
        File.WriteAllText(ProfilePath, ProfileEnvironmentStore.BeginMarker + "\nexport HAND='written'\n");
        var store = new ProfileEnvironmentStore(ProfilePath);

        Assert.AreEqual(0, store.Read().Count);
        store.Set("NEW", "1");
        store.Set("NEW", "2");

        var text = File.ReadAllText(ProfilePath);
        StringAssert.Contains(text, "export HAND='written'");
        CollectionAssert.AreEqual(new[] { new SystemEnvironmentVariable("NEW", "2") }, store.Read().ToArray());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ALineInsideTheBlockThatTheStoreDidNotWriteSurvivesARewrite()
    {
        File.WriteAllText(
            ProfilePath,
            ProfileEnvironmentStore.BeginMarker + "\n# my note\nexport HAND='x' # trailing\nexport A='1'\n" + ProfileEnvironmentStore.EndMarker + "\n");
        var store = new ProfileEnvironmentStore(ProfilePath);

        store.Set("A", "2");
        store.Remove("A");

        Assert.AreEqual(
            ProfileEnvironmentStore.BeginMarker + "\n# my note\nexport HAND='x' # trailing\n" + ProfileEnvironmentStore.EndMarker + "\n",
            File.ReadAllText(ProfilePath));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheMacProfileIsZprofileInTheHomeFolder()
    {
        var path = ProfileEnvironmentStore.DefaultPath();

        Assert.AreEqual(".zprofile", Path.GetFileName(path));
        Assert.AreEqual(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Path.GetDirectoryName(path));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void OnAMacTheServiceKeepsTheUsersVariablesInZprofileAndOffersNoMachineScope()
    {
        var service = new SystemSettingsService(Mock.Of<IExternalProcessRunner>(), () => false, windows: false, linux: false, null, null, macOs: true);

        Assert.IsTrue(service.SupportsEnvironment(EnvironmentScope.User));
        Assert.IsFalse(service.SupportsEnvironment(EnvironmentScope.Machine));
    }
}
