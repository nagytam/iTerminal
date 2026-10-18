using iConsole.Controls.Viewers;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Services;
using iTerminal.Core.AI;
using Microsoft.Extensions.Options;
using System.Text;
using System.Text.Json;

namespace iTerminal.Core.Dialogs;

public class AiModelDialog : Dialog
{
    private readonly AiChatModel _model;
    private readonly AiOptions _options;
    private readonly TextViewerControl _textViewer;

    public AiModelDialog(AiChatModel model, IOptions<AiOptions> options, IThemeService themeService)
        : base(nameof(AiModelDialog), themeService)
    {
        _model = model;
        _options = options.Value;
        Title = "AI Model Information";
        _textViewer = new TextViewerControl(false, themeService);
        AddControl(_textViewer);
    }

    /// <summary>
    /// The model-details request the dialog last started. Nothing in the dialog awaits it — it
    /// exists so a test can wait for the details it just asked for instead of polling.
    /// </summary>
    internal Task DetailsTask { get; private set; } = Task.CompletedTask;

    public override async Task OnInitializeAsync()
    {
        await base.OnInitializeAsync();

        // The details come from the model server over HTTP, which can take the full five-second
        // timeout when the server is down. Awaiting them here held the key that opened the dialog
        // for that long, with nothing on screen; the status and configuration are known at once,
        // so they are shown at once and the details are filled in when they arrive.
        SetLines(BuildInfo(null, DetailsState.Loading));
        DetailsTask = Task.Run(async () =>
        {
            var details = await FetchModelDetailsAsync();
            SetLines(BuildInfo(details, details is null ? DetailsState.Unavailable : DetailsState.Loaded));
            StateHasChanged();
        });
    }

    private void SetLines(string text) =>
        _textViewer.TextModel.Lines = [.. text.TrimEnd().Split(Environment.NewLine)];

    internal enum DetailsState { Loading, Loaded, Unavailable }

    internal IReadOnlyList<string> LinesForTest => [.. _textViewer.TextModel.Lines.AsSnapshot()];

    public override void OnInitializeLayout()
    {
        if (Parent != null)
        {
            CenterInParent(new Size(70, 22));
        }

        _textViewer.Position = new Position(1, 1);
        _textViewer.ViewSize = new Size(ViewSize.Width - 2, ViewSize.Height - 2);

        base.OnInitializeLayout();
    }

    private string BuildInfo(ModelDetails? details, DetailsState state)
    {
        var sb = new StringBuilder();

        var userCount = _model.History.Count(m => m.Role == "user");
        var assistantCount = _model.History.Count(m => m.Role == "assistant");
        var apiKeySet = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(_options.ApiKeyEnvVar));

        sb.AppendLine("══ Status ════════════════════════════════════════════════════");
        sb.AppendLine($"  Available      : {(_model.IsAvailable ? "Yes" : "No")}");
        sb.AppendLine($"  Info           : {_model.ModelInfo}");

        sb.AppendLine();
        sb.AppendLine("══ Configuration ═════════════════════════════════════════════");
        sb.AppendLine($"  Model ID       : {_options.ModelId}");
        sb.AppendLine($"  API URL        : {_options.ApiBaseUrl}");
        sb.AppendLine($"  API Key Env    : {_options.ApiKeyEnvVar}");
        sb.AppendLine($"  API Key Set    : {(apiKeySet ? "Yes" : "No (unauthenticated)")}");

        sb.AppendLine();
        sb.AppendLine("══ Session ═══════════════════════════════════════════════════");
        sb.AppendLine($"  Messages       : {_model.History.Count} ({userCount} user, {assistantCount} assistant)");
        sb.AppendLine($"  Thinking       : {(_model.IsThinking ? "Yes" : "No")}");

        sb.AppendLine();
        sb.AppendLine("══ Model Details ═════════════════════════════════════════════");
        if (state == DetailsState.Loading)
        {
            sb.AppendLine("  Loading from the model server…");
        }
        else if (details is null)
        {
            // Said rather than left out: a missing section reads as "this model has no details".
            sb.AppendLine("  Not available — the server is unreachable or does not report them.");
        }
        else
        {
            if (details.ParameterSize is not null)
                sb.AppendLine($"  Parameters     : {details.ParameterSize}");
            if (details.Family is not null)
                sb.AppendLine($"  Family         : {details.Family}");
            if (details.Quantization is not null)
                sb.AppendLine($"  Quantization   : {details.Quantization}");
            if (details.Format is not null)
                sb.AppendLine($"  Format         : {details.Format}");
            if (details.SizeBytes is not null)
                sb.AppendLine($"  Size           : {details.SizeBytes.Value / 1024.0 / 1024 / 1024:F2} GB");
        }

        sb.AppendLine();
        sb.AppendLine("  Esc:close");

        return sb.ToString();
    }

    private async Task<ModelDetails?> FetchModelDetailsAsync()
    {
        try
        {
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var apiKey = Environment.GetEnvironmentVariable(_options.ApiKeyEnvVar);
            if (!string.IsNullOrEmpty(apiKey))
                http.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

            var url = _options.ApiBaseUrl.TrimEnd('/') + "/models";
            var response = await http.GetAsync(url);
            if (!response.IsSuccessStatusCode) return null;

            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("data", out var data)) return null;

            foreach (var m in data.EnumerateArray())
            {
                var id = m.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                if (!string.Equals(id, _options.ModelId, StringComparison.OrdinalIgnoreCase))
                    continue;

                var details = new ModelDetails();

                // Ollama enriches /v1/models entries with a nested "details" object
                if (m.TryGetProperty("details", out var det))
                {
                    details.Family = det.TryGetProperty("family", out var f) ? f.GetString() : null;
                    details.Quantization = det.TryGetProperty("quantization_level", out var q) ? q.GetString() : null;
                    details.Format = det.TryGetProperty("format", out var fmt) ? fmt.GetString() : null;
                    details.ParameterSize = det.TryGetProperty("parameter_size", out var ps) ? ps.GetString() : null;
                }

                if (m.TryGetProperty("size", out var sz) && sz.TryGetInt64(out var bytes))
                    details.SizeBytes = bytes;

                return details;
            }
        }
        catch { /* server unreachable or non-Ollama provider — skip details section */ }

        return null;
    }

    private sealed class ModelDetails
    {
        public string? Family { get; set; }
        public string? ParameterSize { get; set; }
        public string? Quantization { get; set; }
        public string? Format { get; set; }
        public long? SizeBytes { get; set; }
    }
}
