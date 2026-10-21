namespace iConsole.Services;

/// <summary>
/// The text clipboard: what a text box or the editor copies to and pastes from. Line breaks are a single line feed on
/// this side; an implementation converts to and from whatever the system uses.
///
/// <para>Async by contract: the clipboard is shared with every other program, and one that owns it can keep it busy
/// for a moment, which a key handler must not wait for.</para>
/// </summary>
public interface ITextClipboardService
{
    /// <summary>Replaces the clipboard's contents with <paramref name="text"/>.</summary>
    Task SetTextAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>The text on the clipboard, or null when it holds none (files, an image, nothing).</summary>
    Task<string?> GetTextAsync(CancellationToken cancellationToken = default);
}

/// <summary>A clipboard that lives in this process only: the default, and what tests use.</summary>
public sealed class InProcessTextClipboardService : ITextClipboardService
{
    private readonly object _lock = new();
    private string? _text;

    public Task SetTextAsync(string text, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _text = text;
        }

        return Task.CompletedTask;
    }

    public Task<string?> GetTextAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            return Task.FromResult(_text);
        }
    }
}

/// <summary>
/// The clipboard the text controls use unless they are given another (<c>TextBox.Clipboard</c>,
/// <c>TextEditorControl.Clipboard</c>). The application puts the system clipboard here at start-up; until it does, and
/// in any code that never does, it is an in-process one, so a copy and its paste still agree.
/// </summary>
public static class TextClipboard
{
    private static ITextClipboardService _current = new InProcessTextClipboardService();

    public static ITextClipboardService Current
    {
        get => Volatile.Read(ref _current);
        set => Volatile.Write(ref _current, value ?? throw new ArgumentNullException(nameof(value)));
    }
}
