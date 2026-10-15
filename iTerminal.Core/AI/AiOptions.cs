namespace iTerminal.Core.AI;

public class AiOptions
{
    public string ModelId { get; set; } = "claude-opus-4-5-20251101";
    public string ApiKeyEnvVar { get; set; } = "ANTHROPIC_API_KEY";

    /// <summary>The password manager entry the key is kept under when the environment variable is not set.</summary>
    public const string ApiKeyPasswordId = "ai:api-key";
    public string ApiBaseUrl { get; set; } = "https://api.anthropic.com/v1/";
    public int BarHeight { get; set; } = 5;
    // When true, use Ollama's native /api/chat endpoint with think=false so reasoning models
    // (e.g. qwen3.5) don't spend a long time generating chain-of-thought before replying.
    // Only meaningful when ApiBaseUrl points at a local Ollama server.
    public bool DisableThinking { get; set; } = false;
}
