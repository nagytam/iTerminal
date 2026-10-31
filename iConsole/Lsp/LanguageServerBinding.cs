using iConsole.Controls.Viewers;
using iConsole.Display;
using iConsole.Model;

namespace iConsole.Lsp;

/// <summary>
/// Connects one viewer or editor to a language server through an <see cref="LspClient"/>: the server is told the text when the
/// binding is made and again, a moment after the last change, whenever it changes; the diagnostics it publishes become curly
/// underlines in the text (red for an error, yellow for a warning, cyan for information, gray for a hint) with the message
/// found by <see cref="TextViewerControl.MessageAt"/>; and hover, completion and definition are asked for at the cursor. The
/// text is polled every <c>pollInterval</c> rather than watched, since not every edit announces itself. Dispose it to close the
/// document and remove the underlines.
/// </summary>
public sealed class LanguageServerBinding : IDisposable
{
    /// <summary>The <see cref="TextDecoration.Source"/> of the marks the binding sets.</summary>
    public const string DecorationSource = "lsp";

    private readonly TextViewerControl _view;
    private readonly LspClient _client;
    private readonly Timer _timer;
    private readonly Lock _gate = new();
    private string _lastSent;
    private IReadOnlyList<LspDiagnostic> _diagnostics = [];
    private bool _disposed;

    public LanguageServerBinding(TextViewerControl view, LspClient client, string uri, string languageId, TimeSpan? pollInterval = null)
    {
        _view = view;
        _client = client;
        Uri = uri;
        LanguageId = languageId;
        _lastSent = CurrentText();
        // Listen first: a quick server can publish before OpenDocument returns, and that first set would be lost.
        _client.DiagnosticsReceived += OnDiagnostics;
        _client.OpenDocument(uri, languageId, _lastSent);
        TimeSpan interval = pollInterval ?? TimeSpan.FromMilliseconds(300);
        _timer = new Timer(_ => Flush(), null, interval, interval);
    }

    public string Uri { get; }

    public string LanguageId { get; }

    /// <summary>The diagnostics the server last published for the document.</summary>
    public IReadOnlyList<LspDiagnostic> Diagnostics
    {
        get { lock (_gate) { return _diagnostics; } }
    }

    private string CurrentText() => string.Join("\n", _view.TextModel.Lines);

    /// <summary>Sends the text to the server now if it changed since it was last sent. Returns whether it was sent.</summary>
    public bool Flush()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return false;
            }

            string text = CurrentText();
            if (text == _lastSent)
            {
                return false;
            }

            _lastSent = text;
            _client.ChangeDocument(Uri, text);
            return true;
        }
    }

    private void OnDiagnostics(string uri, IReadOnlyList<LspDiagnostic> diagnostics)
    {
        if (!string.Equals(uri, Uri, StringComparison.Ordinal))
        {
            return;
        }

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
        }

        // The marks go on before the list is published, so whoever sees the new list also sees its marks.
        _view.Decorations.ReplaceSource(DecorationSource, DecorationsFor(diagnostics));
        lock (_gate)
        {
            if (!_disposed)
            {
                _diagnostics = diagnostics;
            }
        }
    }

    /// <summary>The marks for <paramref name="diagnostics"/>: one per line a diagnostic touches, an empty range widened to one character.</summary>
    private IEnumerable<TextDecoration> DecorationsFor(IReadOnlyList<LspDiagnostic> diagnostics)
    {
        IList<string> lines = _view.TextModel.Lines;
        foreach (LspDiagnostic diagnostic in diagnostics)
        {
            (Color color, int priority) = diagnostic.Severity switch
            {
                LspSeverity.Error => (Color.Red, 4),
                LspSeverity.Warning => (Color.Yellow, 3),
                LspSeverity.Information => (Color.Cyan, 2),
                _ => (Color.Gray, 1),
            };
            string message = diagnostic.Source is { Length: > 0 } ? $"{diagnostic.Source}: {diagnostic.Message}" : diagnostic.Message;
            LspRange range = diagnostic.Range;
            for (int line = range.Start.Line; line <= range.End.Line && line < lines.Count; line++)
            {
                if (line < 0)
                {
                    continue;
                }

                int length = lines[line].Length;
                int from = line == range.Start.Line ? Math.Clamp(range.Start.Character, 0, length) : 0;
                int to = line == range.End.Line ? Math.Clamp(range.End.Character, 0, length) : length;
                if (to <= from)
                {
                    to = Math.Min(length, from + 1);
                    from = Math.Min(from, Math.Max(0, to - 1));
                }

                if (to > from)
                {
                    yield return new TextDecoration(
                        line, from, to - from, DecorationSource,
                        Underline: UnderlineStyle.Curly, UnderlineColor: color, Message: message, Priority: priority);
                }
            }
        }
    }

    private LspPosition CursorPosition()
    {
        int line = (int)Math.Clamp(_view.ScrollableModel.CursorPosition.Y, 0, Math.Max(0, _view.TextModel.Lines.Count - 1));
        int column = (int)Math.Max(0, _view.ScrollableModel.CursorPosition.X);
        return new LspPosition(line, column);
    }

    /// <summary>The text the server shows for the cursor's place, or null.</summary>
    public Task<string?> HoverAtCursorAsync(CancellationToken cancellationToken = default)
    {
        Flush();
        return _client.HoverAsync(Uri, CursorPosition(), cancellationToken);
    }

    /// <summary>The completions at the cursor.</summary>
    public Task<IReadOnlyList<LspCompletionItem>> CompletionAtCursorAsync(CancellationToken cancellationToken = default)
    {
        Flush();
        return _client.CompletionAsync(Uri, CursorPosition(), cancellationToken);
    }

    /// <summary>Where the symbol at the cursor is defined.</summary>
    public Task<IReadOnlyList<LspLocation>> DefinitionAtCursorAsync(CancellationToken cancellationToken = default)
    {
        Flush();
        return _client.DefinitionAsync(Uri, CursorPosition(), cancellationToken);
    }

    /// <summary>The first diagnostic that starts after <paramref name="line"/> (wrapping to the first one), or null when there are none.</summary>
    public LspDiagnostic? NextDiagnostic(int line)
    {
        IReadOnlyList<LspDiagnostic> all = Diagnostics;
        return all.OrderBy(d => d.Range.Start.Line).ThenBy(d => d.Range.Start.Character)
            .FirstOrDefault(d => d.Range.Start.Line > line)
            ?? all.OrderBy(d => d.Range.Start.Line).ThenBy(d => d.Range.Start.Character).FirstOrDefault();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _timer.Dispose();
        _client.DiagnosticsReceived -= OnDiagnostics;
        _client.CloseDocument(Uri);
        _view.Decorations.Remove(DecorationSource);
    }
}
