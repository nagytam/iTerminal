using iTerminal.Core.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace iTerminal.Core.Services.Translation;

/// <summary>
/// Translates and detects through the connected AI model — the same <see cref="IChatClient"/> as the prompt bar, so
/// whichever Ollama or OpenAI-compatible model the <c>AI</c> section names does the work. The prompt allows no
/// commentary, and the answer is cleaned of reasoning blocks, because a model that explains itself would end up
/// in the translated text.
/// </summary>
public sealed class LlmTranslationProvider(IChatClient chatClient, IOptions<AiOptions> aiOptions) : ITranslationProvider
{
    public string Id => "llm";

    public string Name => "AI model";

    // Small local models lose track of a long text; paragraph-sized pieces keep them faithful.
    public int MaxChunkLength => 3000;

    public string? UnavailableReason =>
        aiOptions.Value.Provider == AiProvider.None ? "AI is off (AI:Provider)" : null;

    public async Task<TranslationResult> TranslateAsync(string text, string? sourceCode, string targetCode, CancellationToken cancellationToken = default)
    {
        string target = Describe(targetCode);
        string from = string.IsNullOrEmpty(sourceCode) || sourceCode == TranslationLanguages.Auto
            ? "The source language is not given; work it out."
            : $"The text is in {Describe(sourceCode)}.";

        string system =
            $"You are a translation engine. Translate the user's message into {target}. {from} " +
            "Keep the meaning, tone, line breaks, numbers, names and any markup or placeholders as they are. " +
            "Answer with the translation only: no quotes around it, no notes, no explanation, no language names. " +
            $"If the text is already in {target}, answer with it unchanged.";

        string answer = await AskAsync(system, text, cancellationToken);
        if (answer.Length == 0)
        {
            throw new TranslationException($"{Name} sent no translation.");
        }

        return new TranslationResult(answer, null, Name);
    }

    public async Task<LanguageDetection?> DetectAsync(string text, CancellationToken cancellationToken = default)
    {
        const string system =
            "You identify languages. Answer with the ISO 639-1 code of the language the user's message is written in " +
            "(for example en, de, zh, ja) and nothing else.";

        string answer = await AskAsync(system, text.Length > 600 ? text[..600] : text, cancellationToken);
        TranslationLanguage? language = ParseCode(answer);
        return language is null ? null : new LanguageDetection(language.Code, 0.8);
    }

    /// <summary>The language named by a model's answer: its first word, read as a code ("de", "pt-BR", "DE.") or as an English name ("German").</summary>
    internal static TranslationLanguage? ParseCode(string answer)
    {
        string word = answer.Trim().Trim('"', '\'', '`', '.', ' ').Split([' ', '\r', '\n', ',', ';'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
        return TranslationLanguages.Find(word)
            ?? TranslationLanguages.All.FirstOrDefault(language => language.Name.Equals(word, StringComparison.OrdinalIgnoreCase));
    }

    private static string Describe(string code) =>
        TranslationLanguages.Find(code) is { } language ? $"{language.Name} ({language.Code})" : code;

    private async Task<string> AskAsync(string system, string user, CancellationToken cancellationToken)
    {
        if (UnavailableReason is { } reason)
        {
            throw new TranslationException($"{Name}: {reason}.");
        }

        try
        {
            ChatResponse response = await chatClient.GetResponseAsync(
                [new ChatMessage(ChatRole.System, system), new ChatMessage(ChatRole.User, user)],
                new ChatOptions { Temperature = 0 },
                cancellationToken);
            return AiChatService.Sanitize(response.Text ?? string.Empty);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new TranslationException($"{Name} failed: {exception.Message}", exception);
        }
    }
}
