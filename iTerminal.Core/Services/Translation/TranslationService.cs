using System.Text;

namespace iTerminal.Core.Services.Translation;

/// <summary>
/// Translates and detects languages through whichever engine is available. The dialog talks only to this:
/// it splits a long text into pieces an engine accepts, picks the engine (or falls back to the other one),
/// and fills in the source language from the offline detector when the engine does not report it.
/// </summary>
public interface ITranslationService
{
    /// <summary>The engines that are set up, in the order <see cref="TranslationEngine.Auto"/> tries them.</summary>
    IReadOnlyList<ITranslationProvider> Providers { get; }

    /// <summary>The offline guess: instant, no request. Null when the text has too little to go on.</summary>
    LanguageDetection? DetectOffline(string text);

    /// <summary>Whether the engine can be used (it is set up), and if not, why. This does not reach out to it.</summary>
    string? UnavailableReason(TranslationEngine engine);

    /// <param name="sourceCode">The source language, or null / <see cref="TranslationLanguages.Auto"/> to detect it.</param>
    /// <exception cref="TranslationException">No engine could translate; the message says why.</exception>
    Task<TranslationResult> TranslateAsync(string text, string? sourceCode, string targetCode, TranslationEngine engine, CancellationToken cancellationToken = default);

    /// <summary>The language of the text, asking the engine only when the offline guess is not sure; null when nothing could tell.</summary>
    Task<LanguageDetection?> DetectAsync(string text, TranslationEngine engine, CancellationToken cancellationToken = default);
}

public sealed class TranslationService : ITranslationService
{
    /// <summary>An offline guess at least this sure is taken without asking an engine.</summary>
    internal const double TrustedOfflineConfidence = 0.9;

    private readonly ILanguageDetector _detector;
    private readonly IReadOnlyList<ITranslationProvider> _providers;

    public TranslationService(ILanguageDetector detector, IEnumerable<ITranslationProvider> providers)
    {
        _detector = detector;
        // The API first: it is what someone sets up on purpose, and it is faster and cheaper than a model.
        _providers = [.. providers.OrderBy(provider => provider.Id == "api" ? 0 : 1)];
    }

    public IReadOnlyList<ITranslationProvider> Providers => _providers;

    public LanguageDetection? DetectOffline(string text) => _detector.Detect(text);

    public string? UnavailableReason(TranslationEngine engine)
    {
        List<ITranslationProvider> candidates = Candidates(engine);
        if (candidates.Count == 0)
        {
            return engine == TranslationEngine.Api ? "no translation API is registered" : engine == TranslationEngine.Llm ? "no AI model is registered" : "no translation engine is registered";
        }

        if (candidates.Any(provider => provider.UnavailableReason is null))
        {
            return null;
        }

        return string.Join("; ", candidates.Select(provider => provider.UnavailableReason));
    }

    public async Task<TranslationResult> TranslateAsync(string text, string? sourceCode, string targetCode, TranslationEngine engine, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new TranslationResult(string.Empty, null, string.Empty);
        }

        if (TranslationLanguages.Find(targetCode) is null)
        {
            throw new TranslationException($"Unknown target language \"{targetCode}\".");
        }

        string? source = string.IsNullOrEmpty(sourceCode) || sourceCode == TranslationLanguages.Auto ? null : sourceCode;
        List<string> failures = [];
        foreach (ITranslationProvider provider in Candidates(engine))
        {
            if (provider.UnavailableReason is { } reason)
            {
                failures.Add($"{provider.Name}: {reason}");
                continue;
            }

            try
            {
                TranslationResult result = await TranslateInChunksAsync(provider, text, source, targetCode, cancellationToken);
                string? detected = result.DetectedSource ?? (source is null ? _detector.Detect(text)?.Code : null);
                return result with { DetectedSource = detected };
            }
            catch (TranslationException exception)
            {
                failures.Add(exception.Message);
            }
        }

        throw new TranslationException(failures.Count == 0
            ? "No translation engine is available."
            : string.Join(" ", failures));
    }

    public async Task<LanguageDetection?> DetectAsync(string text, TranslationEngine engine, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        LanguageDetection? offline = _detector.Detect(text);
        if (engine == TranslationEngine.Auto && offline is { Confidence: >= TrustedOfflineConfidence })
        {
            return offline;
        }

        foreach (ITranslationProvider provider in Candidates(engine).Where(provider => provider.UnavailableReason is null))
        {
            try
            {
                LanguageDetection? detected = await provider.DetectAsync(text, cancellationToken);
                if (detected is not null)
                {
                    return detected;
                }
            }
            catch (TranslationException)
            {
                // The next engine, or the offline guess, still has a say.
            }
        }

        return offline;
    }

    private List<ITranslationProvider> Candidates(TranslationEngine engine) => engine switch
    {
        TranslationEngine.Api => [.. _providers.Where(provider => provider.Id == "api")],
        TranslationEngine.Llm => [.. _providers.Where(provider => provider.Id == "llm")],
        _ => [.. _providers],
    };

    private static async Task<TranslationResult> TranslateInChunksAsync(
        ITranslationProvider provider, string text, string? source, string target, CancellationToken cancellationToken)
    {
        var translated = new StringBuilder();
        string? detected = null;
        foreach (string chunk in Chunk(text, provider.MaxChunkLength))
        {
            // The blank space around a piece is kept as it was: engines trim it, and it is the paragraph structure.
            string body = chunk.Trim();
            if (body.Length == 0)
            {
                translated.Append(chunk);
                continue;
            }

            int start = chunk.Length - chunk.TrimStart().Length;
            int end = chunk.Length - chunk.TrimEnd().Length;
            TranslationResult piece = await provider.TranslateAsync(body, source ?? detected, target, cancellationToken);
            detected ??= piece.DetectedSource;
            translated.Append(chunk, 0, start).Append(piece.Text).Append(chunk, chunk.Length - end, end);
        }

        return new TranslationResult(translated.ToString(), detected, provider.Name);
    }

    /// <summary>
    /// Cuts a text into pieces of at most <paramref name="maxLength"/> characters that join back into the text
    /// exactly. Cuts fall on line breaks when they can, else after a sentence end or a space, and only at the
    /// limit when a run has none.
    /// </summary>
    internal static IEnumerable<string> Chunk(string text, int maxLength)
    {
        int position = 0;
        while (position < text.Length)
        {
            int remaining = text.Length - position;
            if (remaining <= maxLength)
            {
                yield return text[position..];
                yield break;
            }

            int window = position + maxLength;
            int cut = text.LastIndexOf('\n', window - 1, maxLength);
            if (cut < 0)
            {
                cut = LastIndexOfAny(text, [". ", "! ", "? ", "。", "！", "？"], window, maxLength);
            }

            if (cut < 0)
            {
                cut = text.LastIndexOf(' ', window - 1, maxLength);
            }

            // Keep the separator with the piece it ends; a cut at the very start would make no progress.
            int length = cut <= position ? maxLength : cut + 1 - position;
            yield return text.Substring(position, length);
            position += length;
        }
    }

    private static int LastIndexOfAny(string text, string[] terminators, int window, int maxLength)
    {
        int best = -1;
        string slice = text.Substring(window - maxLength, maxLength);
        foreach (string terminator in terminators)
        {
            int found = slice.LastIndexOf(terminator, StringComparison.Ordinal);
            if (found >= 0)
            {
                best = Math.Max(best, window - maxLength + found + terminator.Length - 1);
            }
        }

        return best;
    }
}
