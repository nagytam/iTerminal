using System.Text;
using iConsole.OpenPgp;

namespace iConsole.Test.OpenPgp;

[TestClass]
public class OpenPgpSignatureReaderTest
{
    /// <summary>A detached signature made by gpg 2.4.9 with a throwaway Ed25519 key (version 4, SHA-256). Only the signature is kept.</summary>
    private const string GpgV4Armor = """
        -----BEGIN PGP SIGNATURE-----

        iHUEABYIAB0WIQSkLvOeh1cxCf0X2vsv5fIZAYukGwUCawBxbAAKCRAv5fIZAYuk
        G+SIAP0ROqWOB7vZ5t3s1oavKQSkjsTlBsXinF14cHTa/py35AEAliGAkgJja6dW
        6oPG/7gD9TrYio3BMRHfA/dh6QZTNQs=
        =VDEd
        -----END PGP SIGNATURE-----
        """;

    private const string GpgV4Fingerprint = "A42EF39E87573109FD17DAFB2FE5F219018BA41B";
    private const long GpgV4Created = 1795191148;

    // ---- builders for hand-made packets ----

    private static byte[] Bytes(params int[] values) => values.Select(value => (byte)value).ToArray();

    private static byte[] Concat(params byte[][] parts) => [.. parts.SelectMany(part => part)];

    private static byte[] BigEndian(long value, int width) =>
        Enumerable.Range(0, width).Select(index => (byte)(value >> (8 * (width - 1 - index)))).ToArray();

    /// <summary>A new-format packet with a one-, two- or five-octet length.</summary>
    private static byte[] NewPacket(int tag, byte[] body)
    {
        byte[] length = body.Length < 192
            ? [(byte)body.Length]
            : body.Length < 8384
                ? [(byte)(((body.Length - 192) >> 8) + 192), (byte)((body.Length - 192) & 0xFF)]
                : Concat([255], BigEndian(body.Length, 4));
        return Concat([(byte)(0xC0 | tag)], length, body);
    }

    /// <summary>A subpacket: a length that counts its type octet, the type, then the data (one-octet length only).</summary>
    private static byte[] Subpacket(int type, byte[] data) => Concat([(byte)(data.Length + 1), (byte)type], data);

    private static byte[] Version3Body(long created, byte[] keyId, int publicKey, int hash) =>
        Concat(Bytes(3, 5, 0x00), BigEndian(created, 4), keyId, Bytes(publicKey, hash, 0xAB, 0xCD, 0, 1, 1));

    private static byte[] Version6Body(int publicKey, int hash, byte[] hashed, byte[] unhashed, byte[] salt) =>
        Concat(
            Bytes(6, 0x00, publicKey, hash),
            BigEndian(hashed.Length, 4),
            hashed,
            BigEndian(unhashed.Length, 4),
            unhashed,
            Bytes(0xAB, 0xCD),
            Bytes(salt.Length),
            salt,
            Bytes(0, 1, 1));

    private static byte[] Version4Body(int hash, byte[] hashed, byte[] unhashed) =>
        Concat(
            Bytes(4, 0x00, 22, hash),
            BigEndian(hashed.Length, 2),
            hashed,
            BigEndian(unhashed.Length, 2),
            unhashed,
            Bytes(0xAB, 0xCD),
            Bytes(0, 1, 1));

    private static byte[] Fingerprint(int keyVersion, byte[] value) => Concat(Bytes(keyVersion), value);

    private static byte[] Repeat(int value, int count) => Enumerable.Repeat((byte)value, count).ToArray();

    // ---- armor ----

    [TestMethod]
    public void ReadsGpgVersion4ArmoredSignature()
    {
        OpenPgpSignatureReport report = OpenPgpSignatureReader.Read(Encoding.ASCII.GetBytes(GpgV4Armor));

        Assert.IsTrue(report.Readable, report.Reason);
        Assert.AreEqual(1, report.SignatureCount);
        OpenPgpSignaturePacket packet = report.Packets[0];
        Assert.AreEqual(4, packet.Version);
        Assert.AreEqual(0x00, packet.SignatureType);
        Assert.AreEqual(22, packet.PublicKeyAlgorithm);
        Assert.AreEqual(8, packet.HashAlgorithm);
        Assert.AreEqual(DateTimeOffset.FromUnixTimeSeconds(GpgV4Created), packet.CreationTime);
        Assert.AreEqual(GpgV4Fingerprint, packet.Fingerprint);
        // gpg puts the key ID in the unhashed area, which the signature does not cover, so it is not used.
        Assert.IsNull(packet.KeyId);
        Assert.IsFalse(packet.NonConformingIssuerKeyId);
        Assert.IsFalse(report.Truncated);
    }

    [TestMethod]
    public void ArmorWithoutChecksumLineIsStillReadable()
    {
        string withoutCrc = string.Join('\n', GpgV4Armor.Split('\n').Where(line => !line.StartsWith('=')));

        OpenPgpSignatureReport report = OpenPgpSignatureReader.Read(Encoding.ASCII.GetBytes(withoutCrc));

        Assert.IsTrue(report.Readable, report.Reason);
        Assert.AreEqual(GpgV4Fingerprint, report.Packets[0].Fingerprint);
    }

    [TestMethod]
    public void ArmorWithWrongChecksumIsUnreadable()
    {
        string wrongCrc = GpgV4Armor.Replace("=VDEd", "=AAAA");

        OpenPgpSignatureReport report = OpenPgpSignatureReader.Read(Encoding.ASCII.GetBytes(wrongCrc));

        Assert.IsFalse(report.Readable);
        StringAssert.Contains(report.Reason, "checksum");
    }

    [TestMethod]
    public void KeyBlockIsNotASignature()
    {
        string key = GpgV4Armor.Replace("BEGIN PGP SIGNATURE", "BEGIN PGP PUBLIC KEY BLOCK").Replace("END PGP SIGNATURE", "END PGP PUBLIC KEY BLOCK");

        OpenPgpSignatureReport report = OpenPgpSignatureReader.Read(Encoding.ASCII.GetBytes(key));

        Assert.IsFalse(report.Readable);
        StringAssert.Contains(report.Reason, "not an OpenPGP signature");
    }

    [TestMethod]
    public void ArmorWithoutEndLineIsUnreadable()
    {
        string open = GpgV4Armor.Replace("-----END PGP SIGNATURE-----", string.Empty);

        OpenPgpSignatureReport report = OpenPgpSignatureReader.Read(Encoding.ASCII.GetBytes(open));

        Assert.IsFalse(report.Readable);
        StringAssert.Contains(report.Reason, "END");
    }

    // ---- binary packets ----

    [TestMethod]
    public void ReadsVersion3SignatureWithInlineTimeAndKeyId()
    {
        byte[] keyId = Bytes(0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88);
        byte[] packet = NewPacket(2, Version3Body(1700000000, keyId, 1, 8));

        OpenPgpSignatureReport report = OpenPgpSignatureReader.Read(packet);

        Assert.IsTrue(report.Readable, report.Reason);
        OpenPgpSignaturePacket parsed = report.Packets[0];
        Assert.AreEqual(3, parsed.Version);
        Assert.AreEqual(DateTimeOffset.FromUnixTimeSeconds(1700000000), parsed.CreationTime);
        Assert.AreEqual("1122334455667788", parsed.KeyId);
        Assert.IsNull(parsed.Fingerprint);
        Assert.AreEqual(1, parsed.PublicKeyAlgorithm);
        Assert.AreEqual(8, parsed.HashAlgorithm);
    }

    [TestMethod]
    public void ReadsVersion6SignatureFingerprintWithoutKeyId()
    {
        byte[] fingerprint = Fingerprint(6, Repeat(0xAB, 32));
        byte[] hashed = Concat(Subpacket(2, BigEndian(1700000001, 4)), Subpacket(33, fingerprint));
        byte[] packet = NewPacket(2, Version6Body(27, 8, hashed, [], Repeat(0x5A, 16)));

        OpenPgpSignatureReport report = OpenPgpSignatureReader.Read(packet);

        Assert.IsTrue(report.Readable, report.Reason);
        OpenPgpSignaturePacket parsed = report.Packets[0];
        Assert.AreEqual(6, parsed.Version);
        Assert.AreEqual(27, parsed.PublicKeyAlgorithm);
        Assert.AreEqual(DateTimeOffset.FromUnixTimeSeconds(1700000001), parsed.CreationTime);
        Assert.AreEqual(string.Concat(Enumerable.Repeat("AB", 32)), parsed.Fingerprint);
        Assert.IsNull(parsed.KeyId);
        Assert.IsFalse(parsed.NonConformingIssuerKeyId);
    }

    [TestMethod]
    public void VersionSixIssuerKeyIdIsFlaggedAndIgnored()
    {
        byte[] hashed = Subpacket(16, Bytes(1, 2, 3, 4, 5, 6, 7, 8));
        byte[] packet = NewPacket(2, Version6Body(27, 8, hashed, [], Repeat(0x5A, 16)));

        OpenPgpSignatureReport report = OpenPgpSignatureReader.Read(packet);

        Assert.IsTrue(report.Readable, report.Reason);
        Assert.IsTrue(report.Packets[0].NonConformingIssuerKeyId);
        Assert.IsNull(report.Packets[0].KeyId);
    }

    [TestMethod]
    public void UnhashedFingerprintAndTimeAreIgnoredButStructureIsChecked()
    {
        // A fingerprint and a creation time in the unhashed area are not covered by the signature, so neither is used.
        byte[] fingerprint = Fingerprint(4, Repeat(0xCD, 20));
        byte[] packet = NewPacket(2, Version4Body(8, [], Concat(Subpacket(33, fingerprint), Subpacket(2, BigEndian(1700000002, 4)))));

        OpenPgpSignatureReport report = OpenPgpSignatureReader.Read(packet);

        Assert.IsTrue(report.Readable, report.Reason);
        Assert.IsNull(report.Packets[0].Fingerprint);
        Assert.IsNull(report.Packets[0].CreationTime);
    }

    [TestMethod]
    public void MalformedUnhashedSubpacketStillMakesThePacketUnreadable()
    {
        // The value is ignored, but a wrong length is still a structural fault.
        byte[] badTime = Subpacket(2, Repeat(0x01, 3));
        byte[] packet = NewPacket(2, Version4Body(8, [], badTime));

        OpenPgpSignatureReport report = OpenPgpSignatureReader.Read(packet);

        Assert.IsFalse(report.Readable);
        StringAssert.Contains(report.Reason, "creation time");
    }

    [TestMethod]
    public void ArmorWithByteOrderMarkIsRead()
    {
        byte[] withBom = Concat([0xEF, 0xBB, 0xBF], Encoding.ASCII.GetBytes(GpgV4Armor));

        OpenPgpSignatureReport report = OpenPgpSignatureReader.Read(withBom);

        Assert.IsTrue(report.Readable, report.Reason);
        Assert.AreEqual(GpgV4Fingerprint, report.Packets[0].Fingerprint);
    }

    [TestMethod]
    public void FingerprintKeyVersionMismatchIsUnreadable()
    {
        byte[] fingerprint = Fingerprint(4, Repeat(0xAB, 20));
        byte[] packet = NewPacket(2, Version6Body(27, 8, Subpacket(33, fingerprint), [], Repeat(0x5A, 16)));

        OpenPgpSignatureReport report = OpenPgpSignatureReader.Read(packet);

        Assert.IsFalse(report.Readable);
        StringAssert.Contains(report.Reason, "key version");
    }

    [TestMethod]
    public void FingerprintWithWrongLengthIsUnreadable()
    {
        byte[] fingerprint = Fingerprint(4, Repeat(0xAB, 19));
        byte[] packet = NewPacket(2, Version4Body(8, [], Subpacket(33, fingerprint)));

        OpenPgpSignatureReport report = OpenPgpSignatureReader.Read(packet);

        Assert.IsFalse(report.Readable);
        StringAssert.Contains(report.Reason, "wrong length");
    }

    [TestMethod]
    public void VersionSixSaltOfWrongSizeIsUnreadable()
    {
        byte[] packet = NewPacket(2, Version6Body(27, 8, [], [], Repeat(0x5A, 12)));

        OpenPgpSignatureReport report = OpenPgpSignatureReader.Read(packet);

        Assert.IsFalse(report.Readable);
        StringAssert.Contains(report.Reason, "salt");
    }

    [TestMethod]
    public void VersionSixWithSha1IsUnreadable()
    {
        byte[] packet = NewPacket(2, Version6Body(27, 2, [], [], Repeat(0x5A, 16)));

        OpenPgpSignatureReport report = OpenPgpSignatureReader.Read(packet);

        Assert.IsFalse(report.Readable);
        StringAssert.Contains(report.Reason, "not allowed");
    }

    [TestMethod]
    public void TruncatedPacketIsUnreadable()
    {
        byte[] packet = NewPacket(2, Version4Body(8, [], Subpacket(33, Fingerprint(4, Repeat(0xAB, 20)))));
        byte[] cut = packet[..(packet.Length - 10)];

        OpenPgpSignatureReport report = OpenPgpSignatureReader.Read(cut);

        Assert.IsFalse(report.Readable);
        StringAssert.Contains(report.Reason, "cut short");
    }

    [TestMethod]
    public void FirstPartialLengthUnder512IsUnreadable()
    {
        // Partial length 2^8 = 256 octets as the first chunk, then a zero-length final chunk: RFC 9580 requires at least 512.
        byte[] packet = Concat([0xC2, (byte)(224 + 8)], Repeat(0, 256), [0]);

        OpenPgpSignatureReport report = OpenPgpSignatureReader.Read(packet);

        Assert.IsFalse(report.Readable);
        StringAssert.Contains(report.Reason, "512");
    }

    [TestMethod]
    public void PacketHeaderWithoutHighBitIsUnreadable()
    {
        // A valid signature followed by a byte with bit 7 clear: that is not OpenPGP framing.
        byte[] packet = Concat(NewPacket(2, Version3Body(1, Repeat(0x33, 8), 1, 8)), [0x02, 0x00]);

        OpenPgpSignatureReport report = OpenPgpSignatureReader.Read(packet);

        Assert.IsFalse(report.Readable);
        StringAssert.Contains(report.Reason, "high bit");
    }

    [TestMethod]
    public void OldFormatIndeterminateLengthRunsToTheEnd()
    {
        byte[] body = Version3Body(1700000000, Repeat(0x22, 8), 1, 8);
        byte[] packet = Concat([(byte)(0x80 | (2 << 2) | 3)], body);

        OpenPgpSignatureReport report = OpenPgpSignatureReader.Read(packet);

        Assert.IsTrue(report.Readable, report.Reason);
        Assert.AreEqual("2222222222222222", report.Packets[0].KeyId);
    }

    [TestMethod]
    public void MoreThanSixteenSignaturesAreTruncated()
    {
        byte[] one = NewPacket(2, Version3Body(1, Repeat(0x33, 8), 1, 8));
        byte[] many = Concat(Enumerable.Repeat(one, 17).ToArray());

        OpenPgpSignatureReport report = OpenPgpSignatureReader.Read(many);

        Assert.IsTrue(report.Readable, report.Reason);
        Assert.AreEqual(17, report.SignatureCount);
        Assert.AreEqual(OpenPgpSignatureReader.MaxListedPackets, report.Packets.Count);
        Assert.IsTrue(report.Truncated);
    }

    [TestMethod]
    public void FileWithNoSignaturePacketIsUnreadable()
    {
        // Tag 6 is a public key packet; a binary file with only that is not a signature.
        byte[] key = NewPacket(6, Concat(Bytes(4, 0, 0, 0, 0, 22), Repeat(0x01, 40)));

        OpenPgpSignatureReport report = OpenPgpSignatureReader.Read(key);

        Assert.IsFalse(report.Readable);
        StringAssert.Contains(report.Reason, "no signature packet");
    }

    [TestMethod]
    public void EmptyAndOversizeFilesAreUnreadable()
    {
        Assert.IsFalse(OpenPgpSignatureReader.Read([]).Readable);

        OpenPgpSignatureReport oversize = OpenPgpSignatureReader.Read(new byte[OpenPgpSignatureReader.MaxBytes + 1]);
        Assert.IsFalse(oversize.Readable);
        StringAssert.Contains(oversize.Reason, "larger");
    }

    [TestMethod]
    public void AlgorithmNamesAreFixedTokensWithNumbersForUnknownIds()
    {
        Assert.AreEqual("EdDSA", OpenPgpSignatureReader.PublicKeyName(22));
        Assert.AreEqual("SHA-256", OpenPgpSignatureReader.HashName(8));
        Assert.AreEqual("99", OpenPgpSignatureReader.HashName(99));
    }
}
