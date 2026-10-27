using iConsole.Display;
using iConsole.Helper;
using iConsole.Model;

namespace iConsole.Controls.Viewers;

/// <summary>Line and range decorations, and spell checking, for the viewer and the editor.</summary>
public partial class TextViewerControl
{
    private DecorationSet _decorations = new();
    private long _drawnDecorationVersion = -1;

    /// <summary>
    /// The marks on the text: diagnostics, diff lines, blame colours. Anything may add to it from any thread; the next draw
    /// shows the change. See <see cref="TextDecoration"/>.
    /// </summary>
    public DecorationSet Decorations
    {
        get => _decorations;
        set
        {
            _decorations = value ?? new DecorationSet();
            StateHasChanged();
        }
    }

    /// <summary>The message of the mark at <paramref name="line"/>, <paramref name="column"/> (a diagnostic's text, say), or null.</summary>
    public string? MessageAt(int line, int column) => _decorations.MessageAt(line, column)?.Message;

    /// <summary>
    /// Repaints when a mark was added or removed since the last frame. Marks arrive on worker threads, so a control cannot be told
    /// to repaint from there; it notices here.
    /// </summary>
    public override void OnIdle()
    {
        base.OnIdle();
        bool spellChanged = _spellVersion != _drawnSpellVersion;
        if (_decorations.Version != _drawnDecorationVersion || spellChanged)
        {
            StateHasChanged();
        }
    }

    /// <summary>
    /// Applies the marks of <paramref name="line"/> to <paramref name="drawn"/>, a row that starts at text column
    /// <paramref name="firstColumn"/>. Colours are left alone on the cursor line, which has its own; the other styles still apply.
    /// </summary>
    private void ApplyDecorations(ConxelString drawn, int line, int firstColumn, bool isCursorLine)
    {
        _drawnDecorationVersion = _decorations.Version;
        IReadOnlyList<TextDecoration> marks = _decorations.ForLine(line);
        foreach (TextDecoration mark in marks)
        {
            int from = Math.Max(0, mark.Column - firstColumn);
            int to = mark.Length < 0 ? drawn.Count : Math.Min(drawn.Count, mark.End - firstColumn);
            for (int x = from; x < to; x++)
            {
                Conxel cell = drawn.String[x];
                if (!isCursorLine)
                {
                    if (mark.Foreground is { } foreground)
                    {
                        cell.ForegroundColor = foreground;
                    }

                    if (mark.Background is { } background)
                    {
                        cell.BackgroundColor = background;
                    }
                }

                if (mark.Underline is { } style)
                {
                    cell.IsUnderline = true;
                    cell.UnderlineStyle = style;
                    if (mark.UnderlineColor is { } underlineColor)
                    {
                        cell.HasUnderlineColor = true;
                        cell.UnderlineColor = underlineColor;
                    }
                }

                cell.IsBold |= mark.Bold;
                cell.IsItalic |= mark.Italic;
                cell.IsDim |= mark.Dim;
                drawn.String[x] = cell;
            }
        }
    }

    // ---- Spell checking ----

    private ISpellChecker? _spellChecker;
    private int _spellVersion;
    private int _drawnSpellVersion = -1;
    private readonly Dictionary<string, (int Start, int Length)[]> _misspelledCache = [];

    private const int SpellCacheLimit = 4000;

    /// <summary>
    /// Underlines misspelled words with a curly line. Null (the default) checks nothing. With a language set (<see cref="Syntax"/>)
    /// only comments and strings are checked, so code is left alone; without one, every word is. A word is judged by
    /// <see cref="ISpellChecker.IsCorrect"/>; only the rows on screen are looked at.
    /// </summary>
    public ISpellChecker? SpellChecker
    {
        get => _spellChecker;
        set
        {
            _spellChecker = value;
            ResetSpellCache();
        }
    }

    /// <summary>Forget what was found, because the words or the checker changed (a word was added to the dictionary).</summary>
    public void ResetSpellCache()
    {
        _misspelledCache.Clear();
        _spellVersion++;
        StateHasChanged();
    }

    /// <summary>The misspelled words of <paramref name="text"/> as start and length pairs, or none when no checker is set.</summary>
    internal (int Start, int Length)[] MisspelledRanges(string text)
    {
        ISpellChecker? checker = _spellChecker;
        if (checker is null || text.Length == 0)
        {
            return [];
        }

        if (_misspelledCache.TryGetValue(text, out (int Start, int Length)[]? cached))
        {
            return cached;
        }

        // In code only comments and strings are prose.
        List<SyntaxSpan>? prose = null;
        if (_syntax is not null)
        {
            prose = [];
            Highlighting.Tokenize(_syntax, text, false, prose);
            prose = [.. prose.Where(s => s.Kind is SyntaxKind.Comment or SyntaxKind.String)];
        }

        var found = new List<(int Start, int Length)>();
        foreach ((int start, int length) in Helper.SpellChecker.Words(text))
        {
            if (prose is not null && !prose.Any(s => start >= s.Start && start < s.Start + s.Length))
            {
                continue;
            }

            if (!checker.IsCorrect(text.Substring(start, length)))
            {
                found.Add((start, length));
            }
        }

        if (_misspelledCache.Count >= SpellCacheLimit)
        {
            _misspelledCache.Clear();
        }

        (int Start, int Length)[] result = [.. found];
        _misspelledCache[text] = result;
        return result;
    }

    /// <summary>Underlines the misspelled words in the part of <paramref name="lineText"/> that <paramref name="drawn"/> shows.</summary>
    private void ApplySpelling(ConxelString drawn, string lineText, int firstColumn, int count, ColorScheme scheme)
    {
        _drawnSpellVersion = _spellVersion;
        if (_spellChecker is null)
        {
            return;
        }

        int shown = Math.Min(Math.Min(drawn.Count, count), Math.Max(0, lineText.Length - firstColumn));
        foreach ((int start, int length) in MisspelledRanges(lineText))
        {
            for (int column = Math.Max(start, firstColumn); column < start + length && column - firstColumn < shown; column++)
            {
                Conxel cell = drawn.String[column - firstColumn];
                cell.IsUnderline = true;
                cell.UnderlineStyle = UnderlineStyle.Curly;
                cell.HasUnderlineColor = true;
                cell.UnderlineColor = scheme.Error;
                drawn.String[column - firstColumn] = cell;
            }
        }
    }

    /// <summary>The misspelled word at <paramref name="line"/>, <paramref name="column"/> with its place in the line, or null.</summary>
    public (string Word, int Start)? MisspelledWordAt(int line, int column)
    {
        if (line < 0 || line >= TextModel.Lines.Count)
        {
            return null;
        }

        string text = TextModel.Lines[line];
        foreach ((int start, int length) in MisspelledRanges(text))
        {
            // The cursor may sit just past the last letter of the word it is typing.
            if (column >= start && column <= start + length)
            {
                return (text.Substring(start, length), start);
            }
        }

        return null;
    }

    /// <summary>What the checker suggests for the misspelled word at <paramref name="line"/>, <paramref name="column"/>; empty when it is spelled right.</summary>
    public IReadOnlyList<string> SuggestionsAt(int line, int column)
    {
        return MisspelledWordAt(line, column) is { } word && _spellChecker is { } checker ? checker.Suggest(word.Word) : [];
    }

    /// <summary>
    /// Adds the misspelled word at <paramref name="line"/>, <paramref name="column"/> to the user's dictionary, so it is not underlined
    /// again. Returns false when there is no such word or the checker is not a <see cref="Helper.SpellChecker"/>.
    /// </summary>
    public bool AddWordToDictionary(int line, int column)
    {
        if (MisspelledWordAt(line, column) is not { } word || _spellChecker is not Helper.SpellChecker checker)
        {
            return false;
        }

        checker.Add(word.Word);
        ResetSpellCache();
        return true;
    }
}
