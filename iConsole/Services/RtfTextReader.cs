using System.Text;

namespace iConsole.Services;

/// <summary>
/// Reads the text of a Rich Text Format file (.rtf) into lines. The formatting is dropped and the document's
/// own text kept: <c>\par</c> and <c>\line</c> end a line, <c>\tab</c> is a tab, a table cell is separated from
/// the next by a box-drawn bar and a row ends its line. Escaped bytes (<c>\'hh</c>) are decoded with the code page
/// the file declares (<c>\ansicpg</c>, Windows-1252 when it declares none), and <c>\uN</c> with its fallback
/// character skipped. Destinations that hold no document text — fonts, colours, styles, metadata, pictures,
/// embedded objects, headers and footers, list numbering — are skipped whole.
/// </summary>
public static class RtfTextReader
{
    private const int DefaultCodePage = 1252;

    /// <summary>Destinations whose contents are not body text. An unknown <c>\*</c> destination is skipped too.</summary>
    private static readonly HashSet<string> SkippedDestinations = new(StringComparer.Ordinal)
    {
        "fonttbl", "colortbl", "stylesheet", "info", "pict", "shppict", "nonshppict", "object", "objdata",
        "fldinst", "themedata", "colorschememapping", "datastore", "latentstyles", "listtable",
        "listoverridetable", "rsidtbl", "generator", "xmlnstbl", "filetbl", "revtbl", "header", "headerl",
        "headerr", "headerf", "footer", "footerl", "footerr", "footerf", "listtext", "pntext", "pn",
        "bkmkstart", "bkmkend", "blipuid", "mmathPr", "falt", "panose",
    };

    /// <summary>Control words that stand for one character of text.</summary>
    private static readonly Dictionary<string, string> SymbolWords = new(StringComparer.Ordinal)
    {
        ["emdash"] = "—",
        ["endash"] = "–",
        ["lquote"] = "‘",
        ["rquote"] = "’",
        ["ldblquote"] = "“",
        ["rdblquote"] = "”",
        ["bullet"] = "•",
        ["emspace"] = " ",
        ["enspace"] = " ",
        ["qmspace"] = " ",
    };

    static RtfTextReader()
    {
        // Windows code pages (1250–1258, 437, 932 …) are not in the base framework.
        Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
    }

    public static IReadOnlyList<string> Read(byte[] content) => new Parser(content).Parse();

    /// <summary>The formatting a group ({ … }) restores when it closes.</summary>
    private sealed class GroupState
    {
        public bool Skip;
        public int UnicodeSkip = 1;

        public GroupState Clone() => new() { Skip = Skip, UnicodeSkip = UnicodeSkip };
    }

    private sealed class Parser(byte[] content)
    {
        // Every byte maps to one char, so the control words (ASCII) and the raw bytes read as they are on disk.
        private readonly string _text = Encoding.Latin1.GetString(content);
        private readonly StringBuilder _output = new();
        private readonly List<byte> _pending = [];
        private readonly Stack<GroupState> _groups = new();
        private Encoding _encoding = Encoding.GetEncoding(DefaultCodePage);
        private GroupState _state = new();
        private int _index;

        public IReadOnlyList<string> Parse()
        {
            _groups.Push(_state);
            while (_index < _text.Length)
            {
                char c = _text[_index];
                switch (c)
                {
                    case '{':
                        Flush();
                        _state = _state.Clone();
                        _groups.Push(_state);
                        _index++;
                        break;

                    case '}':
                        Flush();
                        if (_groups.Count > 1)
                        {
                            _groups.Pop();
                            _state = _groups.Peek();
                        }

                        _index++;
                        break;

                    case '\\':
                        ReadControl();
                        break;

                    case '\r':
                    case '\n':
                        // A raw line break is whitespace in the source, not in the document.
                        Flush();
                        _index++;
                        break;

                    default:
                        if (c >= 0x80)
                        {
                            if (!_state.Skip)
                            {
                                _pending.Add((byte)c);
                            }
                        }
                        else
                        {
                            Emit(c.ToString());
                        }

                        _index++;
                        break;
                }
            }

            Flush();
            return Lines(_output.ToString());
        }

        /// <summary>Writes the escaped bytes gathered so far, decoded with the document's code page.</summary>
        private void Flush()
        {
            if (_pending.Count > 0)
            {
                if (!_state.Skip)
                {
                    _output.Append(_encoding.GetString([.. _pending]));
                }

                _pending.Clear();
            }
        }

        private void Emit(string value)
        {
            Flush();
            if (!_state.Skip)
            {
                _output.Append(value);
            }
        }

        /// <summary>Handles the control symbol or word at the current index and moves past it.</summary>
        private void ReadControl()
        {
            if (_index + 1 >= _text.Length)
            {
                _index = _text.Length;
                return;
            }

            char next = _text[_index + 1];

            if (next == '\'')
            {
                // \'hh is one byte of the document's code page; a run of them decodes as one string.
                if (_index + 3 < _text.Length && TryHex(_text[_index + 2], _text[_index + 3], out byte value))
                {
                    if (!_state.Skip)
                    {
                        _pending.Add(value);
                    }

                    _index += 4;
                }
                else
                {
                    _index += 2;
                }

                return;
            }

            if (next is '\\' or '{' or '}')
            {
                Emit(next.ToString());
                _index += 2;
                return;
            }

            if (next == '*')
            {
                // An ignorable destination: this reader does not know it, so the whole group is not body text.
                Flush();
                _state.Skip = true;
                _index += 2;
                return;
            }

            if (next == '~')
            {
                Emit(" ");
                _index += 2;
                return;
            }

            if (next == '_')
            {
                Emit("-");
                _index += 2;
                return;
            }

            if (next is '\r' or '\n')
            {
                Emit("\n");
                _index += 2;
                return;
            }

            if (next == '-')
            {
                // An optional hyphen shows nothing unless the line breaks there.
                _index += 2;
                return;
            }

            if (!char.IsAsciiLetter(next))
            {
                // Any other control symbol (\: \| \^ …) is a single character with no text.
                _index += 2;
                return;
            }

            ReadControlWord();
        }

        private void ReadControlWord()
        {
            int i = _index + 1;
            while (i < _text.Length && char.IsAsciiLetter(_text[i]))
            {
                i++;
            }

            string word = _text[(_index + 1)..i];

            bool hasParameter = false;
            int parameter = 0;
            int digitsStart = i < _text.Length && _text[i] == '-' ? i + 1 : i;
            int end = digitsStart;
            while (end < _text.Length && char.IsAsciiDigit(_text[end]))
            {
                end++;
            }

            if (end > digitsStart)
            {
                hasParameter = true;
                parameter = int.Parse(_text.AsSpan(digitsStart, Math.Min(end - digitsStart, 9)));
                if (digitsStart != i)
                {
                    parameter = -parameter;
                }

                i = end;
            }

            // One space after a control word is its delimiter, not text.
            if (i < _text.Length && _text[i] == ' ')
            {
                i++;
            }

            _index = i;

            if (SkippedDestinations.Contains(word))
            {
                _state.Skip = true;
                return;
            }

            switch (word)
            {
                case "par":
                case "line":
                case "row":
                case "page":
                case "sect":
                    Emit("\n");
                    break;

                case "cell":
                    Emit(" │ ");
                    break;

                case "tab":
                    Emit("\t");
                    break;

                case "ansicpg" when hasParameter:
                    SetCodePage(parameter);
                    break;

                case "uc":
                    _state.UnicodeSkip = hasParameter ? Math.Max(0, parameter) : 1;
                    break;

                case "u" when hasParameter:
                    // \uN is a signed 16-bit code unit; the next UnicodeSkip units are its fallback text.
                    Emit(((char)(parameter < 0 ? parameter + 65536 : parameter)).ToString());
                    SkipFallback();
                    break;

                case "bin" when hasParameter:
                    // Raw bytes follow the control word; they are not parsed.
                    _index = Math.Min(_text.Length, _index + Math.Max(0, parameter));
                    break;

                default:
                    if (SymbolWords.TryGetValue(word, out string? symbol))
                    {
                        Emit(symbol);
                    }

                    break;
            }
        }

        /// <summary>Steps over the fallback characters after a \u; an escaped byte counts as one.</summary>
        private void SkipFallback()
        {
            for (int skipped = 0; skipped < _state.UnicodeSkip && _index < _text.Length; skipped++)
            {
                _index += _text[_index] == '\\' && _index + 3 < _text.Length && _text[_index + 1] == '\'' ? 4 : 1;
            }
        }

        private void SetCodePage(int codePage)
        {
            Flush();
            try
            {
                _encoding = Encoding.GetEncoding(codePage);
            }
            catch (ArgumentException)
            {
                // A code page this runtime does not have: keep reading with the one in use.
            }
        }

        private static bool TryHex(char high, char low, out byte value)
        {
            int h = HexValue(high);
            int l = HexValue(low);
            value = (byte)(h * 16 + l);
            return h >= 0 && l >= 0;
        }

        private static int HexValue(char c) => c switch
        {
            >= '0' and <= '9' => c - '0',
            >= 'a' and <= 'f' => c - 'a' + 10,
            >= 'A' and <= 'F' => c - 'A' + 10,
            _ => -1,
        };
    }

    private static IReadOnlyList<string> Lines(string text)
    {
        List<string> lines = [];
        foreach (string line in text.Split('\n'))
        {
            lines.Add(line.TrimEnd());
        }

        while (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return lines;
    }
}
