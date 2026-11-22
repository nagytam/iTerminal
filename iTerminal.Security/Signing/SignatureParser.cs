using iConsole.Localization;
using System.Text.RegularExpressions;

namespace iTerminal.Security.Signing;

/// <summary>
/// Reads what the signature tools print into verdicts: gpg's status lines, the three lines of the Authenticode
/// PowerShell script, and the code-signing certificate list. Pure text in, values out, so the parsing is tested without a process.
/// </summary>
public static partial class SignatureParser
{
    /// <summary>Reads gpg's <c>[GNUPG:]</c> status lines into a verdict. Public for tests: it is the part that can be wrong.</summary>
    public static SignatureVerdict ParseGpgStatus(string status, int exitCode, string fallback)
    {
        string? Find(string keyword) => status
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.TrimEnd())
            .FirstOrDefault(line => line.StartsWith("[GNUPG:] " + keyword, StringComparison.Ordinal));

        static string Signer(string line, string keyword)
        {
            // "[GNUPG:] GOODSIG <keyid> <user id...>"
            var rest = line[("[GNUPG:] " + keyword).Length..].Trim();
            var cut = rest.IndexOf(' ');
            return cut < 0 ? rest : $"{rest[(cut + 1)..]} ({rest[..cut]})";
        }

        if (Find("GOODSIG") is { } good)
        {
            return new(SignatureMethod.Gpg, SignatureStatus.Valid, Signer(good, "GOODSIG"), UiText.Get("Sign.Good", "Good signature."));
        }

        if (Find("EXPKEYSIG") is { } expiredKey)
        {
            return new(SignatureMethod.Gpg, SignatureStatus.Valid, Signer(expiredKey, "EXPKEYSIG"), UiText.Get("Sign.GoodExpired", "Good signature, but the key has expired."));
        }

        if (Find("REVKEYSIG") is { } revokedKey)
        {
            return new(SignatureMethod.Gpg, SignatureStatus.Invalid, Signer(revokedKey, "REVKEYSIG"), UiText.Get("Sign.Revoked", "The signature is good but the key was revoked — do not trust it."));
        }

        if (Find("BADSIG") is { } bad)
        {
            return new(SignatureMethod.Gpg, SignatureStatus.Invalid, Signer(bad, "BADSIG"), "BAD signature: the file was changed after it was signed, or this is not its signature.");
        }

        if (Find("NO_PUBKEY") is { } missing)
        {
            var key = missing["[GNUPG:] NO_PUBKEY".Length..].Trim();
            return new(SignatureMethod.Gpg, SignatureStatus.UnknownSigner, key, string.Format(UiText.Get("Sign.UnknownKey", "Signed with key {0}, which is not in your keyring (gpg --recv-keys {0})."), key));
        }

        if (Find("NODATA") is not null)
        {
            return new(SignatureMethod.Gpg, SignatureStatus.NotSigned, string.Empty, UiText.Get("Sign.NotOpenPgp", "That is not an OpenPGP signature."));
        }

        return new(SignatureMethod.Gpg, SignatureStatus.Unavailable, string.Empty, exitCode == 0 ? "gpg gave no verdict." : fallback);
    }

    /// <summary>Reads the three lines the script above prints.</summary>
    public static SignatureVerdict ParseAuthenticode(string text, string? failure = null)
    {
        var lines = text.Split(['\r', '\n'], StringSplitOptions.None).Select(line => line.Trim()).ToArray();
        if (failure is not null || lines.Length == 0 || lines[0].Length == 0)
        {
            return new(SignatureMethod.Authenticode, SignatureStatus.Unavailable, string.Empty, failure ?? UiText.Get("Sign.NoPowerShell", "No answer from PowerShell."));
        }

        var signer = lines.Length > 1 ? lines[1] : string.Empty;
        var message = lines.Length > 2 && lines[2].Length > 0 ? lines[2] : lines[0];
        var status = lines[0] switch
        {
            "Valid" => SignatureStatus.Valid,
            "NotSigned" or "NotSupportedFileFormat" => SignatureStatus.NotSigned,
            "HashMismatch" => SignatureStatus.Invalid,
            // A file with no signature certificate at all is not signed, whatever PowerShell calls it:
            // a .txt answers UnknownError ("the form specified for the subject is not one supported").
            "UnknownError" or "Incompatible" when signer.Length == 0 => SignatureStatus.NotSigned,
            "NotTrusted" or "UnknownError" or "Incompatible" => SignatureStatus.UnknownSigner,
            _ => SignatureStatus.Unavailable,
        };
        return new(SignatureMethod.Authenticode, status, signer, message);
    }

    public static IReadOnlyList<SigningCertificate> ParseCertificates(string text)
    {
        var certificates = new List<SigningCertificate>();
        foreach (var line in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split('|', 3);
            if (parts.Length == 3
                && ThumbprintPattern().IsMatch(parts[0])
                && DateTime.TryParse(parts[1], null, System.Globalization.DateTimeStyles.RoundtripKind, out var expires))
            {
                certificates.Add(new SigningCertificate(parts[0].ToUpperInvariant(), parts[2], expires));
            }
        }

        return certificates;
    }

    [GeneratedRegex("^[0-9A-Fa-f]{40}$")]
    private static partial Regex ThumbprintPattern();
}
