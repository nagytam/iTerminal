using System.Globalization;
using System.Text;
using iConsole.Model;

namespace iConsole.Helper;

/// <summary>
/// The code pages the viewer and the editor can switch between (F8 / Shift+F8), how a file's bytes are
/// decoded when nobody has chosen one, and how already-decoded text is re-read under another one.
/// </summary>
public static class TextEncodings
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Code page numbers offered by the switch, UTF-8 first. Unknown ones on a platform are skipped.</summary>
    private static readonly int[] CodePages =
    [
        65001, // UTF-8
        1200,  // UTF-16 LE
        1201,  // UTF-16 BE
        1252,  // Western European (Windows)
        28591, // ISO-8859-1
        28605, // ISO-8859-15
        1250,  // Central European (Windows)
        28592, // ISO-8859-2
        852,   // Central European (DOS)
        1251,  // Cyrillic (Windows)
        20866, // KOI8-R
        866,   // Cyrillic (DOS)
        1253,  // Greek
        1254,  // Turkish
        1257,  // Baltic
        437,   // OEM United States
        932,   // Shift-JIS
        936,   // GBK
        949,   // Korean
        950,   // Big5
    ];

    static TextEncodings()
    {
        // The legacy code pages are not part of the default set on .NET Core.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>The encodings the switch cycles through, in order.</summary>
    public static IReadOnlyList<Encoding> Available { get; } = Build();

    private static List<Encoding> Build()
    {
        var list = new List<Encoding>();
        foreach (int page in CodePages)
        {
            try
            {
                list.Add(page == 65001 ? Utf8NoBom : Encoding.GetEncoding(page));
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
            {
                // Not available on this platform.
            }
        }

        return list;
    }

    /// <summary>What a person reads in the status bar: "UTF-8", "windows-1250"…</summary>
    public static string DisplayName(Encoding encoding) => encoding.WebName.ToUpperInvariant();

    /// <summary>The encoding after <paramref name="current"/> in the cycle (or before it, going backwards).</summary>
    public static Encoding Next(Encoding current, bool backwards = false)
    {
        int index = IndexOf(current);
        int count = Available.Count;
        int next = index < 0
            ? (backwards ? count - 1 : 0)
            : (index + (backwards ? count - 1 : 1)) % count;
        return Available[next];
    }

    private static int IndexOf(Encoding encoding)
    {
        for (int i = 0; i < Available.Count; i++)
        {
            if (Available[i].CodePage == encoding.CodePage)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Decodes a file whose encoding nobody chose: a byte order mark decides, else UTF-8 when the bytes
    /// are valid UTF-8, else the system's ANSI code page (Latin-1 where there is none). Never lossy —
    /// there is no UTF-8 with replacement characters, which no later switch could undo.
    /// </summary>
    public static string Decode(byte[] bytes, out Encoding encoding)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            encoding = Encoding.UTF8;
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            encoding = Encoding.Unicode;
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            encoding = Encoding.BigEndianUnicode;
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        }

        try
        {
            string text = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
            encoding = Utf8NoBom;
            return text;
        }
        catch (DecoderFallbackException)
        {
            encoding = AnsiEncoding();
            return encoding.GetString(bytes);
        }
    }

    private static Encoding AnsiEncoding()
    {
        try
        {
            int page = CultureInfo.CurrentCulture.TextInfo.ANSICodePage;
            if (page > 0)
            {
                return Encoding.GetEncoding(page);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            // Fall through to Latin-1.
        }

        return Encoding.Latin1;
    }

    /// <summary>
    /// Reads the text the model holds as if it had been decoded with <paramref name="encoding"/> in the
    /// first place: the lines are turned back into the bytes the current encoding made of them and
    /// decoded again. This is exact for what <see cref="Decode"/> produces (valid UTF-8 and the
    /// single-byte code pages round-trip), and it keeps an editor's unsaved changes, which a reload
    /// from disk would throw away.
    /// </summary>
    public static void Reinterpret(ITextModel model, Encoding encoding)
    {
        if (model.Encoding.CodePage != encoding.CodePage)
        {
            string text = string.Join("\n", model.Lines);
            byte[] bytes = model.Encoding.GetBytes(text);
            string decoded = encoding.GetString(bytes);
            string[] lines = decoded.ReplaceLineEndings("\n").Split('\n');

            model.Lines.Clear();
            foreach (string line in lines)
            {
                model.Lines.Add(line);
            }
        }

        model.Encoding = encoding;
        model.TriggerPropertyChanged();
    }
}
