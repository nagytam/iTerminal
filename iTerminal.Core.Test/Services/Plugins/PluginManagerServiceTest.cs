using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Viewers;
using AppSettings = iTerminal.Core.Data.Settings;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Serilog;

namespace iTerminal.Core.Test.Services.Plugins;

/// <summary>
/// Loads and unloads plug-in files. A real plug-in is needed for the loading cases, so the test assembly itself is copied into
/// the plug-in folder: it holds one public viewer, <see cref="SampleViewer"/>, and the copy is loaded in a plug-in context.
/// </summary>
[TestClass]
public class PluginManagerServiceTest
{
    /// <summary>The viewer a copy of this assembly offers. Public, so the manager finds it; it has no state.</summary>
    public sealed class SampleViewer : IFileViewer
    {
        public int Priority => 0;

        public bool IsCandidate(Url url) => url.Extension == "sample";

        public Task OpenAsync(Url url, IFileOpenHost host) => Task.CompletedTask;
    }

    private const string SampleFile = "Sample.dll";

    private string _folder = string.Empty;

    [TestInitialize]
    public void CreateFolder()
    {
        _folder = Path.Combine(Path.GetTempPath(), "iTerminal-plugins-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
    }

    [TestCleanup]
    public void DeleteFolder()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // A temporary folder that another process still has open is left for the system to clean up.
        }
    }

    private sealed class Rig(PluginManagerService manager, FileViewerRegistry viewers, FileEditorRegistry editors, Mock<ISettingsService> settings)
    {
        public PluginManagerService Manager { get; } = manager;

        public FileViewerRegistry Viewers { get; } = viewers;

        public FileEditorRegistry Editors { get; } = editors;

        public Mock<ISettingsService> Settings { get; } = settings;
    }

    private Rig CreateRig(string? savedPlugins = null)
    {
        var settings = new Mock<ISettingsService>();
        settings.Setup(s => s.Get(AppSettings.LoadedPlugins)).Returns(savedPlugins);
        var viewers = new FileViewerRegistry([]);
        var editors = new FileEditorRegistry([]);
        var manager = new PluginManagerService(
            new ServiceCollection().BuildServiceProvider(),
            new PluginLocation(_folder),
            settings.Object,
            viewers,
            editors,
            new LoggerConfiguration().CreateLogger());
        return new Rig(manager, viewers, editors, settings);
    }

    /// <summary>A plug-in file: a copy of this assembly, which holds <see cref="SampleViewer"/>.</summary>
    private void CopySamplePlugin(string fileName = SampleFile) =>
        File.Copy(typeof(PluginManagerServiceTest).Assembly.Location, Path.Combine(_folder, fileName));

    private PluginInfo Find(Rig rig, string fileName) =>
        rig.Manager.Plugins.Single(plugin => plugin.FileName == fileName);

    [Timeout(60_000)]
    [TestMethod]
    public async Task Rescan_ListsTheDllFilesOnly_AsUnloaded()
    {
        File.WriteAllText(Path.Combine(_folder, "Alpha.dll"), "not an assembly");
        File.WriteAllText(Path.Combine(_folder, "notes.txt"), "not a plug-in");
        var rig = CreateRig();

        await rig.Manager.RescanAsync();

        PluginInfo plugin = Assert.ContainsSingle(rig.Manager.Plugins);
        Assert.AreEqual("Alpha.dll", plugin.FileName);
        Assert.AreEqual(PluginState.Unloaded, plugin.State);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task SetLoaded_LoadsTheViewerAndRegistersIt_ThenUnloadRemovesIt()
    {
        CopySamplePlugin();
        var rig = CreateRig();
        await rig.Manager.RescanAsync();

        await rig.Manager.SetLoadedAsync(SampleFile, true);

        PluginInfo loaded = Find(rig, SampleFile);
        Assert.AreEqual(PluginState.Loaded, loaded.State);
        Assert.AreEqual(1, loaded.Viewers);
        IFileViewer registered = Assert.ContainsSingle(rig.Viewers.Handlers);
        Assert.AreEqual(typeof(SampleViewer).FullName, registered.GetType().FullName);
        Assert.AreNotSame(typeof(SampleViewer), registered.GetType(), "the viewer must come from the plug-in's own context");
        Assert.IsTrue(rig.Viewers.HasCandidate(new Url("file:///tmp/a.sample")));

        await rig.Manager.SetLoadedAsync(SampleFile, false);

        Assert.AreEqual(PluginState.Unloaded, Find(rig, SampleFile).State);
        Assert.AreEqual(0, rig.Viewers.Handlers.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task SetLoaded_AFileWithNoViewerOrEditor_FailsWithoutAnErrorText()
    {
        // Microsoft.Extensions.DependencyInjection is a real assembly with no file handler in it.
        File.Copy(typeof(ServiceCollection).Assembly.Location, Path.Combine(_folder, "Di.dll"));
        var rig = CreateRig();
        await rig.Manager.RescanAsync();

        await rig.Manager.SetLoadedAsync("Di.dll", true);

        PluginInfo plugin = Find(rig, "Di.dll");
        Assert.AreEqual(PluginState.Failed, plugin.State);
        Assert.IsNull(plugin.Error);
        Assert.AreEqual(0, rig.Viewers.Handlers.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task SetLoaded_AFileThatIsNotAnAssembly_FailsWithTheReason()
    {
        File.WriteAllText(Path.Combine(_folder, "Broken.dll"), "not an assembly");
        var rig = CreateRig();
        await rig.Manager.RescanAsync();

        await rig.Manager.SetLoadedAsync("Broken.dll", true);

        PluginInfo plugin = Find(rig, "Broken.dll");
        Assert.AreEqual(PluginState.Failed, plugin.State);
        Assert.IsFalse(string.IsNullOrEmpty(plugin.Error));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task SetLoaded_SavesTheLoadedPluginsSoTheyLoadAgainAtStartUp()
    {
        CopySamplePlugin();
        var rig = CreateRig();
        await rig.Manager.RescanAsync();

        await rig.Manager.SetLoadedAsync(SampleFile, true);
        rig.Settings.Verify(s => s.Set(AppSettings.LoadedPlugins, It.Is<string>(saved => saved.Contains(SampleFile))), Times.AtLeastOnce);

        await rig.Manager.SetLoadedAsync(SampleFile, false);
        rig.Settings.Verify(s => s.Set(AppSettings.LoadedPlugins, "[]"), Times.Once);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Start_LoadsThePluginsTheUserLoadedLast()
    {
        CopySamplePlugin();
        var rig = CreateRig(savedPlugins: $"[\"{SampleFile}\"]");

        int failed = await rig.Manager.StartAsync();

        Assert.AreEqual(0, failed);
        Assert.AreEqual(PluginState.Loaded, Find(rig, SampleFile).State);
        Assert.ContainsSingle(rig.Viewers.Handlers);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Start_ACorruptSavedList_IsIgnored()
    {
        var rig = CreateRig(savedPlugins: "{not json");

        int failed = await rig.Manager.StartAsync();

        Assert.AreEqual(0, failed);
        Assert.AreEqual(0, rig.Manager.Plugins.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Rescan_AFileThatWasRemoved_IsForgottenWhileUnloaded()
    {
        File.WriteAllText(Path.Combine(_folder, "Gone.dll"), "not an assembly");
        var rig = CreateRig();
        await rig.Manager.RescanAsync();
        File.Delete(Path.Combine(_folder, "Gone.dll"));

        await rig.Manager.RescanAsync();

        Assert.AreEqual(0, rig.Manager.Plugins.Count);
    }
}
