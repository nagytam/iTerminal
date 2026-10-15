using iConsole.Data;
using iTerminal.Core.Services;

namespace iTerminal.Core.Test.Services;

[TestClass]
public class NullServicesCoverageTest
{
    [TestMethod]
    public async Task NullContentModelService_ExposesDefaultStateAndNoOpOperations()
    {
        NullContentModelService service = NullContentModelService.Instance;

        await service.SetUrlAsync(new Url(Directory.GetCurrentDirectory()));
        await service.SetUrlAsync("panel", new Url(Directory.GetCurrentDirectory()));

        Assert.AreSame(service, NullContentModelService.Instance);
        Assert.AreEqual(string.Empty, service.FocusedPanel);
        Assert.AreEqual(string.Empty, service.LastFocusedPanel);
        Assert.IsNull(service.FocusedFileUrl);
        Assert.IsNull(service.NonFocusedFolderUrl);
        Assert.IsNull(service.GetFocusedModel());
        Assert.IsNull(service.GetLastContentModel());
        Assert.IsNull(service.AddContentModel("panel", new Url(Directory.GetCurrentDirectory())));
    }

    [TestMethod]
    public void NullSettingsService_ReturnsDefaultValuesAndIgnoresWrites()
    {
        NullSettingsService service = NullSettingsService.Instance;

        service.Set("key", "value");
        service.Write();

        Assert.AreSame(service, NullSettingsService.Instance);
        Assert.IsFalse(service.IsDirty);
        Assert.IsNull(service.Get("key"));
        Assert.AreEqual(true, service.GetAsBool("missing", true));
        Assert.AreEqual(false, service.GetAsBool("missing", false));
    }
}
