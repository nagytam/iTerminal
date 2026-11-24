using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using iConsole.Services;
using iConsole.Formats.Services;

namespace iConsole.Formats.Pdf;

/// <summary>Whether the bytes a signature covers still match it.</summary>
internal enum PdfSignatureIntegrity
{
    /// <summary>The signature is mathematically correct for the signed byte ranges.</summary>
    Valid,

    /// <summary>The signed bytes were changed, or the signature does not belong to this file.</summary>
    Invalid,

    /// <summary>A signature format this reader does not check (for example a raw RSA signature).</summary>
    Unsupported,

    /// <summary>The signature dictionary or the signature data cannot be read.</summary>
    Unreadable,
}

/// <summary>Whether the signer's certificate chains to a root this machine trusts. Revocation is never checked.</summary>
internal enum PdfSignerTrust
{
    Trusted,
    NotTrusted,
    Expired,

    /// <summary>No signer certificate could be read, so there is nothing to judge.</summary>
    Unknown,
}

/// <summary>One embedded signature. The text fields come from the file and are already stripped of control characters.</summary>
internal sealed record PdfSignature(
    int Number,
    string Name,
    string Signer,
    string? SigningTime,
    string SubFilter,
    PdfSignatureIntegrity Integrity,
    bool CoversWholeFile,
    PdfSignerTrust Trust);

internal sealed record PdfSignatureReport(IReadOnlyList<PdfSignature> Signatures, int NotShown, bool TooLarge);

/// <summary>
/// Finds the digital signatures embedded in a PDF and checks them. A signed PDF names the byte ranges the signature covers
/// (<c>/ByteRange [a b c d]</c>: everything except the hex <c>/Contents</c> string between them), so the check is a scan for those
/// and a CMS verification over the covered bytes; nothing is run, rendered or fetched.
///
/// <list type="bullet">
/// <item><b>Integrity and trust are separate verdicts.</b> A correct signature from an unknown signer is "unchanged, signer not
/// trusted", never "trusted". Revocation is not checked and no certificate is downloaded: a viewer must not reach the network
/// because of a file it was asked to show.</item>
/// <item><b>Limits.</b> A signature in a compressed object stream (PDF 1.5) is not found, and a <c>/Contents</c> written as a
/// literal string is reported as unreadable. At most <see cref="MaxSignatures"/> are shown and a file over
/// <see cref="MaxBytes"/> is not read.</item>
/// <item><b>The text comes from the file</b>, so it is stripped of control and format characters (escape sequences, bidi
/// overrides) and cut short before it reaches the screen.</item>
/// </list>
/// </summary>
internal static partial class PdfSignatureReader
{
    public const int MaxSignatures = 32;
    public const long MaxBytes = 64L * 1024 * 1024;
    private const int MaxTextLength = 200;

    /// <summary>The most hex digits of a /Contents string that are decoded (256 KB of signature).</summary>
    private const long MaxContentsHex = 512 * 1024;

    private static readonly byte[] ByteRangeKey = "/ByteRange"u8.ToArray();

    /// <summary>True when the bytes start like a PDF (the header may follow up to 1 KB of junk, as readers allow).</summary>
    public static bool LooksLikePdf(ReadOnlySpan<byte> content) =>
        content.Length > 5 && content[..Math.Min(content.Length, 1024)].IndexOf("%PDF-"u8) >= 0;

    /// <param name="pdf">The whole file.</param>
    /// <param name="configureChain">Adjusts the chain policy after the defaults (a test supplies its own root and clock here).</param>
    public static PdfSignatureReport Read(byte[] pdf, Action<X509ChainPolicy>? configureChain = null, CancellationToken cancellationToken = default)
    {
        if (pdf.Length > MaxBytes)
        {
            return new PdfSignatureReport([], 0, TooLarge: true);
        }

        List<PdfSignature> found = [];
        int notShown = 0;
        int searchFrom = 0;
        int number = 0;
        while (searchFrom < pdf.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int at = pdf.AsSpan(searchFrom).IndexOf(ByteRangeKey);
            if (at < 0)
            {
                break;
            }

            int keyStart = searchFrom + at;
            searchFrom = keyStart + ByteRangeKey.Length;
            if (!TryParseRange(pdf, searchFrom, out long[]? range) || range is null)
            {
                continue;
            }

            number++;
            if (found.Count >= MaxSignatures)
            {
                notShown++;
                continue;
            }

            found.Add(Check(pdf, keyStart, range, number, configureChain, cancellationToken));
        }

        return new PdfSignatureReport(found, notShown, TooLarge: false);
    }

    private static bool TryParseRange(byte[] pdf, int from, out long[]? range)
    {
        range = null;
        int i = from;
        while (i < pdf.Length && IsWhite(pdf[i]))
        {
            i++;
        }

        if (i >= pdf.Length || pdf[i] != '[')
        {
            return false;
        }

        int close = Array.IndexOf(pdf, (byte)']', i, Math.Min(pdf.Length - i, 128));
        if (close < 0)
        {
            return false;
        }

        string[] parts = Encoding.ASCII.GetString(pdf, i + 1, close - i - 1).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4)
        {
            return false;
        }

        long[] numbers = new long[4];
        for (int n = 0; n < 4; n++)
        {
            // Twelve digits is far beyond any file this reader opens, and keeps the arithmetic below from overflowing.
            if (parts[n].Length > 12 || !long.TryParse(parts[n], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[n]))
            {
                return false;
            }
        }

        range = numbers;
        return true;
    }

    private static PdfSignature Check(
        byte[] pdf, int keyStart, long[] range, int number, Action<X509ChainPolicy>? configureChain, CancellationToken cancellationToken)
    {
        long a = range[0], b = range[1], c = range[2], d = range[3];
        string subFilter = Clean(Capture(Window(pdf, keyStart, c), SubFilterPattern()));
        string name = Clean(DecodePdfString(Capture(Window(pdf, keyStart, c), NamePattern())));

        PdfSignature Unreadable() => new(number, name, string.Empty, null, subFilter, PdfSignatureIntegrity.Unreadable, false, PdfSignerTrust.Unknown);

        // The ranges must start the file, stay inside it, and leave exactly the "<hex>" of /Contents between them.
        if (a != 0 || a + b > c || c + d > pdf.Length || c - (a + b) < 2 || pdf[a + b] != '<' || pdf[c - 1] != '>')
        {
            return Unreadable();
        }

        // A CMS signature with its certificates is a few KB; a gap that is far larger is not a signature to decode.
        if (c - (a + b) - 2 > MaxContentsHex)
        {
            return Unreadable();
        }

        cancellationToken.ThrowIfCancellationRequested();
        byte[] cms;
        try
        {
            int start = (int)(a + b) + 1;
            int length = (int)(c - a - b) - 2;
            char[] digits = new char[length];
            int count = 0;
            for (int i = 0; i < length; i++)
            {
                byte value = pdf[start + i];
                if (!IsWhite(value))
                {
                    digits[count++] = (char)value;
                }
            }

            cms = TrimDer(Convert.FromHexString(digits.AsSpan(0, count)));
        }
        catch (FormatException)
        {
            return Unreadable();
        }

        if (cms.Length == 0)
        {
            return Unreadable();
        }

        byte[] signed = new byte[b + d];
        Buffer.BlockCopy(pdf, (int)a, signed, 0, (int)b);
        Buffer.BlockCopy(pdf, (int)c, signed, (int)b, (int)d);

        bool whole = c + d >= TrimmedLength(pdf);
        bool sha1Flavour = subFilter.Equals("adbe.pkcs7.sha1", StringComparison.Ordinal);
        if (!(sha1Flavour || subFilter.Equals("adbe.pkcs7.detached", StringComparison.Ordinal)
            || subFilter.Equals("ETSI.CAdES.detached", StringComparison.Ordinal)))
        {
            return new PdfSignature(number, name, string.Empty, null, subFilter, PdfSignatureIntegrity.Unsupported, whole, PdfSignerTrust.Unknown);
        }

        try
        {
            SignedCms cmsMessage = sha1Flavour ? new SignedCms() : new SignedCms(new ContentInfo(signed), detached: true);
            cmsMessage.Decode(cms);
            if (cmsMessage.SignerInfos.Count == 0)
            {
                return Unreadable();
            }

            SignerInfo signer = cmsMessage.SignerInfos[0];
            X509Certificate2? certificate = signer.Certificate;
            string subject = certificate is null ? string.Empty : Clean(certificate.Subject);
            string? time = SigningTime(signer);

            bool intact;
            try
            {
                // adbe.pkcs7.sha1 carries the SHA-1 of the signed bytes as the content the CMS signs.
                #pragma warning disable S4790 // The digest is the one the file format or service names; it is used to read that format, not to make new trust decisions.
                intact = !sha1Flavour || cmsMessage.ContentInfo.Content.AsSpan().SequenceEqual(SHA1.HashData(signed));
                #pragma warning restore S4790
                if (intact)
                {
                    cmsMessage.CheckSignature(verifySignatureOnly: true);
                }
            }
            catch (CryptographicException)
            {
                intact = false;
            }

            PdfSignerTrust trust = !intact || certificate is null
                ? PdfSignerTrust.Unknown
                : TrustOf(certificate, cmsMessage.Certificates, configureChain);
            return new PdfSignature(
                number,
                name,
                subject,
                time,
                subFilter,
                intact ? PdfSignatureIntegrity.Valid : PdfSignatureIntegrity.Invalid,
                whole,
                trust);
        }
        catch (CryptographicException)
        {
            return Unreadable();
        }
    }

    private static string? SigningTime(SignerInfo signer)
    {
        foreach (CryptographicAttributeObject attribute in signer.SignedAttributes)
        {
            foreach (AsnEncodedData value in attribute.Values)
            {
                if (value is Pkcs9SigningTime time)
                {
                    return time.SigningTime.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);
                }
            }
        }

        return null;
    }

    private static PdfSignerTrust TrustOf(X509Certificate2 certificate, X509Certificate2Collection extra, Action<X509ChainPolicy>? configure)
    {
        using X509Chain chain = new();
        X509ChainPolicy policy = chain.ChainPolicy;
        // A viewer must not reach the network because of a file it shows: no revocation lookups, no downloaded issuers.
        policy.RevocationMode = X509RevocationMode.NoCheck;
        policy.DisableCertificateDownloads = true;
        policy.UrlRetrievalTimeout = TimeSpan.FromSeconds(2);
        policy.ExtraStore.AddRange(extra);
        configure?.Invoke(policy);

        if (chain.Build(certificate))
        {
            return PdfSignerTrust.Trusted;
        }

        return chain.ChainStatus.Any(status => status.Status.HasFlag(X509ChainStatusFlags.NotTimeValid))
            ? PdfSignerTrust.Expired
            : PdfSignerTrust.NotTrusted;
    }

    /// <summary>The bytes around a signature dictionary where its plain-text entries sit: just before /ByteRange and after /Contents.</summary>
    private static string Window(byte[] pdf, int keyStart, long c)
    {
        // Both sides stop at the edge of the indirect object, so the entries of a neighbouring signature are never read.
        int before = Math.Max(0, keyStart - 2048);
        string head = Encoding.Latin1.GetString(pdf, before, Math.Min(pdf.Length - before, keyStart - before + 256));
        int objectStart = head[..Math.Min(head.Length, keyStart - before)].LastIndexOf("obj", StringComparison.Ordinal);
        if (objectStart >= 0)
        {
            head = head[(objectStart + 3)..];
        }

        int afterStart = (int)Math.Min(pdf.Length, c);
        string tail = Encoding.Latin1.GetString(pdf, afterStart, Math.Min(pdf.Length - afterStart, 1024));
        int objectEnd = tail.IndexOf("endobj", StringComparison.Ordinal);
        if (objectEnd >= 0)
        {
            tail = tail[..objectEnd];
        }

        return head + "\n" + tail;
    }

    /// <summary>A PDF text string read as Latin-1 is UTF-16BE when it starts with the byte order mark FE FF.</summary>
    internal static string DecodePdfString(string latin1)
    {
        if (latin1.Length < 2 || latin1[0] != 'þ' || latin1[1] != 'ÿ')
        {
            return latin1;
        }

        byte[] bytes = Encoding.Latin1.GetBytes(latin1[2..]);
        return Encoding.BigEndianUnicode.GetString(bytes, 0, bytes.Length - (bytes.Length % 2));
    }

    private static string Capture(string window, Regex pattern)
    {
        Match match = pattern.Match(window);
        return match.Success ? match.Groups[1].Value : string.Empty;
    }

    /// <summary>The file's length without the trailing whitespace and padding some writers leave after %%EOF.</summary>
    private static long TrimmedLength(byte[] pdf)
    {
        int end = pdf.Length;
        while (end > 0 && (IsWhite(pdf[end - 1]) || pdf[end - 1] == 0))
        {
            end--;
        }

        return end;
    }

    /// <summary>A signature field is padded with zeros after the DER structure; the decoder wants the structure alone.</summary>
    private static byte[] TrimDer(byte[] bytes)
    {
        if (bytes.Length < 2 || bytes[0] != 0x30)
        {
            return bytes;
        }

        int first = bytes[1];
        long length;
        int header = 2;
        if (first < 0x80)
        {
            length = first;
        }
        else
        {
            int count = first & 0x7F;
            if (count is 0 or > 4 || bytes.Length < 2 + count)
            {
                return bytes;
            }

            length = 0;
            for (int i = 0; i < count; i++)
            {
                length = (length << 8) | bytes[2 + i];
            }

            header = 2 + count;
        }

        long total = header + length;
        return total <= bytes.Length ? bytes[..(int)total] : bytes;
    }

    private static bool IsWhite(byte value) => value is (byte)' ' or (byte)'\r' or (byte)'\n' or (byte)'\t' or (byte)'\f';

    /// <summary>Text from the file for the screen: no control or format characters (escape sequences, bidi overrides), and short.</summary>
    internal static string Clean(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        StringBuilder builder = new(Math.Min(text.Length, MaxTextLength + 1));
        foreach (char character in text)
        {
            if (char.IsControl(character) || char.GetUnicodeCategory(character) == UnicodeCategory.Format)
            {
                continue;
            }

            if (builder.Length == MaxTextLength)
            {
                builder.Append('…');
                break;
            }

            builder.Append(character);
        }

        return builder.ToString().Trim();
    }

    /// <summary>The section F3 shows: nothing at all for a file without signatures.</summary>
    public static IReadOnlyList<string> Describe(PdfSignatureReport report)
    {
        if (report.TooLarge)
        {
            return
            [
                ViewerText.Heading("PdfSignatures", "Digital signatures"),
                "  " + ViewerText.Format("PdfTooLarge", "Signatures were not checked: the file is larger than {0} MB.", MaxBytes / (1024 * 1024)),
                string.Empty,
            ];
        }

        if (report.Signatures.Count == 0)
        {
            return [];
        }

        List<string> lines = [ViewerText.Heading("PdfSignatures", "Digital signatures")];
        foreach (PdfSignature signature in report.Signatures)
        {
            string title = ViewerText.Format("PdfSignatureNumber", "Signature {0}", signature.Number);
            lines.Add(signature.Name.Length > 0 ? $"  {title} — {signature.Name}" : $"  {title}");
            Field(lines, ViewerText.Get("PdfSigner", "Signer"), signature.Signer);
            Field(lines, ViewerText.Get("PdfSigned", "Signed"), signature.SigningTime);
            Field(lines, ViewerText.Get("PdfFormat", "Format"), signature.SubFilter);
            Field(lines, ViewerText.Get("PdfIntegrity", "Integrity"), signature.Integrity switch
            {
                PdfSignatureIntegrity.Valid => ViewerText.Get("PdfIntegrityOk", "The signed bytes are unchanged."),
                PdfSignatureIntegrity.Invalid => ViewerText.Get("PdfIntegrityBad", "CHANGED: the file does not match this signature."),
                PdfSignatureIntegrity.Unsupported => ViewerText.Get("PdfIntegrityUnsupported", "Not checked: this signature format is not supported."),
                _ => ViewerText.Get("PdfIntegrityUnreadable", "The signature could not be read."),
            });
            if (signature.Integrity is PdfSignatureIntegrity.Valid or PdfSignatureIntegrity.Invalid)
            {
                Field(lines, ViewerText.Get("PdfCoverage", "Coverage"), signature.CoversWholeFile
                    ? ViewerText.Get("PdfCoverageWhole", "The whole file.")
                    : ViewerText.Get("PdfCoverageLater", "Changes were added after signing."));
            }

            if (signature.Trust != PdfSignerTrust.Unknown)
            {
                Field(lines, ViewerText.Get("PdfTrust", "Trust"), signature.Trust switch
                {
                    PdfSignerTrust.Trusted => ViewerText.Get("PdfTrustOk", "Trusted: the certificate chains to a root this machine trusts."),
                    PdfSignerTrust.Expired => ViewerText.Get("PdfTrustExpired", "Not trusted: the certificate has expired."),
                    _ => ViewerText.Get("PdfTrustNo", "Not trusted: the certificate does not chain to a root this machine trusts."),
                });
            }

            lines.Add(string.Empty);
        }

        if (report.NotShown > 0)
        {
            lines.Add("  " + ViewerText.Format("PdfMore", "{0} more signatures are not shown.", report.NotShown));
        }

        lines.Add("  " + ViewerText.Get("PdfNoRevocation", "Revocation was not checked."));
        lines.Add(string.Empty);
        return lines;
    }

    private static void Field(List<string> lines, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            lines.Add($"    {label,-14}{value}");
        }
    }

    [GeneratedRegex(@"/SubFilter\s*/([^\s/<>\[\]()]+)")]
    private static partial Regex SubFilterPattern();

    [GeneratedRegex(@"/Name\s*\(([^)]*)\)")]
    private static partial Regex NamePattern();
}
