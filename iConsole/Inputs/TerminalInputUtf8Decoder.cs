using System.Buffers;
using System.Runtime.InteropServices;
using System.Text;
using iConsole.Data;

namespace iConsole.Inputs;

/// <summary>
/// Incremental UTF-8 stdin decoder: SGR mouse (CSI &lt;…M/m), common CSI/SS3 keys, UTF-8 text, Alt+char.
/// Pure logic — usable from Linux TTY reads or Windows VT stdin.
/// </summary>
public sealed class TerminalInputUtf8Decoder
{
    private readonly List<byte> _buffer = [];

    public void Append(ReadOnlySpan<byte> data)
    {
        foreach (var b in data)
        {
            _buffer.Add(b);
        }
    }

    public void Drain(Action<KeyEvent> emitKey, Action<MouseEvent> emitMouse)
    {
        while (TryConsumeOne(emitKey, emitMouse))
        {
        }
    }

    // Call after stdin is confirmed empty (EAGAIN / n==0). If the only buffered byte is
    // 0x1b it is a standalone Escape, not the start of a multi-byte escape sequence.
    public void DrainStandaloneEscape(Action<KeyEvent> emitKey)
    {
        if (_buffer.Count == 1 && _buffer[0] == 0x1b)
        {
            emitKey(new KeyEvent('\x1b', ConsoleKey.Escape, false, false, false));
            _buffer.Clear();
        }
    }

    private bool TryConsumeOne(Action<KeyEvent> emitKey, Action<MouseEvent> emitMouse)
    {
        if (_buffer.Count == 0)
        {
            return false;
        }

        var b = CollectionsMarshal.AsSpan(_buffer);
        if (b[0] != 0x1b)
        {
            return TryConsumeUtf8Text(b, emitKey);
        }

        if (b.Length < 2)
        {
            return false;
        }

        if (b[1] == 0x1b)
        {
            if (b.Length < 3)
            {
                return false;
            }

            if (b[2] < 0x20)
            {
                RemovePrefix(2);
                return true;
            }

            var ch = (char)b[2];
            emitKey(KeyFromChar(ch, alt: true));
            RemovePrefix(3);
            return true;
        }

        if (b[1] == (byte)'[')
        {
            if (!TryFindCsiTerminator(b, out var endInclusive))
            {
                return false;
            }

            var fullLen = endInclusive + 1;
            var csi = b[..fullLen];
            if (TryParseCellSizeReply(csi) || TryParseKittyKeyboardReply(csi) || TryParseKittyKey(csi, emitKey))
            {
                RemovePrefix(fullLen);
                return true;
            }

            if (TryParseSgrMouse(csi, emitMouse))
            {
                RemovePrefix(fullLen);
                return true;
            }

            // "ESC [ I" and "ESC [ O": the window got or lost the keyboard focus (mode 1004).
            if (csi.Length == 3 && csi[2] is (byte)'I' or (byte)'O')
            {
                FocusReported?.Invoke(csi[2] == (byte)'I');
                RemovePrefix(fullLen);
                return true;
            }

            if (TryParseCsiKey(csi, emitKey))
            {
                RemovePrefix(fullLen);
                return true;
            }

            RemovePrefix(fullLen);
            return true;
        }

        if (b[1] == (byte)'O' && b.Length >= 3)
        {
            if (TryParseSs3Arrow(b[2], emitKey) || TryParseRxvtCtrlArrow(b[2], emitKey))
            {
                RemovePrefix(3);
                return true;
            }

            // "ESC O <modifier> <letter>" — the modified arrow in the SS3 shape some terminals send
            // (Ctrl+Up is "ESC O 5 A"). A digit with no letter after it yet may be the start of one,
            // so it waits for the next byte rather than being read as Alt+Shift+O and a stray "5".
            if (b[2] is >= (byte)'2' and <= (byte)'8')
            {
                if (b.Length < 4)
                {
                    return false;
                }

                if (TryParseSs3ModifiedArrow(b[2], b[3], emitKey))
                {
                    RemovePrefix(4);
                    return true;
                }
            }
        }

        if (b[1] < 0x20)
        {
            RemovePrefix(1);
            return true;
        }

        {
            var ch = (char)b[1];
            emitKey(KeyFromChar(ch, alt: true));
            RemovePrefix(2);
            return true;
        }
    }

    private static bool TryFindCsiTerminator(ReadOnlySpan<byte> b, out int endInclusive)
    {
        endInclusive = -1;
        for (var i = 2; i < b.Length; i++)
        {
            var c = b[i];
            if (c is >= 0x40 and <= 0x7e)
            {
                endInclusive = i;
                return true;
            }
        }

        return false;
    }

    /// <summary>Raised with true for a focus-in report and false for a focus-out report.</summary>
    public event Action<bool>? FocusReported;

    /// <summary>Raised with the width and height in pixels of one cell, when the terminal answers <c>ESC [ 16 t</c>.</summary>
    public event Action<int, int>? CellSizeReported;

    // "ESC [ 6 ; height ; width t" — the answer to the cell size query.
    private bool TryParseCellSizeReply(ReadOnlySpan<byte> csi)
    {
        if (csi.Length < 8 || csi[2] != (byte)'6' || csi[3] != (byte)';' || csi[^1] != (byte)'t')
        {
            return false;
        }

        var parts = Encoding.ASCII.GetString(csi[4..^1]).Split(';');
        if (parts.Length != 2 || !int.TryParse(parts[0], out var height) || !int.TryParse(parts[1], out var width))
        {
            return false;
        }

        CellSizeReported?.Invoke(width, height);
        return true;
    }

    /// <summary>True once the terminal has answered <see cref="KittyKeyboardProtocol.Query"/>.</summary>
    public bool KittyKeyboardSupported { get; private set; }

    // "ESC [ ? flags u" — the answer to the query.
    private bool TryParseKittyKeyboardReply(ReadOnlySpan<byte> csi)
    {
        if (csi.Length < 4 || csi[2] != (byte)'?' || csi[^1] != (byte)'u')
        {
            return false;
        }

        foreach (var c in csi[3..^1])
        {
            if (c is < (byte)'0' or > (byte)'9')
            {
                return false;
            }
        }

        KittyKeyboardSupported = true;
        return true;
    }

    // "ESC [ code[:shifted[:base]] ; modifiers[:event] [; text] u". Modifiers are 1 + a bit set:
    // shift 1, alt 2, ctrl 4 (super, hyper, meta, caps lock and num lock are not read).
    private static bool TryParseKittyKey(ReadOnlySpan<byte> csi, Action<KeyEvent> emitKey)
    {
        if (csi.Length < 4 || csi[^1] != (byte)'u' || csi[2] is (byte)'?' or (byte)'<' or (byte)'>' or (byte)'=')
        {
            return false;
        }

        var parts = Encoding.ASCII.GetString(csi[2..^1]).Split(';');
        var codeFields = parts[0].Split(':');
        if (!int.TryParse(codeFields[0], out var code) || code <= 0)
        {
            return false;
        }

        var modifiers = 0;
        var eventType = 1;
        if (parts.Length > 1 && parts[1].Length > 0)
        {
            var modFields = parts[1].Split(':');
            if (!int.TryParse(modFields[0], out modifiers) || modifiers < 1)
            {
                return false;
            }

            modifiers--;
            if (modFields.Length > 1 && !int.TryParse(modFields[1], out eventType))
            {
                return false;
            }
        }

        // A key release is not a key press. Nothing asks for them yet; swallow the sequence.
        if (eventType == 3)
        {
            return true;
        }

        var shift = (modifiers & 1) != 0;
        var alt = (modifiers & 2) != 0;
        var control = (modifiers & 4) != 0;

        switch (code)
        {
            case 27:
                emitKey(new KeyEvent('\x1b', ConsoleKey.Escape, shift, alt, control));
                return true;
            case 13:
                emitKey(new KeyEvent('\r', ConsoleKey.Enter, shift, alt, control));
                return true;
            case 9:
                emitKey(new KeyEvent('\t', ConsoleKey.Tab, shift, alt, control));
                return true;
            case 8:
            case 127:
                emitKey(new KeyEvent('\b', ConsoleKey.Backspace, shift, alt, control));
                return true;
            case 32:
                emitKey(new KeyEvent(' ', ConsoleKey.Spacebar, shift, alt, control));
                return true;
        }

        // Private-use codes are the function and keypad keys; they are not mapped yet.
        if (code is >= 0xD800 and <= 0xDFFF || code >= 0xE000)
        {
            return true;
        }

        var ch = char.ConvertFromUtf32(code)[0];
        var consoleKey = ConsoleKey.None;
        if (ch is >= 'a' and <= 'z')
        {
            consoleKey = ch - 'a' + ConsoleKey.A;
        }
        else if (ch is >= '0' and <= '9')
        {
            consoleKey = ch - '0' + ConsoleKey.D0;
        }

        // The legacy decoder reports Ctrl+letter as the control character, so the key matches either way.
        char keyChar;
        if (control && consoleKey is >= ConsoleKey.A and <= ConsoleKey.Z)
        {
            keyChar = (char)(ch & 0x1f);
        }
        else if (shift && codeFields.Length > 1 && int.TryParse(codeFields[1], out var shifted) && shifted is > 0 and < 0xD800)
        {
            keyChar = (char)shifted;
        }
        else
        {
            keyChar = shift && consoleKey is >= ConsoleKey.A and <= ConsoleKey.Z ? char.ToUpperInvariant(ch) : ch;
        }

        emitKey(new KeyEvent(keyChar, consoleKey, shift, alt, control));
        return true;
    }

    private static bool TryParseSgrMouse(ReadOnlySpan<byte> csi, Action<MouseEvent> emitMouse)
    {
        if (csi.Length < 9 || csi[2] != (byte)'<')
        {
            return false;
        }

        var fin = csi[^1];
        if (fin is not ((byte)'M' or (byte)'m'))
        {
            return false;
        }

        var body = csi[3..^1];
        if (!TrySplitTwoSemicolons(body, out var pbSpan, out var pxSpan, out var pySpan))
        {
            return false;
        }

        if (!TryParseInt(pbSpan, out var pb)
            || !TryParseInt(pxSpan, out var px)
            || !TryParseInt(pySpan, out var py))
        {
            return false;
        }

        var release = fin == (byte)'m';
        var pbNoMove = pb & ~32;
        var wheelDelta = 0;
        if (!release)
        {
            if (pbNoMove == 64)
            {
                wheelDelta = 1;
            }
            else if (pbNoMove == 65)
            {
                wheelDelta = -1;
            }
        }

        var btn = pb & 3;
        var motion = (pb & 32) != 0;
        var left = !release && wheelDelta == 0 && !motion && btn == 0;
        var right = !release && wheelDelta == 0 && !motion && btn == 2;
        var drag = !release && wheelDelta == 0 && motion && btn == 0;
        // Motion with no button held is reported as button code 3 (xterm any-motion tracking, ?1003).
        var hover = !release && wheelDelta == 0 && motion && btn == 3;
        var pos = new Position(px - 1, py - 1);
        emitMouse(new MouseEvent
        {
            Position = pos,
            IsDrag = drag,
            IsHover = hover,
            LeftClicked = left,
            RightClicked = right,
            IsRelease = release,
            WheelDelta = wheelDelta,
        });
        return true;
    }

    private static bool TrySplitTwoSemicolons(ReadOnlySpan<byte> body, out ReadOnlySpan<byte> a, out ReadOnlySpan<byte> b, out ReadOnlySpan<byte> c)
    {
        a = b = c = default;
        var s1 = body.IndexOf((byte)';');
        if (s1 < 0)
        {
            return false;
        }

        var rest = body[(s1 + 1)..];
        var s2 = rest.IndexOf((byte)';');
        if (s2 < 0)
        {
            return false;
        }

        a = body[..s1];
        b = rest[..s2];
        c = rest[(s2 + 1)..];
        return true;
    }

    private static bool TryParseInt(ReadOnlySpan<byte> asciiDigits, out int value)
    {
        value = 0;
        if (asciiDigits.IsEmpty)
        {
            return false;
        }

        foreach (var d in asciiDigits)
        {
            if (d is < (byte)'0' or > (byte)'9')
            {
                return false;
            }

            value = checked(value * 10 + (d - '0'));
        }

        return true;
    }

    private static bool TryParseCsiKey(ReadOnlySpan<byte> csi, Action<KeyEvent> emitKey)
    {
        if (csi.Length < 3)
        {
            return false;
        }

        var tail = csi[2..];
        var last = tail[^1];

        // Back-tab is "ESC [ Z"; some terminals send the "ESC [ 1 Z" form.
        if (last == (byte)'Z' && tail.Length <= 2)
        {
            emitKey(new KeyEvent('\t', ConsoleKey.Tab, shift: true, alt: false, control: false));
            return true;
        }

        if (tail.Length == 1)
        {
            return last switch
            {
                (byte)'A' => EmitArrow(ConsoleKey.UpArrow, emitKey),
                (byte)'B' => EmitArrow(ConsoleKey.DownArrow, emitKey),
                (byte)'C' => EmitArrow(ConsoleKey.RightArrow, emitKey),
                (byte)'D' => EmitArrow(ConsoleKey.LeftArrow, emitKey),
                (byte)'H' => EmitArrow(ConsoleKey.Home, emitKey),
                (byte)'F' => EmitArrow(ConsoleKey.End, emitKey),
                _ => false,
            };
        }

        if (tail.Length >= 2 && last == (byte)'~')
        {
            return TryParseCsiTildeFunctionKey(tail[..^1], emitKey);
        }

        if (tail.Length >= 2 && TryMapCsiLetterKeyId(last, out var modifiedKey))
        {
            return TryParseCsiModifiedLetterKey(tail[..^1], modifiedKey, emitKey);
        }

        return false;
    }

    private static bool TryMapCsiLetterKeyId(byte last, out ConsoleKey consoleKey)
    {
        switch (last)
        {
            case (byte)'A':
                consoleKey = ConsoleKey.UpArrow;
                return true;
            case (byte)'B':
                consoleKey = ConsoleKey.DownArrow;
                return true;
            case (byte)'C':
                consoleKey = ConsoleKey.RightArrow;
                return true;
            case (byte)'D':
                consoleKey = ConsoleKey.LeftArrow;
                return true;
            case (byte)'H':
                consoleKey = ConsoleKey.Home;
                return true;
            case (byte)'F':
                consoleKey = ConsoleKey.End;
                return true;
            case (byte)'P':
                consoleKey = ConsoleKey.F1;
                return true;
            case (byte)'Q':
                consoleKey = ConsoleKey.F2;
                return true;
            case (byte)'R':
                consoleKey = ConsoleKey.F3;
                return true;
            case (byte)'S':
                consoleKey = ConsoleKey.F4;
                return true;
            default:
                consoleKey = default;
                return false;
        }
    }

    /// <summary>
    /// CSI <c>1 ; Pm &lt;letter&gt;</c> — the modified counterpart of the unmodified single-letter
    /// arrow/Home/End/F1-F4 sequences. E.g. Ctrl+Right is <c>ESC [ 1 ; 5 C</c>, Ctrl+F1 is
    /// <c>ESC [ 1 ; 5 P</c>. The leading <c>Ps</c> (always "1" in practice) is discarded; only the
    /// modifier after the semicolon is used, same table as <see cref="ApplyXtermCsiModifier"/>.
    /// </summary>
    private static bool TryParseCsiModifiedLetterKey(ReadOnlySpan<byte> bodyBytes, ConsoleKey consoleKey, Action<KeyEvent> emitKey)
    {
        var body = Encoding.ASCII.GetString(bodyBytes);
        var semi = body.IndexOf(';');
        var modifierText = semi >= 0 ? body[(semi + 1)..] : body;
        if (!int.TryParse(modifierText, out var modifier))
        {
            return false;
        }

        ApplyXtermCsiModifier(modifier, out var shift, out var alt, out var control);
        emitKey(new KeyEvent(' ', consoleKey, shift, alt, control));
        return true;
    }

    private static bool TryParseSs3Arrow(byte final, Action<KeyEvent> emitKey) =>
        final switch
        {
            (byte)'A' => EmitArrow(ConsoleKey.UpArrow, emitKey),
            (byte)'B' => EmitArrow(ConsoleKey.DownArrow, emitKey),
            (byte)'C' => EmitArrow(ConsoleKey.RightArrow, emitKey),
            (byte)'D' => EmitArrow(ConsoleKey.LeftArrow, emitKey),
            (byte)'H' => EmitArrow(ConsoleKey.Home, emitKey),
            (byte)'F' => EmitArrow(ConsoleKey.End, emitKey),
            (byte)'P' => EmitArrow(ConsoleKey.F1, emitKey),
            (byte)'Q' => EmitArrow(ConsoleKey.F2, emitKey),
            (byte)'R' => EmitArrow(ConsoleKey.F3, emitKey),
            (byte)'S' => EmitArrow(ConsoleKey.F4, emitKey),
            _ => false,
        };

    /// <summary>
    /// rxvt's Ctrl+arrows: <c>ESC O a</c> Ctrl+Up, <c>b</c> Ctrl+Down, <c>c</c> Ctrl+Right, <c>d</c>
    /// Ctrl+Left. They are the lower-case letters the SS3 arrows would be, so the ordinary table
    /// does not match them.
    /// </summary>
    private static bool TryParseRxvtCtrlArrow(byte final, Action<KeyEvent> emitKey)
    {
        ConsoleKey key;
        switch (final)
        {
            case (byte)'a':
                key = ConsoleKey.UpArrow;
                break;
            case (byte)'b':
                key = ConsoleKey.DownArrow;
                break;
            case (byte)'c':
                key = ConsoleKey.RightArrow;
                break;
            case (byte)'d':
                key = ConsoleKey.LeftArrow;
                break;
            default:
                return false;
        }

        emitKey(new KeyEvent(' ', key, false, false, true));
        return true;
    }

    /// <summary>
    /// <c>ESC O &lt;Pm&gt; &lt;arrow&gt;</c>, the modifier written as a digit in the SS3 form: <c>ESC O 5 A</c>
    /// is Ctrl+Up. Only the arrows are read this way; the modifier uses the same table as the CSI form.
    /// </summary>
    private static bool TryParseSs3ModifiedArrow(byte modifierDigit, byte final, Action<KeyEvent> emitKey)
    {
        if (!TryMapCsiLetterKeyId(final, out var key) || key is not (ConsoleKey.UpArrow or ConsoleKey.DownArrow or ConsoleKey.LeftArrow or ConsoleKey.RightArrow))
        {
            return false;
        }

        ApplyXtermCsiModifier(modifierDigit - '0', out var shift, out var alt, out var control);
        emitKey(new KeyEvent(' ', key, shift, alt, control));
        return true;
    }

    private static bool EmitArrow(ConsoleKey key, Action<KeyEvent> emitKey)
    {
        emitKey(new KeyEvent(' ', key, false, false, false));
        return true;
    }

    /// <summary>
    /// CSI <c>Ps</c> ~ or <c>Ps ; Pm</c> ~ — <c>Pm</c> is the xterm-style modifier (1 = none, 5 = Control, …).
    /// Plain Page Up is <c>ESC [ 5 ~</c>; Ctrl+Page Up is commonly <c>ESC [ 5 ; 5 ~</c>.
    /// </summary>
    private static bool TryParseCsiTildeFunctionKey(ReadOnlySpan<byte> bodyBytes, Action<KeyEvent> emitKey)
    {
        var body = Encoding.ASCII.GetString(bodyBytes);
        string keyId;
        var modifier = 1;
        var semi = body.IndexOf(';');
        if (semi >= 0)
        {
            keyId = body[..semi];
            if (!int.TryParse(body[(semi + 1)..], out modifier))
            {
                return false;
            }
        }
        else
        {
            keyId = body;
        }

        if (!TryMapCsiTildeKeyId(keyId, out var consoleKey))
        {
            return false;
        }

        ApplyXtermCsiModifier(modifier, out var shift, out var alt, out var control);
        emitKey(new KeyEvent(' ', consoleKey, shift, alt, control));
        return true;
    }

    private static bool TryMapCsiTildeKeyId(string keyId, out ConsoleKey consoleKey)
    {
        switch (keyId)
        {
            case "1":
                consoleKey = ConsoleKey.Home;
                return true;
            case "2":
                consoleKey = ConsoleKey.Insert;
                return true;
            case "3":
                consoleKey = ConsoleKey.Delete;
                return true;
            case "4":
                consoleKey = ConsoleKey.End;
                return true;
            case "5":
                consoleKey = ConsoleKey.PageUp;
                return true;
            case "6":
                consoleKey = ConsoleKey.PageDown;
                return true;
            case "11":
                consoleKey = ConsoleKey.F1;
                return true;
            case "12":
                consoleKey = ConsoleKey.F2;
                return true;
            case "13":
                consoleKey = ConsoleKey.F3;
                return true;
            case "14":
                consoleKey = ConsoleKey.F4;
                return true;
            case "15":
                consoleKey = ConsoleKey.F5;
                return true;
            case "17":
                consoleKey = ConsoleKey.F6;
                return true;
            case "18":
                consoleKey = ConsoleKey.F7;
                return true;
            case "19":
                consoleKey = ConsoleKey.F8;
                return true;
            case "20":
                consoleKey = ConsoleKey.F9;
                return true;
            case "21":
                consoleKey = ConsoleKey.F10;
                return true;
            case "23":
                consoleKey = ConsoleKey.F11;
                return true;
            case "24":
                consoleKey = ConsoleKey.F12;
                return true;
            default:
                consoleKey = default;
                return false;
        }
    }

    /// <summary>
    /// xterm / VT modifier parameter for CSI function keys: 1 = none, 2 = Shift, 3 = Alt, 4 = Shift+Alt, 5 = Control, …
    /// </summary>
    private static void ApplyXtermCsiModifier(int modifier, out bool shift, out bool alt, out bool control)
    {
        shift = alt = control = false;
        switch (modifier)
        {
            case 0:
            case 1:
                break;
            case 2:
                shift = true;
                break;
            case 3:
                alt = true;
                break;
            case 4:
                shift = true;
                alt = true;
                break;
            case 5:
                control = true;
                break;
            case 6:
                shift = true;
                control = true;
                break;
            case 7:
                alt = true;
                control = true;
                break;
            case 8:
                shift = true;
                alt = true;
                control = true;
                break;
            default:
                break;
        }
    }

    private bool TryConsumeUtf8Text(ReadOnlySpan<byte> b, Action<KeyEvent> emitKey)
    {
        var first = b[0];
        if (first < 0x80)
        {
            if (first == 0x7f)
            {
                emitKey(new KeyEvent('\b', ConsoleKey.Backspace, false, false, false));
            }
            else if (first == '\r' || first == '\n')
            {
                emitKey(new KeyEvent('\r', ConsoleKey.Enter, false, false, false));
            }
            else if (first == '\t')
            {
                emitKey(new KeyEvent('\t', ConsoleKey.Tab, false, false, false));
            }
            else if (first < 0x20 && first != '\t' && first != '\r' && first != '\n')
            {
                var letter = (char)(first + 0x40);
                var ck = char.ToUpperInvariant(letter) is var u && u is >= 'A' and <= 'Z'
                    ? u - 'A' + ConsoleKey.A
                    : ConsoleKey.None;
                emitKey(new KeyEvent((char)first, ck, shift: false, alt: false, control: true));
            }
            else
            {
                emitKey(KeyFromChar((char)first, alt: false));
            }

            RemovePrefix(1);
            return true;
        }

        var len = Utf8CharLength(first);
        if (len == 0 || b.Length < len)
        {
            return false;
        }

        var status = Rune.DecodeFromUtf8(b[..len], out var rune, out _);
        if (status != OperationStatus.Done)
        {
            return false;
        }

        var s = rune.ToString();
        emitKey(new KeyEvent(s.Length > 0 ? s[0] : ' ', ConsoleKey.None, false, false, false));
        RemovePrefix(len);
        return true;
    }

    private static KeyEvent KeyFromChar(char ch, bool alt)
    {
        if (ch >= 'a' && ch <= 'z')
        {
            return new KeyEvent(ch, ch - 'a' + ConsoleKey.A, false, alt, false);
        }

        if (ch >= 'A' && ch <= 'Z')
        {
            return new KeyEvent(ch, ch - 'A' + ConsoleKey.A, true, alt, false);
        }

        if (ch is >= '0' and <= '9')
        {
            return new KeyEvent(ch, ch - '0' + ConsoleKey.D0, false, alt, false);
        }

        return new KeyEvent(ch, ConsoleKey.None, false, alt, false);
    }

    private static int Utf8CharLength(byte firstByte) =>
        firstByte switch
        {
            < 0x80 => 1,
            < 0xC0 => 0,
            < 0xE0 => 2,
            < 0xF0 => 3,
            < 0xF8 => 4,
            _ => 0,
        };

    private void RemovePrefix(int count)
    {
        if (count <= 0)
        {
            return;
        }

        if (count >= _buffer.Count)
        {
            _buffer.Clear();
            return;
        }

        _buffer.RemoveRange(0, count);
    }
}
