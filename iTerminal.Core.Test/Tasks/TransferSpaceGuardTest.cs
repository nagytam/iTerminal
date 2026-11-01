using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace iTerminal.Core.Tasks.Test;

/// <summary>Before a copy writes, the target must have room for it; a move on one volume is a rename and needs none.</summary>
[TestClass]
public class TransferSpaceGuardTest
{
    private static readonly Url Source = new(Path.Combine(Path.GetTempPath(), "a.bin"));
    private static readonly Url Target = new(Path.Combine(Path.GetTempPath(), "out", "a.bin"));

    private static (Mock<IFileSystem> Fs, Mock<IFreeSpaceFileSystem> Space) Volume(long? free, string? volumeOfSource = "v1", string? volumeOfTarget = "v1")
    {
        var fs = new Mock<IFileSystem>();
        var space = fs.As<IFreeSpaceFileSystem>();
        space.Setup(s => s.GetFreeBytes(It.IsAny<Url>())).Returns(free);
        space.Setup(s => s.GetVolumeId(Source)).Returns(volumeOfSource);
        space.Setup(s => s.GetVolumeId(Target)).Returns(volumeOfTarget);
        return (fs, space);
    }

    private static Mock<IErrorPromptService> Prompts(params bool[] answers)
    {
        var prompts = new Mock<IErrorPromptService>();
        var queue = new Queue<bool>(answers);
        prompts.Setup(p => p.ConfirmAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => queue.Dequeue());
        return prompts;
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AMoveOnOneKnownVolume_NeedsNoGuard()
    {
        var (fs, _) = Volume(free: 0);

        var guard = TransferSpaceGuard.For(fs.Object, Source, fs.Object, Target, deletesSource: true, Prompts().Object);

        Assert.IsNull(guard);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AMoveWhoseVolumeCannotBeIdentified_IsStillChecked()
    {
        // Unknown is not "the same volume": a move that might cross volumes must not skip the space check.
        var (fs, _) = Volume(free: 0, volumeOfSource: null, volumeOfTarget: null);

        var guard = TransferSpaceGuard.For(fs.Object, Source, fs.Object, Target, deletesSource: true, Prompts().Object);

        Assert.IsNotNull(guard);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ACopy_IsAlwaysChecked()
    {
        var (fs, _) = Volume(free: 0);

        var guard = TransferSpaceGuard.For(fs.Object, Source, fs.Object, Target, deletesSource: false, Prompts().Object);

        Assert.IsNotNull(guard);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void WithoutPrompts_ThereIsNoGuard()
    {
        var (fs, _) = Volume(free: 0);

        Assert.IsNull(TransferSpaceGuard.For(fs.Object, Source, fs.Object, Target, deletesSource: false, prompts: null));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task EnoughRoom_ProceedsWithoutAsking()
    {
        var (fs, _) = Volume(free: 1000);
        var prompts = Prompts();
        var guard = TransferSpaceGuard.For(fs.Object, Source, fs.Object, Target, deletesSource: false, prompts.Object);

        await guard!.EnsureAsync(() => 500, CancellationToken.None);

        prompts.Verify(p => p.ConfirmAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Short_GoOnAnyway_ProceedsOnceAndAsksNoMore()
    {
        var (fs, _) = Volume(free: 100);
        var prompts = Prompts(false);
        var guard = TransferSpaceGuard.For(fs.Object, Source, fs.Object, Target, deletesSource: false, prompts.Object);

        await guard!.EnsureAsync(() => 500, CancellationToken.None);
        await guard.EnsureAsync(() => 500, CancellationToken.None);

        prompts.Verify(p => p.ConfirmAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Short_PausedThenCancelled_StopsTheCopy()
    {
        var (fs, _) = Volume(free: 100);
        var guard = TransferSpaceGuard.For(fs.Object, Source, fs.Object, Target, deletesSource: false, Prompts(true, false).Object);

        await Assert.ThrowsAsync<OperationCanceledException>(() => guard!.EnsureAsync(() => 500, CancellationToken.None));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Short_PausedThenResumedAfterFreeingSpace_Continues()
    {
        // Short until the user has freed space at the resume prompt; then the copy goes on without a third question.
        var freed = false;
        var (fs, space) = Volume(free: 100);
        space.Setup(s => s.GetFreeBytes(It.IsAny<Url>())).Returns(() => freed ? 1000 : 100);
        var prompts = new Mock<IErrorPromptService>();
        var answers = new Queue<Func<bool>>([() => true, () => { freed = true; return true; }]);
        prompts.Setup(p => p.ConfirmAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => answers.Dequeue()());
        var guard = TransferSpaceGuard.For(fs.Object, Source, fs.Object, Target, deletesSource: false, prompts.Object);

        await guard!.EnsureAsync(() => 500, CancellationToken.None);

        prompts.Verify(p => p.ConfirmAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }
}
