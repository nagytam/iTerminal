namespace iTerminal.Core.Services.Translation;

/// <summary>What an engine made of one piece of text: the translation and, when the source was left to the engine, the language it found.</summary>
public sealed record TranslationResult(string Text, string? DetectedSource, string EngineName);

/// <summary>A failure that is worth showing as it is: the engine is not set up, refused the request or could not be reached.</summary>
public sealed class TranslationException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>One way of translating: an HTTP API or the connected AI model. Providers do the single request; <see cref="ITranslationService"/> splits long texts and chooses between them.</summary>
public interface ITranslationProvider
{
    /// <summary>"api" or "llm".</summary>
    string Id { get; }

    string Name { get; }

    /// <summary>The longest text, in characters, one request carries.</summary>
    int MaxChunkLength { get; }

    /// <summary>Why the provider cannot be used right now ("no API URL set", "AI is off"), or null when it is set up. Says nothing about whether it is reachable.</summary>
    string? UnavailableReason { get; }

    /// <param name="sourceCode">The source language, or null to let the engine find it.</param>
    Task<TranslationResult> TranslateAsync(string text, string? sourceCode, string targetCode, CancellationToken cancellationToken = default);

    Task<LanguageDetection?> DetectAsync(string text, CancellationToken cancellationToken = default);
}
