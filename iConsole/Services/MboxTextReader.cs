namespace iConsole.Services;

/// <summary>
/// Reads a mbox mailbox into lines: each message is split out at its "From " line, its quoting undone (mboxrd and
/// mboxo write a body line that starts with "From " as ">From "), and the message is shown by the same reader as an
/// .eml file, with a numbered rule above it.
/// </summary>
public static class MboxTextReader
{
    public static IReadOnlyList<string> Read(byte[] content)
    {
        List<string> lines = [];
        try
        {
            List<(int Start, int End)> messages = Split(content);
            if (messages.Count == 0)
            {
                lines.Add("[No messages were found in this file]");
                return lines;
            }

            for (int i = 0; i < messages.Count; i++)
            {
                if (i > 0)
                {
                    lines.Add(string.Empty);
                }

                lines.Add($"Message {i + 1} of {messages.Count}");
                lines.Add(new string('=', Math.Max(3, lines[^1].Length)));
                byte[] message = Unquote(content, messages[i].Start, messages[i].End);
                lines.AddRange(MimeMessageReader.Read(message));
            }
        }
        catch (Exception exception)
        {
            lines.Add($"[Could not read the mailbox: {exception.Message}]");
        }

        return lines;
    }

    /// <summary>The byte range of each message, from the byte after its "From " line to the next message's "From " line.</summary>
    internal static List<(int Start, int End)> Split(byte[] content)
    {
        // A UTF-8 byte order mark (some editors write one) is not part of the first line.
        int first = content.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }) ? 3 : 0;
        List<int> starts = [];
        for (int i = first; i < content.Length; i++)
        {
            bool lineStart = i == first || content[i - 1] == '\n';
            if (lineStart && content.AsSpan(i).StartsWith("From "u8))
            {
                starts.Add(i);
            }
        }

        List<(int Start, int End)> messages = [];
        for (int i = 0; i < starts.Count; i++)
        {
            int headerEnd = IndexOfNewline(content, starts[i]);
            int start = headerEnd < 0 ? content.Length : headerEnd + 1;
            int end = i + 1 < starts.Count ? starts[i + 1] : content.Length;
            if (start < end)
            {
                messages.Add((start, end));
            }
        }

        return messages;
    }

    /// <summary>Copies one message out of the mailbox, removing one level of ">" from each line that is quoted "From ".</summary>
    private static byte[] Unquote(byte[] content, int start, int end)
    {
        using MemoryStream output = new();
        int lineStart = start;
        while (lineStart < end)
        {
            int newline = Array.IndexOf(content, (byte)'\n', lineStart, end - lineStart);
            int lineEnd = newline < 0 ? end : newline + 1;
            int quotes = 0;
            while (lineStart + quotes < lineEnd && content[lineStart + quotes] == '>')
            {
                quotes++;
            }

            bool quotedFrom = quotes > 0 && content.AsSpan(lineStart + quotes, Math.Min(5, lineEnd - lineStart - quotes)).SequenceEqual("From "u8);
            int skip = quotedFrom ? 1 : 0;
            output.Write(content, lineStart + skip, lineEnd - lineStart - skip);
            lineStart = lineEnd;
        }

        return output.ToArray();
    }

    private static int IndexOfNewline(byte[] content, int from)
    {
        for (int i = from; i < content.Length; i++)
        {
            if (content[i] == '\n')
            {
                return i;
            }
        }

        return -1;
    }
}
