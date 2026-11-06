using iTerminal.Core.Services.Processes;
using iTerminal.Core.Services.Signing;
using System.Security.Cryptography;
using System.Text;

namespace iTerminal.Core.Services.Checksums;

public enum PublisherTool
{
    Gpg,
    Minisign,
    Sigstore,
}

public enum PublisherSignatureStatus
{
    /// <summary>The signature matches and the key is one this machine holds (a key in the keyring is not the same as a trusted one).</summary>
    Good,

    /// <summary>The signature matches, but the key has expired. Never counts as verifying a checksum list.</summary>
    ExpiredKey,

    /// <summary>The signature does NOT match — the data changed, it is someone else's signature, or the key was revoked.</summary>
    Bad,

    /// <summary>The public key is not here: not in the keyring, or no minisign key configured.</summary>
    UnknownKey,

    /// <summary>The tool (gpg, minisign) is not installed.</summary>
    ToolMissing,

    /// <summary>A signature is there but nothing was run: sigstore needs the signer's identity, which only the user knows.</summary>
    Unchecked,

    /// <summary>The check could not be made, or the data changed while it was made.</summary>
    Error,
}

/// <summary>
/// One signature found beside a file and what its tool said. <paramref name="Subject"/> is the file name that was signed —
/// the file itself, or the checksum list; <paramref name="SubjectSha256"/> is the SHA-256 of the list when the subject is a
/// small checksum list that did not change while the tool ran (null otherwise).
/// </summary>
public sealed record PublisherVerification(
    PublisherTool Tool,
    string Subject,
    string SignatureFile,
    PublisherSignatureStatus Status,
    string Signer,
    string Detail,
    string? SubjectSha256);

/// <summary>Checks the publisher's signature found beside a file, or beside the checksum list that names it.</summary>
public interface IPublisherSignatureService
{
    /// <summary>
    /// Looks for signatures beside the local file <paramref name="filePath"/> — on the checksum list
    /// <paramref name="writtenSource"/> (<c>SHA256SUMS</c>) first, then on the file itself — and checks each with its tool.
    /// Empty when there are none.
    /// </summary>
    Task<IReadOnlyList<PublisherVerification>> VerifyAsync(string filePath, string? writtenSource, CancellationToken cancellationToken = default);
}

/// <summary>
/// <see cref="IPublisherSignatureService"/> over <c>gpg</c> and <c>minisign</c> through <see cref="IExternalProcessRunner"/>.
///
/// <list type="bullet">
/// <item><b>Keys are only ever the user's own</b>: gpg's keyring, minisign's own configured key. A key lying beside the
/// file would be planted together with a forged signature, so none is read from there.</item>
/// <item><b>Nothing is run through a shell.</b> Paths are single arguments, gpg gets <c>--</c> before them, and a
/// name beginning with <c>-</c> is refused.</item>
/// <item><b>sigstore is found, not checked</b>: <c>cosign verify-blob</c> will not run without the signer's identity
/// (or a key), which this program cannot know — so it is reported as <see cref="PublisherSignatureStatus.Unchecked"/>.</item>
/// <item><b>The checksum list is hashed before and after the tool runs</b>; a change in between is an error, and the
/// hash is returned so the caller can tell the list it parsed is the list that was verified.</item>
/// </list>
/// </summary>
public class PublisherSignatureService(IExternalProcessRunner runner) : IPublisherSignatureService
{
    private static readonly string[] SignatureExtensions = [".asc", ".sig", ".gpg", ".minisig", ".bundle"];
    private const string PemPrefix = "iTerminal-signature-v1";

    public async Task<IReadOnlyList<PublisherVerification>> VerifyAsync(string filePath, string? writtenSource, CancellationToken cancellationToken = default)
    {
        var folder = Path.GetDirectoryName(filePath);
        var name = Path.GetFileName(filePath);
        if (string.IsNullOrEmpty(folder) || string.IsNullOrEmpty(name))
        {
            return [];
        }

        var subjects = new List<string>();
        if (!string.IsNullOrEmpty(writtenSource))
        {
            subjects.Add(writtenSource);
        }

        if (!subjects.Contains(name, StringComparer.Ordinal))
        {
            subjects.Add(name);
        }

        var results = new List<PublisherVerification>();
        foreach (string subject in subjects)
        {
            foreach (string extension in SignatureExtensions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string signature = Path.Combine(folder, subject + extension);
                if (!File.Exists(signature))
                {
                    continue;
                }

                if (PickTool(signature, extension) is not { } tool)
                {
                    continue;
                }

                results.Add(await VerifyOneAsync(tool, Path.Combine(folder, subject), signature, subject == writtenSource, cancellationToken));
            }
        }

        return results;
    }

    /// <summary>The tool that reads this signature file, or null when it is not a publisher signature (it is this program's own <c>.sig</c>, or unreadable).</summary>
    internal static PublisherTool? PickTool(string signaturePath, string extension)
    {
        switch (extension)
        {
            case ".minisig":
                return PublisherTool.Minisign;
            case ".bundle":
                return PublisherTool.Sigstore;
            case ".asc":
            case ".gpg":
                return PublisherTool.Gpg;
        }

        byte[] head;
        try
        {
            using FileStream stream = new(signaturePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            head = new byte[256];
            int read = stream.Read(head, 0, head.Length);
            Array.Resize(ref head, read);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A signature that cannot be read is still reported, by the tool that would have read it.
            return PublisherTool.Gpg;
        }

        string text = Encoding.Latin1.GetString(head);
        if (text.StartsWith(PemPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        if (text.StartsWith("untrusted comment:", StringComparison.Ordinal))
        {
            return PublisherTool.Minisign;
        }

        bool binary = head.Any(b => b < 0x20 && b is not (byte)'\r' and not (byte)'\n' and not (byte)'\t' || b > 0x7e);
        if (binary || text.Contains("BEGIN PGP", StringComparison.Ordinal))
        {
            return PublisherTool.Gpg;
        }

        // Text that is neither: cosign's base64 signature (or its JSON).
        return PublisherTool.Sigstore;
    }

    private async Task<PublisherVerification> VerifyOneAsync(PublisherTool tool, string subjectPath, string signaturePath, bool subjectIsList, CancellationToken cancellationToken)
    {
        string subject = Path.GetFileName(subjectPath);
        string signatureFile = Path.GetFileName(signaturePath);
        PublisherVerification Make(PublisherSignatureStatus status, string signer = "", string detail = "", string? hash = null) =>
            new(tool, subject, signatureFile, status, signer, detail, hash);

        if (tool == PublisherTool.Sigstore)
        {
            return Make(PublisherSignatureStatus.Unchecked);
        }

        // Paths are absolute, so a leading '-' can only be in the name; refuse it anyway rather than rely on that.
        if (subject.StartsWith('-') || signatureFile.StartsWith('-'))
        {
            return Make(PublisherSignatureStatus.Error, detail: "a name that starts with '-' is not checked");
        }

        Snapshot before;
        try
        {
            before = await TakeSnapshotAsync(subjectPath, subjectIsList, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Make(PublisherSignatureStatus.Error, detail: exception.Message);
        }

        ProcessOutput output;
        if (tool == PublisherTool.Gpg)
        {
            output = await runner.RunAsync("gpg", ["--status-fd", "1", "--batch", "--no-tty", "--verify", "--", signaturePath, subjectPath], null, cancellationToken);
        }
        else
        {
            // No -p: minisign then uses the user's own ~/.minisign/minisign.pub.
            output = await runner.RunAsync("minisign", ["-V", "-m", subjectPath, "-x", signaturePath], null, cancellationToken);
        }

        if (output.ExitCode == -1)
        {
            return Make(PublisherSignatureStatus.ToolMissing, detail: tool == PublisherTool.Gpg ? "gpg" : "minisign");
        }

        PublisherVerification verdict = tool == PublisherTool.Gpg ? FromGpg(output, Make) : FromMinisign(output, Make);
        try
        {
            Snapshot after = await TakeSnapshotAsync(subjectPath, subjectIsList, cancellationToken);
            if (after != before)
            {
                return Make(PublisherSignatureStatus.Error, detail: "changed while it was checked");
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Make(PublisherSignatureStatus.Error, detail: exception.Message);
        }

        return verdict with { SubjectSha256 = before.Sha256 };
    }

    private static PublisherVerification FromGpg(ProcessOutput output, Func<PublisherSignatureStatus, string, string, string?, PublisherVerification> make)
    {
        SignatureVerdict parsed = SignatureService.ParseGpgStatus(output.StandardOutput, output.ExitCode, output.FirstErrorLine);
        // The expired key is its own status: a good signature, but not one that vouches for a list.
        bool expired = parsed.Status == SignatureStatus.Valid && output.StandardOutput.Contains("[GNUPG:] EXPKEYSIG", StringComparison.Ordinal);
        PublisherSignatureStatus status = parsed.Status switch
        {
            SignatureStatus.Valid => expired ? PublisherSignatureStatus.ExpiredKey : PublisherSignatureStatus.Good,
            SignatureStatus.Invalid => PublisherSignatureStatus.Bad,
            SignatureStatus.UnknownSigner => PublisherSignatureStatus.UnknownKey,
            _ => PublisherSignatureStatus.Error,
        };
        return make(status, parsed.Signer, status == PublisherSignatureStatus.Error ? parsed.Message : string.Empty, null);
    }

    private static PublisherVerification FromMinisign(ProcessOutput output, Func<PublisherSignatureStatus, string, string, string?, PublisherVerification> make)
    {
        string all = output.StandardOutput + "\n" + output.StandardError;
        if (output.Success)
        {
            string signer = all.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .FirstOrDefault(line => line.StartsWith("Trusted comment:", StringComparison.Ordinal))?["Trusted comment:".Length..].Trim() ?? string.Empty;
            return make(PublisherSignatureStatus.Good, signer, string.Empty, null);
        }

        if (all.Contains("verification failed", StringComparison.OrdinalIgnoreCase))
        {
            return make(PublisherSignatureStatus.Bad, string.Empty, string.Empty, null);
        }

        if (all.Contains("public key", StringComparison.OrdinalIgnoreCase))
        {
            return make(PublisherSignatureStatus.UnknownKey, string.Empty, string.Empty, null);
        }

        return make(PublisherSignatureStatus.Error, string.Empty, output.FirstErrorLine, null);
    }

    /// <summary>What a subject looked like at one moment: a list by its content hash (it is small), a file by size and time (it may be an ISO).</summary>
    private readonly record struct Snapshot(string? Sha256, long Length, DateTime Modified);

    private static async Task<Snapshot> TakeSnapshotAsync(string path, bool hash, CancellationToken cancellationToken)
    {
        FileInfo info = new(path);
        if (!hash)
        {
            return new Snapshot(null, info.Length, info.LastWriteTimeUtc);
        }

        await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, FileOptions.Asynchronous);
        string sha = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken));
        return new Snapshot(sha, info.Length, info.LastWriteTimeUtc);
    }
}
