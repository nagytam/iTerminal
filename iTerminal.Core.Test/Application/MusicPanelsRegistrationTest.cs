using iConsole.Application;
using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Jobs;
using iTerminal.Core.Application;
using iTerminal.Core.Commands.Music;
using iTerminal.Core.Controls.MenuBar;
using iTerminal.Core.Controls.StatusBar;
using iTerminal.Core.Jobs;
using iTerminal.Core.Model.ContentModels;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Music;
using iTerminal.Core.Test.Helper;
using Microsoft.Extensions.DependencyInjection;

namespace iTerminal.Core.Test.Application;

/// <summary>
/// Resolves the music drives out of the application's real container. Adding a constructor parameter
/// to <see cref="HotKeys"/> or <see cref="TerminalMenuBar"/> is exactly the kind of change that
/// compiles but leaves the composition root unable to build the status bar — at which point no hot
/// key works at all, which is indistinguishable from a wrong key binding when using the app.
/// </summary>
[TestClass]
public class MusicPanelsRegistrationTest
{
    private static ServiceProvider BuildProvider()
    {
        // Mirrors Program.cs: the console driver has to be registered before the core services,
        // several of which (BeepService, Buffer, …) depend on it.
        var services = new ServiceCollection();
        services.AddDefaultConsoleDriver();
        services.AddiConsoleCore();
        services.AddiTerminal(TemporaryDataDirectory.Create());
        return services.BuildServiceProvider();
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Container_ResolvesThePlayerServices()
    {
        using var provider = BuildProvider();

        Assert.IsNotNull(provider.GetService<IAudioDeviceService>());
        Assert.IsNotNull(provider.GetService<IPlaylistService>());
        Assert.IsNotNull(provider.GetService<IMusicPlayerService>());
        Assert.IsNotNull(provider.GetService<IMusicLibraryService>());
        Assert.IsNotNull(provider.GetService<IMusicPlaylistStore>());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void MusicPlayerService_IsASingletonSoPlaybackOutlivesThePanel()
    {
        using var provider = BuildProvider();

        var first = provider.GetRequiredService<IMusicPlayerService>();
        var second = provider.GetRequiredService<IMusicPlayerService>();

        Assert.AreSame(first, second);
    }

    /// <summary>
    /// The content model service builds both music drives, so the container has to hand it the same
    /// player and library singletons the rest of the app uses — otherwise the panel would show a
    /// second, empty library and drive a second, silent player.
    /// </summary>
    [Timeout(60_000)]
    [TestMethod]
    public void ContentModelService_BuildsTheMusicDrivesFromTheSharedSingletons()
    {
        using var provider = BuildProvider();
        var contentModelService = provider.GetRequiredService<IContentModelService>();

        var playlist = contentModelService.AddContentModel("LeftPanel", new Url(Container.MUSIC_PLAYLIST));
        var catalog = contentModelService.AddContentModel("RightPanel", new Url(Container.MUSIC_CATALOG));

        Assert.IsInstanceOfType<ContentModelMusicPlaylist>(playlist);
        Assert.IsInstanceOfType<ContentModelMusicCatalog>(catalog);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Container_BuildsTheStatusBarHotKeysWithThePanelsCommandAttached()
    {
        using var provider = BuildProvider();

        var hotKeys = provider.GetRequiredService<HotKeys>();

        var command = hotKeys.Commands.AsSnapshot().OfType<MusicPanelsCommand>().SingleOrDefault();
        Assert.IsNotNull(command, "The music command must be attached to the status bar hot keys");
        Assert.AreSame(hotKeys, command.Control, "AddCommand must have bound the command to its host control");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Container_BuildsTheToolsMenu()
    {
        using var provider = BuildProvider();

        Assert.IsNotNull(provider.GetService<TerminalMenuBar>());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void SessionJob_IsRegisteredSoThePlaylistSurvivesARestart()
    {
        using var provider = BuildProvider();

        var jobs = provider.GetServices<IJob>().ToList();

        Assert.IsTrue(jobs.OfType<MusicSessionJob>().Any(), "MusicSessionJob must be registered as an IJob");
        Assert.IsTrue(jobs.OfType<MusicLibraryIndexJob>().Any(), "MusicLibraryIndexJob must be registered as an IJob");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void PanelsCommand_ExposesBothChordsForTheHelpScreen()
    {
        using var provider = BuildProvider();
        var command = new MusicPanelsCommand(provider.GetRequiredService<IContentModelService>());

        StringAssert.Contains(command.KeyDisplayText, "M");
        StringAssert.Contains(command.KeyDisplayText, "F7");
    }
}
