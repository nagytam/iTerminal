using iTerminal.Security.OpenPgp;

namespace iTerminal.Security.Signing;

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

/// <summary>
/// What one method found: the verdict, who signed, and a sentence for the screen. <see cref="Packet"/> is the first signature
/// packet of a gpg signature, read from the file's bytes before gpg ran; it is a fact about the file, not part of the verdict.
/// </summary>
public sealed record SignatureVerdict(SignatureMethod Method, SignatureStatus Status, string Signer, string Message, OpenPgpSignaturePacket? Packet = null);

/// <summary>What signing did. <see cref="SignaturePath"/> is the detached file written, or null for an embedded signature.</summary>
public sealed record SignResult(bool Success, string? SignaturePath, string Message);

/// <summary>A code-signing certificate the Windows certificate store holds.</summary>
public sealed record SigningCertificate(string Thumbprint, string Subject, DateTime Expires);

/// <summary>What is available on this machine, so the dialog offers only what can work.</summary>
public sealed record SigningCapabilities(bool Gpg, bool Authenticode);
