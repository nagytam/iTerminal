using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Commands.Music;
using iTerminal.Core.Model.ContentModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace iTerminal.Core.Commands.Music.Test;

/// <summary>The volume keys on a music panel: the keypad and the main row both work, and + is found whether or not Shift is held.</summary>
[TestClass]
public class MusicVolumeCommandTest
{
    private static (Mock<IMusicContentModel> Music, Mock<IContentModelService> Service) FocusedMusicPanel()
    {
        var music = new Mock<IMusicContentModel>();
        var service = new Mock<IContentModelService>();
        service.Setup(s => s.GetFocusedModel()).Returns(music.Object);
        return (music, service);
    }

    private static KeyEvent Typed(char c, ConsoleKey key, bool shift) => new(c, key, shift, false, false);

    [TestMethod]
    public async Task TheMainRowPlus_RaisesTheVolume_WithOrWithoutShift()
    {
        var (music, service) = FocusedMusicPanel();
        var command = new MusicVolumeUpCommand(music.Object, service.Object);

        Assert.IsTrue(await command.HandleKeyEventAsync(Typed('+', ConsoleKey.OemPlus, shift: true)));
        Assert.IsTrue(await command.HandleKeyEventAsync(Typed('=', ConsoleKey.OemPlus, shift: false)));

        music.Verify(m => m.Perform(MusicAction.VolumeUp), Times.Exactly(2));
    }

    [TestMethod]
    public async Task TheMainRowMinus_LowersTheVolume_WhetherOrNotShiftIsHeld()
    {
        var (music, service) = FocusedMusicPanel();
        var command = new MusicVolumeDownCommand(music.Object, service.Object);

        Assert.IsTrue(await command.HandleKeyEventAsync(Typed('-', ConsoleKey.OemMinus, shift: false)));
        Assert.IsTrue(await command.HandleKeyEventAsync(Typed('-', ConsoleKey.OemMinus, shift: true)));

        music.Verify(m => m.Perform(MusicAction.VolumeDown), Times.Exactly(2));
    }

    [TestMethod]
    public async Task TheKeypadKey_StillWorks()
    {
        var (music, service) = FocusedMusicPanel();
        var command = new MusicVolumeUpCommand(music.Object, service.Object);

        Assert.IsTrue(await command.HandleKeyEventAsync(new KeyEvent(' ', ConsoleKey.Add, false, false, false)));

        music.Verify(m => m.Perform(MusicAction.VolumeUp), Times.Once);
    }

    [TestMethod]
    public async Task WithControlHeld_ThePlusIsNotTheVolumeKey()
    {
        var (music, service) = FocusedMusicPanel();
        var command = new MusicVolumeUpCommand(music.Object, service.Object);

        await command.HandleKeyEventAsync(new KeyEvent('+', ConsoleKey.OemPlus, false, false, true));

        music.Verify(m => m.Perform(It.IsAny<MusicAction>()), Times.Never);
    }

    [TestMethod]
    public async Task AnotherPanelFocused_NothingChanges()
    {
        var (music, service) = FocusedMusicPanel();
        service.Setup(s => s.GetFocusedModel()).Returns(new Mock<IContentModel>().Object);
        var command = new MusicVolumeUpCommand(music.Object, service.Object);

        await command.HandleKeyEventAsync(Typed('+', ConsoleKey.OemPlus, shift: true));

        music.Verify(m => m.Perform(It.IsAny<MusicAction>()), Times.Never);
    }
}
