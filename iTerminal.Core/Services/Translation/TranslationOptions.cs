namespace iTerminal.Core.Services.Translation;

/// <summary>Which engine does the translating.</summary>
public enum TranslationEngine
{
    /// <summary>The translation API when one is set up, otherwise the AI model; the other one is tried if the first fails.</summary>
    Auto,

    /// <summary>Only the translation API (a LibreTranslate-compatible server).</summary>
    Api,

    /// <summary>Only the connected AI model (the <c>AI</c> section's provider and model).</summary>
    Llm,
}

/// <summary>The <c>Translation</c> configuration section.</summary>
public class TranslationOptions
{
    /// <summary>The server of a LibreTranslate-compatible API: <c>/translate</c> and <c>/detect</c> are appended. Empty means no API is set up.</summary>
    public string ApiUrl { get; set; } = string.Empty;

    /// <summary>The environment variable the API key is read from; the password manager entry <see cref="ApiKeyPasswordId"/> is the fallback.</summary>
    public string ApiKeyEnvVar { get; set; } = "LIBRETRANSLATE_API_KEY";

    /// <summary>The password manager entry the key is kept under when the environment variable is not set.</summary>
    public const string ApiKeyPasswordId = "translation:api-key";

    /// <summary>The language a text is translated into when none was chosen yet.</summary>
    public string DefaultTargetLanguage { get; set; } = "en";
}
