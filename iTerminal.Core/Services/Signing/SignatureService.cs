using iTerminal.Core.Services.Processes;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace iTerminal.Core.Services.Signing;

/// <summary>The three ways a file can be signed or checked here.</summary>
public enum SignatureMethod
{
    /// <summary>A detached OpenPGP signature (<c>name.asc</c>), made and checked by the <c>gpg</c> program.</summary>
    Gpg,

    /// <summary>The signature embedded in a Windows executable, script or installer. Windows only.</summary>
    Authenticode,

    /// <summary>A detached signature (<c>name.sig</c>) made with a PEM RSA or ECDSA key, in-process: no tool needed.</summary>
    Pem,
}

public enum SignatureStatus
{
    /// <summary>The signature is there and the file matches it.</summary>
    Valid,

    /// <summary>The signature is there and the file does NOT match it — changed, or signed by someone else.</summary>
    Invalid,

    /// <summary>The file is not signed (by this method).</summary>
    NotSigned,

    /// <summary>The signature matches but the signer is not one this machine trusts or knows (no public key, untrusted root).</summary>
    UnknownSigner,

    /// <summary>The check could not be made: the tool is missing, or a key could not be read.</summary>
    Unavailable,
}

/// <summary>What one method found: the verdict, who signed, and a sentence for the screen.</summary>
public sealed record SignatureVerdict(SignatureMethod Method, SignatureStatus Status, string Signer, string Message);

/// <summary>What signing did. <see cref="SignaturePath"/> is the detached file written, or null for an embedded signature.</summary>
public sealed record SignResult(bool Success, string? SignaturePath, string Message);

/// <summary>A code-signing certificate the Windows certificate store holds.</summary>
public sealed record SigningCertificate(string Thumbprint, string Subject, DateTime Expires);

/// <summary>What is available on this machine, so the dialog offers only what can work.</summary>
public sealed record SigningCapabilities(bool Gpg, bool Authenticode);

/// <summary>
/// Signs a file and checks signatures. Async by contract: gpg and PowerShell are processes, and a
/// large file is read end to end.
/// </summary>
public interface ISignatureService
{
    Task<SigningCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Every signature that can be found for <paramref name="filePath"/>: the embedded one (Windows),
    /// and a detached <c>.asc</c>/<c>.sig</c>/<c>.gpg</c>/<c>.sign</c> beside it — or <paramref name="signaturePath"/> when
    /// given. <paramref name="publicKeyPath"/> is the PEM public key a <c>.sig</c> is checked against.
    /// </summary>
    Task<IReadOnlyList<SignatureVerdict>> VerifyAsync(
        string filePath,
        string? signaturePath = null,
        string? publicKeyPath = null,
        CancellationToken cancellationToken = default);

    /// <param name="keyIdentifier">gpg: the key id or e-mail (empty for the default key). Authenticode: a certificate thumbprint.</param>
    /// <param name="keyPath">Pem: the private key file.</param>
    Task<SignResult> SignAsync(
        string filePath,
        SignatureMethod method,
        string? keyIdentifier = null,
        string? keyPath = null,
        CancellationToken cancellationToken = default);

    /// <summary>The code-signing certificates in the current user's store; empty off Windows.</summary>
    Task<IReadOnlyList<SigningCertificate>> GetCertificatesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// <see cref="ISignatureService"/> over <c>gpg</c> and PowerShell (through
/// <see cref="IExternalProcessRunner"/>, so tests fake the tools) and the BCL's RSA and ECDSA.
///
/// <list type="bullet">
/// <item><b>Nothing typed is spliced into code.</b> gpg gets an argument list; the PowerShell
/// scripts get a path as a single-quoted literal (quotes doubled) and a thumbprint only after a
/// strict hex match.</item>
/// <item><b>gpg is read through <c>--status-fd</c></b>, whose lines are a stable interface; its prose
/// is translated and changes between versions.</item>
/// <item><b>A PEM signature is the base64 of a signature over the file's SHA-256</b>: PKCS#1 v1.5 for
/// RSA and the fixed-size IEEE P1363 form for ECDSA, told apart by the key. It is this program's
/// own small format — not CMS, not OpenSSL's DER — because the point is a check that needs no tool;
/// the verdict names the algorithm so a file signed some other way is not misreported.</item>
/// <item><b>Encrypted private keys are not read.</b> A passphrase would have to be typed into a
/// dialog; use gpg, whose agent asks for it properly.</item>
/// </list>
/// </summary>
public partial class SignatureService(IExternalProcessRunner runner) : ISignatureService
{
    private static readonly string[] DetachedExtensions = [".asc", ".sig", ".gpg", ".sign"];
    private const string PemPrefix = "iTerminal-signature-v1";

    public async Task<SigningCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default)
    {
        var gpg = (await runner.RunAsync("gpg", ["--version"], null, cancellationToken)).Success;
        return new SigningCapabilities(gpg, OperatingSystem.IsWindows());
    }

    public async Task<IReadOnlyList<SignatureVerdict>> VerifyAsync(
        string filePath,
        string? signaturePath = null,
        string? publicKeyPath = null,
        CancellationToken cancellationToken = default)
    {
        var verdicts = new List<SignatureVerdict>();
        if (!File.Exists(filePath))
        {
            return [new(SignatureMethod.Pem, SignatureStatus.Unavailable, string.Empty, "The file is not there.")];
        }

        if (OperatingSystem.IsWindows() && signaturePath is null)
        {
            var embedded = await VerifyAuthenticodeAsync(filePath, cancellationToken);
            // A file with no embedded signature is the common case: say so only when nothing else is found either.
            if (embedded.Status != SignatureStatus.NotSigned)
            {
                verdicts.Add(embedded);
            }
        }

        var detached = signaturePath is not null ? [signaturePath] : FindDetached(filePath);
        foreach (var signature in detached)
        {
            cancellationToken.ThrowIfCancellationRequested();
            verdicts.Add(await VerifyDetachedAsync(filePath, signature, publicKeyPath, cancellationToken));
        }

        if (verdicts.Count == 0)
        {
            verdicts.Add(new(
                OperatingSystem.IsWindows() ? SignatureMethod.Authenticode : SignatureMethod.Gpg,
                SignatureStatus.NotSigned,
                string.Empty,
                "No signature found: nothing embedded in the file and no .asc, .sig, .gpg or .sign beside it."));
        }

        return verdicts;
    }

    internal static IReadOnlyList<string> FindDetached(string filePath) =>
        [.. DetachedExtensions.Select(extension => filePath + extension).Where(File.Exists)];

    private async Task<SignatureVerdict> VerifyDetachedAsync(string filePath, string signaturePath, string? publicKeyPath, CancellationToken cancellationToken)
    {
        string head;
        try
        {
            using var stream = File.OpenRead(signaturePath);
            var buffer = new byte[Math.Min(256, stream.Length)];
            stream.ReadExactly(buffer);
            head = Encoding.Latin1.GetString(buffer);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new(SignatureMethod.Gpg, SignatureStatus.Unavailable, string.Empty, $"{Path.GetFileName(signaturePath)} could not be read: {exception.Message}");
        }

        return head.StartsWith(PemPrefix, StringComparison.Ordinal)
            ? await VerifyPemAsync(filePath, signaturePath, publicKeyPath, cancellationToken)
            : await VerifyGpgAsync(filePath, signaturePath, cancellationToken);
    }

    // ---- gpg ----

    private async Task<SignatureVerdict> VerifyGpgAsync(string filePath, string signaturePath, CancellationToken cancellationToken)
    {
        var output = await runner.RunAsync("gpg", ["--status-fd", "1", "--batch", "--no-tty", "--verify", signaturePath, filePath], null, cancellationToken);
        if (output.ExitCode == -1)
        {
            return new(SignatureMethod.Gpg, SignatureStatus.Unavailable, string.Empty, "gpg is not installed (or not on the PATH), so the signature cannot be checked.");
        }

        return ParseGpgStatus(output.StandardOutput, output.ExitCode, output.FirstErrorLine);
    }

    /// <summary>Reads gpg's <c>[GNUPG:]</c> status lines into a verdict. Public for tests: it is the part that can be wrong.</summary>
    internal static SignatureVerdict ParseGpgStatus(string status, int exitCode, string fallback)
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
            return new(SignatureMethod.Gpg, SignatureStatus.Valid, Signer(good, "GOODSIG"), "Good signature.");
        }

        if (Find("EXPKEYSIG") is { } expiredKey)
        {
            return new(SignatureMethod.Gpg, SignatureStatus.Valid, Signer(expiredKey, "EXPKEYSIG"), "Good signature, but the key has expired.");
        }

        if (Find("REVKEYSIG") is { } revokedKey)
        {
            return new(SignatureMethod.Gpg, SignatureStatus.Invalid, Signer(revokedKey, "REVKEYSIG"), "The signature is good but the key was revoked — do not trust it.");
        }

        if (Find("BADSIG") is { } bad)
        {
            return new(SignatureMethod.Gpg, SignatureStatus.Invalid, Signer(bad, "BADSIG"), "BAD signature: the file was changed after it was signed, or this is not its signature.");
        }

        if (Find("NO_PUBKEY") is { } missing)
        {
            var key = missing["[GNUPG:] NO_PUBKEY".Length..].Trim();
            return new(SignatureMethod.Gpg, SignatureStatus.UnknownSigner, key, $"Signed with key {key}, which is not in your keyring (gpg --recv-keys {key}).");
        }

        if (Find("NODATA") is not null)
        {
            return new(SignatureMethod.Gpg, SignatureStatus.NotSigned, string.Empty, "That is not an OpenPGP signature.");
        }

        return new(SignatureMethod.Gpg, SignatureStatus.Unavailable, string.Empty, exitCode == 0 ? "gpg gave no verdict." : fallback);
    }

    private async Task<SignResult> SignGpgAsync(string filePath, string? keyIdentifier, CancellationToken cancellationToken)
    {
        var target = filePath + ".asc";
        if (File.Exists(target))
        {
            return new(false, null, $"{Path.GetFileName(target)} exists — not overwritten.");
        }

        var arguments = new List<string> { "--batch", "--yes", "--armor", "--detach-sign" };
        if (!string.IsNullOrWhiteSpace(keyIdentifier))
        {
            // A key id starting with '-' would be read as an option.
            if (keyIdentifier.TrimStart().StartsWith('-'))
            {
                return new(false, null, "That is not a key id.");
            }

            arguments.AddRange(["--local-user", keyIdentifier.Trim()]);
        }

        arguments.AddRange(["--output", target, filePath]);
        var output = await runner.RunAsync("gpg", arguments, null, cancellationToken);
        if (output.ExitCode == -1)
        {
            return new(false, null, "gpg is not installed (or not on the PATH).");
        }

        return output.Success && File.Exists(target)
            ? new(true, target, $"Wrote {Path.GetFileName(target)}.")
            : new(false, null, output.FirstErrorLine);
    }

    // ---- Authenticode ----

    private async Task<SignatureVerdict> VerifyAuthenticodeAsync(string filePath, CancellationToken cancellationToken)
    {
        var script =
            $"$s = Get-AuthenticodeSignature -LiteralPath {Literal(filePath)}; " +
            "Write-Output $s.Status; " +
            "Write-Output $s.SignerCertificate.Subject; " +
            "Write-Output $s.StatusMessage";
        var output = await RunPowerShellAsync(script, cancellationToken);
        if (output.ExitCode == -1)
        {
            return new(SignatureMethod.Authenticode, SignatureStatus.Unavailable, string.Empty, "PowerShell could not be started.");
        }

        return ParseAuthenticode(output.StandardOutput, output.Success ? null : output.FirstErrorLine);
    }

    /// <summary>Reads the three lines the script above prints.</summary>
    internal static SignatureVerdict ParseAuthenticode(string text, string? failure = null)
    {
        var lines = text.Split(['\r', '\n'], StringSplitOptions.None).Select(line => line.Trim()).ToArray();
        if (failure is not null || lines.Length == 0 || lines[0].Length == 0)
        {
            return new(SignatureMethod.Authenticode, SignatureStatus.Unavailable, string.Empty, failure ?? "No answer from PowerShell.");
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

    private async Task<SignResult> SignAuthenticodeAsync(string filePath, string? thumbprint, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new(false, null, "Authenticode signing is only available on Windows.");
        }

        if (thumbprint is null || !ThumbprintPattern().IsMatch(thumbprint.Trim()))
        {
            return new(false, null, "Choose a certificate (a 40-digit hexadecimal thumbprint).");
        }

        var script =
            $"$c = Get-ChildItem Cert:\\CurrentUser\\My | Where-Object {{ $_.Thumbprint -eq '{thumbprint.Trim().ToUpperInvariant()}' }} | Select-Object -First 1; " +
            "if (-not $c) { Write-Error 'That certificate is not in your store.'; exit 1 }; " +
            $"$r = Set-AuthenticodeSignature -LiteralPath {Literal(filePath)} -Certificate $c -HashAlgorithm SHA256; " +
            "if ($r.Status -ne 'Valid' -and $r.Status -ne 'UnknownError') { Write-Error $r.StatusMessage; exit 1 }; " +
            "Write-Output $r.StatusMessage";
        var output = await RunPowerShellAsync(script, cancellationToken);
        return output.Success
            ? new(true, null, "Signed.")
            : new(false, null, output.FirstErrorLine);
    }

    public async Task<IReadOnlyList<SigningCertificate>> GetCertificatesAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            return [];
        }

        var output = await RunPowerShellAsync(
            "Get-ChildItem Cert:\\CurrentUser\\My -CodeSigningCert | ForEach-Object { \"$($_.Thumbprint)|$($_.NotAfter.ToString('o'))|$($_.Subject)\" }",
            cancellationToken);
        return output.Success ? ParseCertificates(output.StandardOutput) : [];
    }

    internal static IReadOnlyList<SigningCertificate> ParseCertificates(string text)
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

    /// <summary>pwsh when installed, else Windows PowerShell — the latter is on every Windows machine.</summary>
    private async Task<ProcessOutput> RunPowerShellAsync(string script, CancellationToken cancellationToken)
    {
        string[] arguments = ["-NoProfile", "-NonInteractive", "-Command", script];
        var output = await runner.RunAsync("pwsh", arguments, null, cancellationToken);
        return output.ExitCode == -1
            ? await runner.RunAsync("powershell", arguments, null, cancellationToken)
            : output;
    }

    /// <summary>A path as a PowerShell single-quoted literal: nothing in a file name can end it.</summary>
    internal static string Literal(string value) => "'" + value.Replace("'", "''") + "'";

    [GeneratedRegex("^[0-9A-Fa-f]{40}$")]
    private static partial Regex ThumbprintPattern();

    // ---- PEM (in-process) ----

    private async Task<SignatureVerdict> VerifyPemAsync(string filePath, string signaturePath, string? publicKeyPath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(publicKeyPath))
        {
            return new(SignatureMethod.Pem, SignatureStatus.UnknownSigner, string.Empty,
                $"{Path.GetFileName(signaturePath)} is an iTerminal signature: give the signer's public key (PEM) to check it.");
        }

        try
        {
            // "iTerminal-signature-v1 <rsa|ecdsa> <base64>"
            var fields = (await File.ReadAllTextAsync(signaturePath, cancellationToken)).Trim().Split(' ', 3, StringSplitOptions.TrimEntries);
            if (fields.Length != 3 || fields[0] != PemPrefix)
            {
                return new(SignatureMethod.Pem, SignatureStatus.Invalid, string.Empty, "The signature file is damaged.");
            }

            var signature = Convert.FromBase64String(fields[2]);
            var hash = await HashAsync(filePath, cancellationToken);
            var pem = await File.ReadAllTextAsync(publicKeyPath, cancellationToken);
            var (valid, fingerprint) = fields[1] switch
            {
                "rsa" => VerifyRsa(pem, hash, signature),
                "ecdsa" => VerifyEcdsa(pem, hash, signature),
                _ => throw new CryptographicException($"Unknown algorithm '{fields[1]}'."),
            };
            return valid
                ? new(SignatureMethod.Pem, SignatureStatus.Valid, fingerprint, $"Good {fields[1].ToUpperInvariant()} signature.")
                : new(SignatureMethod.Pem, SignatureStatus.Invalid, fingerprint, "BAD signature: the file was changed, or the key is not the signer's.");
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new(SignatureMethod.Pem, SignatureStatus.Unavailable, string.Empty, $"Could not check it: {exception.Message}");
        }
    }

    private static (bool Valid, string Fingerprint) VerifyRsa(string pem, byte[] hash, byte[] signature)
    {
        using var rsa = RSA.Create();
        rsa.ImportFromPem(pem);
        return (rsa.VerifyHash(hash, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1), Fingerprint(rsa.ExportSubjectPublicKeyInfo()));
    }

    private static (bool Valid, string Fingerprint) VerifyEcdsa(string pem, byte[] hash, byte[] signature)
    {
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(pem);
        return (ecdsa.VerifyHash(hash, signature), Fingerprint(ecdsa.ExportSubjectPublicKeyInfo()));
    }

    private async Task<SignResult> SignPemAsync(string filePath, string? keyPath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(keyPath) || !File.Exists(keyPath))
        {
            return new(false, null, "Give the private key file (PEM).");
        }

        var target = filePath + ".sig";
        if (File.Exists(target))
        {
            return new(false, null, $"{Path.GetFileName(target)} exists — not overwritten.");
        }

        try
        {
            var pem = await File.ReadAllTextAsync(keyPath, cancellationToken);
            if (pem.Contains("ENCRYPTED", StringComparison.Ordinal))
            {
                return new(false, null, "That key is passphrase-protected. Use gpg for an encrypted key.");
            }

            var hash = await HashAsync(filePath, cancellationToken);
            string algorithm;
            byte[] signature;
            if (pem.Contains("EC PRIVATE KEY", StringComparison.Ordinal) || IsEcPkcs8(pem))
            {
                using var ecdsa = ECDsa.Create();
                ecdsa.ImportFromPem(pem);
                signature = ecdsa.SignHash(hash);
                algorithm = "ecdsa";
            }
            else
            {
                using var rsa = RSA.Create();
                rsa.ImportFromPem(pem);
                signature = rsa.SignHash(hash, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                algorithm = "rsa";
            }

            await File.WriteAllTextAsync(target, $"{PemPrefix} {algorithm} {Convert.ToBase64String(signature)}\n", cancellationToken);
            return new(true, target, $"Wrote {Path.GetFileName(target)}.");
        }
        catch (Exception exception) when (exception is CryptographicException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new(false, null, $"Could not sign: {exception.Message}");
        }
    }

    /// <summary>A PKCS#8 key does not say what it holds in its header; try it as ECDSA.</summary>
    private static bool IsEcPkcs8(string pem)
    {
        if (!pem.Contains("BEGIN PRIVATE KEY", StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportFromPem(pem);
            return true;
        }
        catch (Exception exception) when (exception is CryptographicException or ArgumentException)
        {
            return false;
        }
    }

    private static async Task<byte[]> HashAsync(string filePath, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16, useAsync: true);
        return await SHA256.HashDataAsync(stream, cancellationToken);
    }

    /// <summary>The short fingerprint of a public key — what the signer can be recognised by.</summary>
    private static string Fingerprint(byte[] subjectPublicKeyInfo) =>
        "SHA256:" + Convert.ToHexString(SHA256.HashData(subjectPublicKeyInfo))[..16].ToLowerInvariant();

    public Task<SignResult> SignAsync(
        string filePath,
        SignatureMethod method,
        string? keyIdentifier = null,
        string? keyPath = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
        {
            return Task.FromResult(new SignResult(false, null, "The file is not there."));
        }

        return method switch
        {
            SignatureMethod.Gpg => SignGpgAsync(filePath, keyIdentifier, cancellationToken),
            SignatureMethod.Authenticode => SignAuthenticodeAsync(filePath, keyIdentifier, cancellationToken),
            _ => SignPemAsync(filePath, keyPath, cancellationToken),
        };
    }
}
