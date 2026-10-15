using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace iTerminal.Core.AI;

public record AiChatMessage(string Role, string Content);

public class AiChatModel : INotifyPropertyChanged
{
    private string _input = string.Empty;
    private string _lastResponse = string.Empty;
    private bool _isThinking = false;
    private bool _isAvailable = false;
    private string _modelInfo = "checking...";

    public List<AiChatMessage> History { get; } = [];

    public bool IsAvailable
    {
        get => _isAvailable;
        set { _isAvailable = value; OnPropertyChanged(); }
    }

    public string ModelInfo
    {
        get => _modelInfo;
        set { _modelInfo = value; OnPropertyChanged(); }
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
