using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Controls.Contents;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace iTerminal.Core.AI;

public class AiChatService : IAiChatService
{
    private readonly IChatClient _chatClient;
    private readonly IContentModelService _contentModelService;
    private readonly IUiService _uiService;
    private readonly AiOptions _aiOptions;
    private readonly List<AITool> _tools;

    private static readonly string SystemPrompt =
        "You are an AI assistant integrated into iTerminal, a TUI file manager. " +
        "You can navigate file panels, control panel layout, and open dialogs. " +
        "Keep responses short — they are displayed in a small terminal bar.";

    public AiChatService(IChatClient chatClient, IContentModelService contentModelService, IUiService uiService, IOptions<AiOptions> aiOptions)
    {
        _chatClient = chatClient;
        _contentModelService = contentModelService;
        _uiService = uiService;
        _aiOptions = aiOptions.Value;
        _tools = BuildTools();
    }

    /// <summary>
    /// Says what to do about a refused availability check, not just its status code: a bare
    /// "server error (401)" left the user to work out that an API key was missing, and which one.
    /// </summary>
    internal static string DescribeFailure(int statusCode, bool apiKeySet, string apiKeyEnvVar) => statusCode switch
    {
        401 or 403 when !apiKeySet => $"no API key — set {apiKeyEnvVar}",
        401 or 403 => $"API key refused ({statusCode}) — check {apiKeyEnvVar}",
        404 => $"no models endpoint ({statusCode}) — check the API URL",
        429 => "rate limited (429) — try again later",
        >= 500 => $"server error ({statusCode})",
        _ => $"request refused ({statusCode})",
    };

    public async Task CheckAvailabilityAsync(AiChatModel model)
    {
        if (_aiOptions.Provider == AiProvider.None)
        {
            model.IsAvailable = false;
            model.ModelInfo = "AI is off (AI:Provider)";
            return;
        }

        try
        {
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var apiKey = Environment.GetEnvironmentVariable(_aiOptions.ApiKeyEnvVar);
            if (!string.IsNullOrEmpty(apiKey))
                http.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

            var url = _aiOptions.ApiBaseUrl.TrimEnd('/') + "/models";
            var response = await http.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                model.IsAvailable = false;
                model.ModelInfo = DescribeFailure((int)response.StatusCode, apiKeySet: !string.IsNullOrEmpty(apiKey), _aiOptions.ApiKeyEnvVar);
                return;
            }

            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);

            // OpenAI-compatible /v1/models response: { "data": [ { "id": "..." }, ... ] }
            // Ollama /v1/models response has the same shape
            var ids = doc.RootElement.TryGetProperty("data", out var data)
                ? data.EnumerateArray().Select(m => m.GetProperty("id").GetString() ?? string.Empty)
                : [];

            if (ids.Contains(_aiOptions.ModelId, StringComparer.OrdinalIgnoreCase))
            {
                model.IsAvailable = true;
                model.ModelInfo = _aiOptions.ModelId;
            }
            else
            {
                model.IsAvailable = false;
                model.ModelInfo = $"model not found: {_aiOptions.ModelId}";
            }
        }
        catch (System.Net.Http.HttpRequestException)
        {
            model.IsAvailable = false;
            model.ModelInfo = "server unreachable";
        }
        catch (TaskCanceledException)
        {
            model.IsAvailable = false;
            model.ModelInfo = "server timeout";
        }
        catch (Exception ex)
        {
            model.IsAvailable = false;
            model.ModelInfo = $"error: {ex.Message}";
        }
    }

    public async Task SendMessageAsync(AiChatModel model, CancellationToken ct = default)
    {
        var userMessage = model.Input.Trim();
        if (string.IsNullOrEmpty(userMessage)) return;

        model.History.Add(new AiChatMessage("user", userMessage));
        model.Input = string.Empty;
        model.IsThinking = true;
        model.LastResponse = "Thinking...";

        try
        {
            var messages = BuildMessages(model.History);
            var options = new ChatOptions { Tools = _tools, ToolMode = ChatToolMode.Auto };

            var response = await _chatClient.GetResponseAsync(messages, options, ct);
            var raw = string.Concat(response.Messages
                .Where(m => m.Role == ChatRole.Assistant)
                .Select(m => m.Text ?? string.Empty));
            var text = Sanitize(raw);

            model.History.Add(new AiChatMessage("assistant", text));
            model.LastResponse = text;
        }
        catch (OperationCanceledException)
        {
            model.LastResponse = "Cancelled.";
        }
        catch (Exception ex)
        {
            model.LastResponse = $"Error: {ex.Message}";
        }
        finally
        {
            model.IsThinking = false;
        }
    }

    // Strips <think>...</think> blocks and ANSI escape sequences from model output.
    private static readonly Regex ThinkTagRegex = new(@"<think>.*?</think>", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex AnsiEscapeRegex = new(@"\x1b(\[[0-9;]*[A-Za-z]|[^[])", RegexOptions.Compiled);

    private static string Sanitize(string text)
    {
        text = ThinkTagRegex.Replace(text, string.Empty);
        text = AnsiEscapeRegex.Replace(text, string.Empty);
        return text.Trim();
    }

    private List<ChatMessage> BuildMessages(List<AiChatMessage> history)
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, SystemPrompt)
        };

        foreach (var msg in history)
        {
            var role = msg.Role == "user" ? ChatRole.User : ChatRole.Assistant;
            messages.Add(new ChatMessage(role, msg.Content));
        }

        return messages;
    }

    private List<AITool> BuildTools() =>
    [
        AIFunctionFactory.Create(
            () => _contentModelService.Url?.Path ?? "/",
            "get_current_path",
            "Get the path currently shown in the focused file panel"),

        AIFunctionFactory.Create(
            async (string path) =>
            {
                await _contentModelService.SetUrlAsync(new Url(path));
                return $"Navigated to: {path}";
            },
            "navigate_focused_panel",
            "Navigate the focused file panel to a folder path"),

        AIFunctionFactory.Create(
            async (string path) =>
            {
                await _contentModelService.SetUrlAsync(Content.Left, new Url(path));
                return $"Left panel navigated to: {path}";
            },
            "navigate_left_panel",
            "Navigate the left file panel to a folder path"),

        AIFunctionFactory.Create(
            async (string path) =>
            {
                await _contentModelService.SetUrlAsync(Content.Right, new Url(path));
                return $"Right panel navigated to: {path}";
            },
            "navigate_right_panel",
            "Navigate the right file panel to a folder path"),

        AIFunctionFactory.Create(
            (string path) =>
            {
                try
                {
                    var entries = Directory.GetFileSystemEntries(path)
                        .Select(e => (isDir: Directory.Exists(e), name: Path.GetFileName(e)))
                        .OrderBy(e => e.isDir ? 0 : 1)
                        .ThenBy(e => e.name, StringComparer.OrdinalIgnoreCase)
                        .Select(e => e.isDir ? $"[{e.name}]" : e.name);
                    return string.Join("\n", entries);
                }
                catch (Exception ex)
                {
                    return $"Error: {ex.Message}";
                }
            },
            "list_folder",
            "List files and folders at a path. Folders are shown in [brackets]."),

        AIFunctionFactory.Create(
            () => { _uiService.ShowFilePanels(); return "Showing file panels."; },
            "show_file_panels",
            "Switch to default split view: left and right file panels with the AI bar at the bottom."),

        AIFunctionFactory.Create(
            () => { _uiService.ShowFullTerminal(); return "Showing full-screen terminal."; },
            "show_full_terminal",
            "Expand the terminal to fill the entire screen, hiding the file panels."),

        AIFunctionFactory.Create(
            () => { _uiService.ShowTerminalBar(); return "Showing terminal bar."; },
            "show_terminal_bar",
            "Show the terminal in the bottom bar alongside the file panels."),

        AIFunctionFactory.Create(
            () => { _uiService.ShowAiBar(); return "Showing AI bar."; },
            "show_ai_bar",
            "Show the AI prompt bar at the bottom alongside the file panels."),

        AIFunctionFactory.Create(
            () => { _uiService.FocusLeftPanel(); return "Focused left file panel."; },
            "focus_left_panel",
            "Move keyboard focus to the left file panel."),

        AIFunctionFactory.Create(
            () => { _uiService.FocusRightPanel(); return "Focused right file panel."; },
            "focus_right_panel",
            "Move keyboard focus to the right file panel."),

        AIFunctionFactory.Create(
            async (string dialog_name) =>
            {
                await _uiService.OpenDialogAsync(dialog_name);
                return $"Opened {dialog_name}.";
            },
            "open_dialog",
            "Open a dialog by name. Available names: settings, help, about, disk_manager, process_manager, network_manager, system_info, task_manager, theme_manager, screen_manager, ai_model, git (the Git client: changed files, staging area, commit history)."),

        AIFunctionFactory.Create(
            () => { _uiService.Quit(); return "Closing iTerminal. Goodbye!"; },
            "quit_application",
            "Close and exit the iTerminal application. Call this whenever the user wants to quit, exit, leave, close, or shut down the program (e.g. \"quit\", \"exit\", \"close the app\", \"I'm done\", \"bye\")."),
    ];
}
