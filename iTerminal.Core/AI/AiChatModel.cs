using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace iTerminal.Core.AI;

public record AiChatMessage(string Role, string Content);

public class AiChatModel : INotifyPropertyChanged
{
    /// <summary>What <see cref="ModelInfo"/> says until the availability check answers. The prompt bar spins while it is shown.</summary>
    public const string CheckingModelInfo = "checking…";

    private string _input = string.Empty;
    private string _lastResponse = string.Empty;
    private bool _isThinking = false;
    private bool _isAvailable = false;
    private string _modelInfo = CheckingModelInfo;

    public List<AiChatMessage> History { get; } = [];

    public bool IsAvailable
    {
        get => _isAvailable;
        set { _isAvailable = value; OnPropertyChanged(); }
    }

    private Func<string>? _modelInfoSource;

    public string ModelInfo
    {
        get => _modelInfoSource?.Invoke() ?? _modelInfo;
        set { _modelInfoSource = null; _modelInfo = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// A status that is a sentence in the chosen language: read again each time it is shown, so a language change
    /// reaches it instead of leaving the words it was first worded in.
    /// </summary>
    public Func<string> ModelInfoSource
    {
        set { _modelInfoSource = value; OnPropertyChanged(nameof(ModelInfo)); }
    }

    public string Input
    {
        get => _input;
        set { _input = value; OnPropertyChanged(); }
    }

    public string LastResponse
    {
        get => _lastResponse;
        set { _lastResponse = value; OnPropertyChanged(); }
    }

    public bool IsThinking
    {
        get => _isThinking;
        set { _isThinking = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
