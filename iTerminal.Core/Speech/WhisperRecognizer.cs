using System.Text;
using iConsole.Localization;
using iTerminal.Core.Services.RuntimeDependencies;
using Whisper.net;

namespace iTerminal.Core.Speech;

/// <summary>
/// Whisper (whisper.cpp through Whisper.net) on the CPU. The model is loaded for each recording and released
/// afterwards, so the ~300 MB it takes are only held while text is being recognised; the language is detected.
/// </summary>
public sealed class WhisperRecognizer(WhisperRuntimeDependency dependency) : ISpeechRecognizer
{
    public bool IsModelInstalled => dependency.ModelPath() is not null;

    /// <summary>
    /// Whisper writes a bracketed marker for what is not speech — <c>[BLANK_AUDIO]</c>, <c>[MUSIC]</c>, <c>(silence)</c> — and
    /// a segment that is only that is noise, not a question.
    /// </summary>
    internal static bool IsNonSpeech(string segment)
    {
        string trimmed = segment.Trim();
        return trimmed.Length == 0
            || (trimmed.Length > 1
                && ((trimmed[0] == '[' && trimmed[^1] == ']') || (trimmed[0] == '(' && trimmed[^1] == ')') || (trimmed[0] == '*' && trimmed[^1] == '*'))
                && trimmed.IndexOfAny(['[', ']', '(', ')'], 1, trimmed.Length - 2) < 0);
    }

    public async Task<string> TranscribeAsync(float[] samples, CancellationToken cancellationToken)
    {
        string path = dependency.ModelPath()
            ?? throw new SpeechException(UiText.Get("Voice.ModelMissing", "Voice input needs the Whisper model. Install it from Tools > Runtime Dependencies."));

        try
        {
            using WhisperFactory factory = WhisperFactory.FromPath(path);
            await using WhisperProcessor processor = factory.CreateBuilder().WithLanguage("auto").Build();

            var text = new StringBuilder();
            await foreach (SegmentData segment in processor.ProcessAsync(samples, cancellationToken))
            {
                if (!IsNonSpeech(segment.Text))
                {
                    text.Append(segment.Text);
                }
            }

            return text.ToString();
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException
            or InvalidOperationException or NotSupportedException)
        {
            throw new SpeechException(
                UiText.Get("Voice.EngineFailed", "The speech engine could not be loaded on this system") + $" ({exception.GetType().Name}).");
        }
    }
}
