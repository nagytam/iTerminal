using System.Globalization;
using System.Text;
using OpenMcdf;

namespace iConsole.Services;

/// <summary>
/// Reads an Outlook message (.msg) into the same lines an .eml gets: sender, recipients, date and subject, then the
/// body. A message is an OLE2 compound file whose properties are streams named for their MAPI tag and type
/// (<c>__substg1.0_0037001F</c> is the subject as Unicode). The plain body is shown; without one, the HTML body is
/// shown as text. Attachments are listed by name.
/// </summary>
public static class OutlookMessageReader
{
    private const ushort Subject = 0x0037;
    private const ushort PlainBody = 0x1000;
    private const ushort HtmlBody = 0x1013;
    private const ushort SenderName = 0x0C1A;
    private const ushort SenderAddress = 0x0C1F;
    private const ushort DisplayTo = 0x0E04;
    private const ushort DisplayCc = 0x0E03;
    private const ushort ClientSubmitTime = 0x0039;
    private const ushort AttachLongName = 0x3707;
    private const ushort AttachDisplayName = 0x3001;

    static OutlookMessageReader()
    {
        // The ANSI properties are in the message's code page, which is not in the base framework.
        Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
    }

    public static IReadOnlyList<string> Read(byte[] content)
    {
        try
        {
            using MemoryStream file = new(content);
            using RootStorage root = RootStorage.Open(file, StorageModeFlags.LeaveOpen);

            string? sender = Property(root, SenderName) ?? Property(root, SenderAddress);
            if (sender is not null && Property(root, SenderAddress) is { } address && address != sender)
            {
                sender = $"{sender} <{address}>";
            }

            string? plain = Property(root, PlainBody);
            string? html = plain is null ? HtmlProperty(root) : null;

            return MessageLines.Format(
                from: sender,
                to: Property(root, DisplayTo),
                cc: Property(root, DisplayCc),
                date: SubmitTime(root),
                subject: Property(root, Subject),
                plainText: plain,
                html: html,
                attachments: Attachments(root));
        }
        catch (Exception exception)
        {
            return [$"[Could not read the message: {exception.Message}]"];
        }
    }

    /// <summary>A text property: Unicode when the file has it, otherwise the ANSI one.</summary>
    private static string? Property(Storage storage, ushort tag)
    {
        if (ReadStream(storage, $"__substg1.0_{tag:X4}001F") is { } unicode)
        {
            return Encoding.Unicode.GetString(unicode).TrimEnd('\0');
        }

        if (ReadStream(storage, $"__substg1.0_{tag:X4}001E") is { } ansi)
        {
            return Encoding.GetEncoding(1252).GetString(ansi).TrimEnd('\0');
        }

        return null;
    }

    private static string? HtmlProperty(Storage storage) =>
        ReadStream(storage, $"__substg1.0_{HtmlBody:X4}0102") is { } html
            ? string.Join("\n", HtmlTextExtractor.Lines(html))
            : null;

    /// <summary>The submit time, a Windows file time, shown in UTC.</summary>
    private static string? SubmitTime(Storage storage)
    {
        if (ReadStream(storage, $"__substg1.0_{ClientSubmitTime:X4}0040") is not { Length: >= 8 } time)
        {
            return null;
        }

        DateTime utc = DateTime.FromFileTimeUtc(BitConverter.ToInt64(time, 0));
        return utc.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);
    }

    private static List<string> Attachments(Storage root)
    {
        List<string> names = [];
        foreach (EntryInfo entry in root.EnumerateEntries().Where(e => e.Name.StartsWith("__attach_version1.0_", StringComparison.Ordinal)))
        {
            if (!root.TryOpenStorage(entry.Name, out Storage? attachment) || attachment is null)
            {
                continue;
            }

            string? name = Property(attachment, AttachLongName) ?? Property(attachment, AttachDisplayName);
            names.Add(name ?? "(unnamed attachment)");
        }

        return names;
    }

    private static byte[]? ReadStream(Storage storage, string name)
    {
        if (!storage.TryOpenStream(name, out CfbStream? stream) || stream is null)
        {
            return null;
        }

        using MemoryStream copy = new();
        stream.CopyTo(copy);
        return copy.ToArray();
    }
}
