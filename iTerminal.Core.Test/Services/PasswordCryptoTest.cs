using iTerminal.Core.Services;

namespace iTerminal.Core.Test.Services;

/// <summary>A saved password is sealed with AES-GCM under the vault key: it opens with that key and with nothing else, and a changed byte is refused.</summary>
[TestClass]
public class PasswordCryptoTest
{
    [TestMethod]
    public void ASealedValueOpensWithTheSameKey()
    {
        byte[] key = PasswordCrypto.NewKey();

        Assert.AreEqual("hunter2 — jelszó ✓", PasswordCrypto.Open(key, PasswordCrypto.Seal(key, "hunter2 — jelszó ✓")));
    }

    [TestMethod]
    public void AnEmptyPasswordRoundTrips()
    {
        byte[] key = PasswordCrypto.NewKey();

        Assert.AreEqual(string.Empty, PasswordCrypto.Open(key, PasswordCrypto.Seal(key, string.Empty)));
    }

    [TestMethod]
    public void TheSameTextSealsDifferentlyEachTime()
    {
        // A fresh nonce per value: two identical passwords do not show as the same ciphertext.
        byte[] key = PasswordCrypto.NewKey();

        Assert.AreNotEqual(PasswordCrypto.Seal(key, "same"), PasswordCrypto.Seal(key, "same"));
    }

    [TestMethod]
    public void AnotherKeyDoesNotOpenIt()
    {
        string sealed_ = PasswordCrypto.Seal(PasswordCrypto.NewKey(), "secret");

        Assert.IsNull(PasswordCrypto.Open(PasswordCrypto.NewKey(), sealed_));
    }

    [TestMethod]
    public void AChangedByteIsRefused()
    {
        byte[] key = PasswordCrypto.NewKey();
        byte[] bytes = Convert.FromBase64String(PasswordCrypto.Seal(key, "secret"));
        bytes[^1] ^= 0x01;

        Assert.IsNull(PasswordCrypto.Open(key, Convert.ToBase64String(bytes)));
    }

    [TestMethod]
    public void AValueTooShortToHoldANonceAndTagIsRefusedNotThrown()
    {
        byte[] key = PasswordCrypto.NewKey();

        Assert.IsNull(PasswordCrypto.Open(key, Convert.ToBase64String([1, 2, 3])));
        Assert.IsNull(PasswordCrypto.Open(key, string.Empty));
    }

    [TestMethod]
    public void NotBase64IsRefused()
    {
        Assert.IsNull(PasswordCrypto.Open(PasswordCrypto.NewKey(), "not base64!!"));
    }

    [TestMethod]
    public void AKeyOfTheWrongLengthIsRefused()
    {
        string sealed_ = PasswordCrypto.Seal(PasswordCrypto.NewKey(), "secret");

        Assert.IsNull(PasswordCrypto.Open(new byte[16], sealed_));
    }
}
