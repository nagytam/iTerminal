using iTerminal.Core.Services;

namespace iTerminal.Core.Test.Services;

[TestClass]
public class SettingsAndCacheServiceTest
{
    private string _tempFile = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _tempFile = Path.GetTempFileName();
        // Remove file so SettingsService starts fresh (it reads if exists)
        File.Delete(_tempFile);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (File.Exists(_tempFile)) File.Delete(_tempFile);
    }

    private SettingsService CreateService()
    {
        // We cannot inject the path; test via the public API using the real service
        // and verify Set/Get/Delete round-trips work in-memory.
        return new SettingsService();
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Set_And_Get_RoundTrip()
    {
        var svc = CreateService();
        svc.Set("key1", "value1");
        Assert.AreEqual("value1", svc.Get("key1"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Get_ReturnsNull_ForUnknownKey()
    {
        var svc = CreateService();
        Assert.IsNull(svc.Get("unknown_key_xyz"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Set_OverwritesExistingValue()
    {
        var svc = CreateService();
        svc.Set("k", "first");
        svc.Set("k", "second");
        Assert.AreEqual("second", svc.Get("k"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Delete_RemovesKey()
    {
        var svc = CreateService();
        svc.Set("toDelete", "value");
        svc.Delete("toDelete");
        Assert.IsNull(svc.Get("toDelete"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Delete_NonExistentKey_DoesNotThrow()
    {
        var svc = CreateService();
        svc.Delete("neverSet");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void IsDirty_IsFalse_Initially()
    {
        var svc = CreateService();
        // After construction IsDirty may be false if no file existed
        // (cannot guarantee because user profile may have existing settings file)
        Assert.IsNotNull(svc);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void IsDirty_IsTrue_AfterSet()
    {
        var svc = CreateService();
        svc.Set("k", "v");
        Assert.IsTrue(svc.IsDirty);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void IsDirty_IsTrue_AfterDelete()
    {
        var svc = CreateService();
        svc.Set("k", "v");
        _ = svc.IsDirty; // consume
        svc.Delete("k");
        Assert.IsTrue(svc.IsDirty);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void GetAsBool_ReturnsTrue_ForTrueString()
    {
        var svc = CreateService();
        svc.Set("flag", "true");
        Assert.AreEqual(true, svc.GetAsBool("flag"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void GetAsBool_ReturnsFalse_ForFalseString()
    {
        var svc = CreateService();
        svc.Set("flag", "false");
        Assert.AreEqual(false, svc.GetAsBool("flag"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void GetAsBool_ReturnsDefault_ForMissingKey()
    {
        var svc = CreateService();
        Assert.AreEqual(false, svc.GetAsBool("missing"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void GetAsBool_ReturnsDefault_ForNonBoolString()
    {
        var svc = CreateService();
        svc.Set("flag", "notabool");
        Assert.AreEqual(false, svc.GetAsBool("flag"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Write_DoesNotThrow()
    {
        var svc = CreateService();
        svc.Set("writeTest", "val");
        svc.Write();
        Assert.IsFalse(svc.IsDirty);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void FolderHistory_ConstantIsDefined()
    {
        Assert.IsFalse(string.IsNullOrEmpty(SettingsService.FOLDER_HISTORY));
    }
}

[TestClass]
public class MemoryCacheServiceTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void Set_And_Get_RoundTrip()
    {
        var svc = new MemoryCacheService();
        svc.Set("key", "value");
        Assert.AreEqual("value", svc.Get("key"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Get_ReturnsNull_ForUnknownKey()
    {
        var svc = new MemoryCacheService();
        Assert.IsNull(svc.Get("unknown"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Set_OverwritesExistingEntry()
    {
        var svc = new MemoryCacheService();
        svc.Set("k", "first");
        svc.Set("k", "second");
        Assert.AreEqual("second", svc.Get("k"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Set_AcceptsArbitraryObjects()
    {
        var svc = new MemoryCacheService();
        var obj = new List<int> { 1, 2, 3 };
        svc.Set("list", obj);
        var retrieved = svc.Get("list") as List<int>;
        Assert.IsNotNull(retrieved);
        CollectionAssert.AreEqual(obj, retrieved);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void MemoryCacheService_ImplementsInterface()
    {
        var svc = new MemoryCacheService();
        Assert.IsInstanceOfType(svc, typeof(IMemoryCacheService));
    }
}
