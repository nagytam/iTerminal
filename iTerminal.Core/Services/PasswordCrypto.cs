using System.Security.Cryptography;
using System.Text;

namespace iTerminal.Core.Services;

/// <summary>
/// The password manager's cryptography: a key derived from the master password (PBKDF2-SHA256), and AES-GCM for
/// everything written to disk. Each sealed value carries its own random nonce and authentication tag, so a value
/// altered on disk fails to open rather than decrypting to something wrong.
/// </summary>
internal static class PasswordCrypto
{
    public const int KeySize = 32;
    public const int SaltSize = 16;
    public const int DefaultIterations = 600_000;

    private const int NonceSize = 12;
    private const int TagSize = 16;

    public static byte[] NewSalt() => RandomNumberGenerator.GetBytes(SaltSize);

    public static byte[] DeriveKey(string masterPassword, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(masterPassword), salt, iterations, HashAlgorithmName.SHA256, KeySize);

    /// <summary>nonce, tag and ciphertext, base64 in one string.</summary>
    public static string Seal(byte[] key, string plaintext)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var bytes = Encoding.UTF8.GetBytes(plaintext);
        var ciphertext = new byte[bytes.Length];
        var tag = new byte[TagSize];
        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, bytes, ciphertext, tag);
        return Convert.ToBase64String([.. nonce, .. tag, .. ciphertext]);
    }

    /// <summary>The plaintext, or null when the value does not open with this key.</summary>
    public static string? Open(byte[] key, string sealedValue)
    {
        try
        {
            var all = Convert.FromBase64String(sealedValue);
            var nonce = all[..NonceSize];
            var tag = all[NonceSize..(NonceSize + TagSize)];
            var ciphertext = all[(NonceSize + TagSize)..];
            var plaintext = new byte[ciphertext.Length];
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(nonce, ciphertext, tag, plaintext);
            return Encoding.UTF8.GetString(plaintext);
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException or ArgumentException)
        {
            return null;
        }
    }
}
