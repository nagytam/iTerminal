using iConsole.Media.Speech;

namespace iConsole.Media.Speech.Test;

/// <summary>The factory looks programs up through the finder it was given, so the application decides where ffmpeg is.</summary>
[TestClass]
public class MacRecorderFinderTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void MacRecorders_UseTheInjectedFinderForFfmpeg()
    {
        var factory = new MicrophoneCaptureFactory(program => program == "ffmpeg" ? "/custom/bin/ffmpeg" : null);

        List<RecorderCommand> commands = factory.MacRecorders();

        Assert.AreEqual(2, commands.Count);
        Assert.IsTrue(commands.All(command => command.Program == "/custom/bin/ffmpeg"));
    }
}
