namespace iTerminal.Core.Speech;

/// <summary>
/// One recording from the default microphone as 16 kHz mono signed 16-bit little-endian PCM. The constructor does
/// nothing: no device is opened and no process is started until <see cref="Start"/>.
/// </summary>
public interface IMicrophoneCapture : IDisposable
{
    /// <summary>Starts recording. Blocks briefly (a device opens, a program starts), so not from a key handler.</summary>
    /// <exception cref="SpeechException">There is no microphone, no recorder, or no permission.</exception>
    void Start();

    /// <summary>Stops recording, releases the microphone and returns what was captured, capped at <see cref="Pcm.MaxBytes"/>.</summary>
    byte[] Stop();
}

/// <summary>Picks the recorder of this operating system.</summary>
public interface IMicrophoneCaptureFactory
{
    /// <exception cref="SpeechException">No way of recording exists here, with the reason.</exception>
    IMicrophoneCapture Create();
}
