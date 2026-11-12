using System.Runtime.InteropServices;

namespace iTerminal.Core.Services.Updates;

/// <summary>What Authenticode verification found for a file.</summary>
public enum AuthenticodeStatus
{
    /// <summary>The file carries a signature that matches it and chains to a trusted root.</summary>
    Valid,

    /// <summary>The file carries no Authenticode signature at all.</summary>
    NotSigned,

    /// <summary>A signature is present but does not match the file, or its signer is not trusted.</summary>
    Invalid,

    /// <summary>The check could not be made: not Windows, the file is gone, or the API failed.</summary>
    Unavailable,
}

/// <summary>The verdict, the signer's subject when there is one, and a sentence for the log.</summary>
public sealed record AuthenticodeResult(AuthenticodeStatus Status, string Signer, string Message);

/// <summary>
/// Checks a file's embedded Authenticode signature. The update service uses it on a downloaded MSI
/// after the SHA-256 check: the digest comes from GitHub, and GitHub itself can be compromised.
/// </summary>
public interface IAuthenticodeVerifier
{
    /// <summary>
    /// Verifies <paramref name="filePath"/>. Synchronous by contract: <c>WinVerifyTrust</c> is a local
    /// API call, and the update runs it on a background task, never on a key handler.
    /// </summary>
    AuthenticodeResult Verify(string filePath);
}

/// <summary>
/// <see cref="IAuthenticodeVerifier"/> over <c>WinVerifyTrust</c> with
/// <c>WINTRUST_ACTION_GENERIC_VERIFY_V2</c>, the same API Windows uses when it shows an installer's
/// publisher. No PowerShell, and no parsing of tool output.
///
/// <para>Revocation is checked for the whole chain and MD2/MD4 are refused. A file that cannot carry a
/// signature answers <see cref="AuthenticodeStatus.NotSigned"/>; an unexpected API answer is
/// <see cref="AuthenticodeStatus.Unavailable"/> rather than "unsigned", so a failure is never mistaken
/// for a file that was simply never signed.</para>
/// </summary>
public sealed class WinVerifyTrustAuthenticodeVerifier : IAuthenticodeVerifier
{
    private static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    public AuthenticodeResult Verify(string filePath)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new(AuthenticodeStatus.Unavailable, string.Empty, "Authenticode is only available on Windows.");
        }

        if (!File.Exists(filePath))
        {
            return new(AuthenticodeStatus.Unavailable, string.Empty, "The file is not there.");
        }

        IntPtr path = Marshal.StringToHGlobalUni(filePath);
        IntPtr fileInfo = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
        try
        {
            Marshal.StructureToPtr(new WinTrustFileInfo
            {
                cbStruct = (uint)Marshal.SizeOf<WinTrustFileInfo>(),
                pcwszFilePath = path,
                hFile = IntPtr.Zero,
                pgKnownSubject = IntPtr.Zero,
            }, fileInfo, fDeleteOld: false);

            WinTrustData data = new()
            {
                cbStruct = (uint)Marshal.SizeOf<WinTrustData>(),
                pPolicyCallbackData = IntPtr.Zero,
                pSIPClientData = IntPtr.Zero,
                dwUIChoice = WTD_UI_NONE,
                fdwRevocationChecks = WTD_REVOKE_WHOLECHAIN,
                dwUnionChoice = WTD_CHOICE_FILE,
                pFile = fileInfo,
                dwStateAction = WTD_STATEACTION_VERIFY,
                hWVTStateData = IntPtr.Zero,
                pwszURLReference = IntPtr.Zero,
                dwProvFlags = WTD_DISABLE_MD2_MD4,
                dwUIContext = 0,
                pSignatureSettings = IntPtr.Zero,
            };

            Guid action = GenericVerifyV2;
            uint result = WinVerifyTrust(IntPtr.Zero, ref action, ref data);

            // Always close the state: WinVerifyTrust keeps the file open until it is told to let go.
            data.dwStateAction = WTD_STATEACTION_CLOSE;
            action = GenericVerifyV2;
            WinVerifyTrust(IntPtr.Zero, ref action, ref data);

            return Interpret(result);
        }
        finally
        {
            Marshal.FreeHGlobal(fileInfo);
            Marshal.FreeHGlobal(path);
        }
    }

    /// <summary>Turns WinVerifyTrust's answer into a verdict. Internal so the tests can reach the part that can be wrong.</summary>
    internal static AuthenticodeResult Interpret(uint result) => result switch
    {
        0 => new(AuthenticodeStatus.Valid, string.Empty, "The signature is valid."),
        TRUST_E_NOSIGNATURE or TRUST_E_SUBJECT_FORM_UNKNOWN =>
            new(AuthenticodeStatus.NotSigned, string.Empty, "The file is not Authenticode-signed."),
        TRUST_E_BAD_DIGEST or TRUST_E_SUBJECT_NOT_TRUSTED or TRUST_E_EXPLICIT_DISTRUST =>
            new(AuthenticodeStatus.Invalid, string.Empty, "The signature does not match the file, or the signer is not trusted."),
        CERT_E_UNTRUSTEDROOT or CERT_E_CHAINING or CERT_E_EXPIRED or CERT_E_REVOKED
            or CERT_E_REVOCATION_FAILURE or CERT_E_WRONG_USAGE or CERT_E_PURPOSE or CERT_E_CRITICAL
            or CERT_E_MALFORMED or CERT_E_INVALID_NAME or CERT_E_INVALID_POLICY or CERT_E_UNTRUSTEDCA
            or CERT_E_UNTRUSTEDTESTROOT or CERT_E_ISSUERCHAINING or CERT_E_PATHLENCONST or CERT_E_ROLE =>
            new(AuthenticodeStatus.Invalid, string.Empty, "The signature's certificate chain is not trusted, is expired or revoked, or is not a code-signing certificate."),
        _ => new(AuthenticodeStatus.Unavailable, string.Empty, $"WinVerifyTrust answered 0x{result:X8}."),
    };

    private const uint WTD_UI_NONE = 2;
    private const uint WTD_REVOKE_WHOLECHAIN = 1;
    private const uint WTD_CHOICE_FILE = 1;
    private const uint WTD_STATEACTION_VERIFY = 1;
    private const uint WTD_STATEACTION_CLOSE = 2;
    private const uint WTD_DISABLE_MD2_MD4 = 0x00002000;

    private const uint TRUST_E_SUBJECT_FORM_UNKNOWN = 0x800B0003;
    private const uint TRUST_E_SUBJECT_NOT_TRUSTED = 0x800B0004;
    private const uint TRUST_E_NOSIGNATURE = 0x800B0100;
    private const uint TRUST_E_EXPLICIT_DISTRUST = 0x800B0111;
    private const uint TRUST_E_BAD_DIGEST = 0x80096010;

    private const uint CERT_E_UNTRUSTEDROOT = 0x800B0109;
    private const uint CERT_E_CHAINING = 0x800B010A;
    private const uint CERT_E_EXPIRED = 0x800B0101;
    private const uint CERT_E_REVOKED = 0x800B010C;
    private const uint CERT_E_REVOCATION_FAILURE = 0x800B010E;
    private const uint CERT_E_WRONG_USAGE = 0x800B0110;
    private const uint CERT_E_PURPOSE = 0x800B0106;
    private const uint CERT_E_CRITICAL = 0x800B0105;
    private const uint CERT_E_MALFORMED = 0x800B0108;
    private const uint CERT_E_INVALID_NAME = 0x800B0114;
    private const uint CERT_E_INVALID_POLICY = 0x800B0113;
    private const uint CERT_E_UNTRUSTEDCA = 0x800B0112;
    private const uint CERT_E_UNTRUSTEDTESTROOT = 0x800B010D;
    private const uint CERT_E_ISSUERCHAINING = 0x800B0107;
    private const uint CERT_E_PATHLENCONST = 0x800B0102;
    private const uint CERT_E_ROLE = 0x800B0103;

    [DllImport("wintrust.dll", ExactSpelling = true, PreserveSig = true)]
    private static extern uint WinVerifyTrust(IntPtr hwnd, ref Guid pgActionID, ref WinTrustData pWVTData);

    [StructLayout(LayoutKind.Sequential)]
    private struct WinTrustFileInfo
    {
        public uint cbStruct;
        public IntPtr pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WinTrustData
    {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public IntPtr pFile;
        public uint dwStateAction;
        public IntPtr hWVTStateData;
        public IntPtr pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
        public IntPtr pSignatureSettings;
    }
}
