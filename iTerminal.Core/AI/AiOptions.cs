namespace iTerminal.Core.AI;

public enum AiProvider
{
    /// <summary>No AI at all: the prompt bar says it is off, nothing is started, and Ollama is never offered.</summary>
    None,

    /// <summary>A local Ollama server, reached through its OpenAI-compatible endpoint. Needs no key; iTerminal can download and run it.</summary>
    Ollama,

    /// <summary>Any OpenAI-compatible API, with the key taken from <see cref="AiOptions.ApiKeyEnvVar"/> or the password store.</summary>
    OpenAI,
}

public class AiOptions
{
    public AiProvider Provider { get; set; } = AiProvider.Ollama;
    public string ModelId { get; set; } = "claude-opus-4-5-20251101";

    /// <summary>
    /// The embedding model behind the document manager's meaning search, served by the same endpoint as the chat model
    /// (Ollama: <c>/v1/embeddings</c>, e.g. <c>nomic-embed-text</c>). Empty turns meaning search off; iTerminal never
    /// downloads it, so the model has to be pulled first.
    /// <para>
    /// Once this is set, the text of every document in the documents folder is sent to that endpoint on each scan, not only
    /// what is asked about. Leave it empty unless the endpoint is one you are content to send your documents to. Ollama on
    /// this machine keeps them local.
    /// </para>
    /// </summary>
    public string EmbeddingModelId { get; set; } = string.Empty;
    public string ApiKeyEnvVar { get; set; } = "ANTHROPIC_API_KEY";

    /// <summary>The password manager entry the key is kept under when the environment variable is not set.</summary>
    public const string ApiKeyPasswordId = "ai:api-key";
    public string ApiBaseUrl { get; set; } = "https://api.anthropic.com/v1/";
    public int BarHeight { get; set; } = 5;
    // When true, the Ollama provider asks for no reasoning (reasoning_effort "none") so reasoning models
    // (e.g. qwen3.5) don't spend a long time generating chain-of-thought before replying. Ignored by the
    // OpenAI provider, whose models may not accept the parameter.
    public bool DisableThinking { get; set; } = false;
}
