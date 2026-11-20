using System.Globalization;
using System.Text;

namespace iConsole.OpenPgp;

/// <summary>
/// One signature packet of a detached OpenPGP signature, as the bytes state it. Nothing here is verified: the
/// reader does not check the signature, does not look a key up and does not decide whether the signer is trusted.
/// Hex values are upper-case; the names come from fixed tables, so no text from the file is shown as a name.
/// </summary>
public sealed record OpenPgpSignaturePacket(
    int Version,
    int SignatureType,
    int PublicKeyAlgorithm,
    int HashAlgorithm,
    DateTimeOffset? CreationTime,
    string? KeyId,
    string? Fingerprint,
    bool NonConformingIssuerKeyId);

/// <summary>What the reader found. When <see cref="Readable"/> is false, <see cref="Reason"/> says why and nothing else is set.</summary>
public sealed record OpenPgpSignatureReport(bool Readable, string Reason, IReadOnlyList<OpenPgpSignaturePacket> Packets, int SignatureCount)
{
    /// <summary>True when the file holds more signature packets than <see cref="Packets"/> lists.</summary>
    public bool Truncated => SignatureCount > Packets.Count;
}

/// <summary>
/// Reads the signature packets of a detached OpenPGP signature, armored (<c>BEGIN PGP SIGNATURE</c>) or binary, for versions
/// 3, 4 and 6 (RFC 9580). It never throws: a file it cannot read gives an unreadable report with a reason.
///
/// <list type="bullet">
/// <item><b>Bounds.</b> A file over <see cref="MaxBytes"/> is refused. At most <see cref="MaxListedPackets"/> signature packets are
/// parsed and listed; the rest are counted and the report is marked truncated.</item>
/// <item><b>Armor.</b> The CRC-24 line is optional: a missing one is accepted, a wrong one makes the file unreadable.</item>
/// <item><b>Version 6 signers.</b> A version 6 signature must not carry an Issuer Key ID subpacket (RFC 9580 section 5.2.3.12). Such a
/// subpacket is ignored and the packet is flagged <see cref="OpenPgpSignaturePacket.NonConformingIssuerKeyId"/>; the packet is
/// still read.</item>
/// <item><b>Malformed packets</b> (a length past the end, a fingerprint whose length does not match its key version, a salt that does
/// not match its hash algorithm, a first partial length under 512 octets) make the whole file unreadable. The rule covers every
/// listed packet (the first 16). Packets past that are length-framed and counted, but their bodies are not parsed.</item>
/// <item><b>Identity comes from the hashed subpackets only.</b> The unhashed area is structure-checked, but its key ID, fingerprint and
/// creation time are never used, because a signature does not cover it and anyone can rewrite it.</item>
/// </list>
/// </summary>
public static class OpenPgpSignatureReader
{
    public const int MaxBytes = 1024 * 1024;
    public const int MaxListedPackets = 16;

    private const byte SignaturePacketTag = 2;
    private const int MinFirstPartialLength = 512;
    private const string ArmorBegin = "-----BEGIN PGP SIGNATURE-----";
    private const string ArmorEnd = "-----END PGP SIGNATURE-----";
    private const uint Crc24Init = 0xB704CE;
    private const uint Crc24Poly = 0x1864CFB;

    /// <summary>Reads the file's bytes: an armored text block when it starts with the armor header, otherwise binary packets.</summary>
    public static OpenPgpSignatureReport Read(byte[] file)
    {
        if (file.Length == 0)
        {
            return Fail("the file is empty");
        }

        if (file.Length > MaxBytes)
        {
            return Fail("the file is larger than 1 MiB");
        }

        // A UTF-8 byte-order mark (editors add one) is skipped before the armor check; a binary file starts with a high-bit byte.
        int start = file.Length >= 3 && file[0] == 0xEF && file[1] == 0xBB && file[2] == 0xBF ? 3 : 0;
        if (file.Length <= start)
        {
            return Fail("the file is empty");
        }

        if (start == 0 && (file[0] & 0x80) != 0)
        {
            return ReadPackets(file);
        }

        string text = Encoding.Latin1.GetString(file, start, file.Length - start).TrimStart();
        return text.StartsWith(ArmorBegin, StringComparison.Ordinal)
            ? ReadArmor(text)
            : Fail("the file is not an OpenPGP signature");
    }

    /// <summary>The name of a public-key algorithm ID (RFC 9580 section 9.1), or the number when it is not in the table.</summary>
    public static string PublicKeyName(int id) => id switch
    {
        1 or 2 or 3 => "RSA",
        16 => "Elgamal",
        17 => "DSA",
        18 => "ECDH",
        19 => "ECDSA",
        22 => "EdDSA",
        25 => "X25519",
        26 => "X448",
        27 => "Ed25519",
        28 => "Ed448",
        _ => id.ToString(CultureInfo.InvariantCulture),
    };

    /// <summary>The name of a hash algorithm ID (RFC 9580 section 9.4), or the number when it is not in the table.</summary>
    public static string HashName(int id) => id switch
    {
        1 => "MD5",
        2 => "SHA-1",
        3 => "RIPEMD-160",
        8 => "SHA-256",
        9 => "SHA-384",
        10 => "SHA-512",
        11 => "SHA-224",
        12 => "SHA3-256",
        14 => "SHA3-512",
        _ => id.ToString(CultureInfo.InvariantCulture),
    };

    private static OpenPgpSignatureReport Fail(string reason) => new(false, reason, [], 0);

    // ---- armor ----

    private static OpenPgpSignatureReport ReadArmor(string text)
    {
        string[] lines = text.Split('\n');
        int index = 1;

        // Armor headers run to the first blank line; an armor with no headers still has that blank line.
        while (index < lines.Length && lines[index].Trim().Length > 0)
        {
            index++;
        }

        index++;

        var body = new StringBuilder();
        string? checksum = null;
        bool closed = false;
        for (; index < lines.Length; index++)
        {
            string line = lines[index].Trim();
            if (line == ArmorEnd)
            {
                closed = true;
                break;
            }

            if (line.Length == 5 && line[0] == '=')
            {
                checksum = line[1..];
            }
            else if (line.Length > 0)
            {
                body.Append(line);
            }
        }

        if (!closed)
        {
            return Fail("the armor has no END line");
        }

        byte[] data;
        try
        {
            data = Convert.FromBase64String(body.ToString());
        }
        catch (FormatException)
        {
            return Fail("the armor body is not base64");
        }

        if (checksum is not null && !ChecksumMatches(data, checksum))
        {
            return Fail("the armor checksum does not match");
        }

        return ReadPackets(data);
    }

    /// <summary>The CRC-24 of RFC 9580 section 6.1 over the decoded bytes, compared with the armor's <c>=XXXX</c> line.</summary>
    private static bool ChecksumMatches(byte[] data, string encoded)
    {
        byte[] expected;
        try
        {
            expected = Convert.FromBase64String(encoded);
        }
        catch (FormatException)
        {
            return false;
        }

        if (expected.Length != 3)
        {
            return false;
        }

        uint crc = Crc24Init;
        foreach (byte value in data)
        {
            crc ^= (uint)value << 16;
            for (int bit = 0; bit < 8; bit++)
            {
                crc <<= 1;
                if ((crc & 0x1000000) != 0)
                {
                    crc ^= Crc24Poly;
                }
            }
        }

        crc &= 0xFFFFFF;
        return expected[0] == (byte)(crc >> 16) && expected[1] == (byte)(crc >> 8) && expected[2] == (byte)crc;
    }

    // ---- packets ----

    private static OpenPgpSignatureReport ReadPackets(byte[] data)
    {
        var listed = new List<OpenPgpSignaturePacket>();
        int signatures = 0;
        int position = 0;
        while (position < data.Length)
        {
            string? error = ReadPacket(data, ref position, out int tag, out byte[] body);
            if (error is not null)
            {
                return Fail(error);
            }

            if (tag != SignaturePacketTag)
            {
                continue;
            }

            signatures++;
            if (listed.Count < MaxListedPackets)
            {
                OpenPgpSignaturePacket? packet = ParseSignature(body, out string? reason);
                if (packet is null)
                {
                    return Fail(reason ?? "the signature packet cannot be read");
                }

                listed.Add(packet);
            }
        }

        return signatures == 0 ? Fail("no signature packet") : new(true, string.Empty, listed, signatures);
    }

    /// <summary>Reads one packet's header and body. Returns the reason it cannot be read, or null when it can.</summary>
    private static string? ReadPacket(byte[] data, ref int position, out int tag, out byte[] body)
    {
        tag = 0;
        body = [];
        byte header = data[position++];
        if ((header & 0x80) == 0)
        {
            return "a packet header has no high bit";
        }

        if ((header & 0x40) != 0)
        {
            // New format: the length may be split into partial chunks, each a power of two, until a final length.
            tag = header & 0x3F;
            using MemoryStream chunks = new();
            while (true)
            {
                if (position >= data.Length)
                {
                    return "a packet is cut short";
                }

                byte first = data[position++];
                long length;
                bool partial = false;
                if (first < 192)
                {
                    length = first;
                }
                else if (first < 224)
                {
                    if (position >= data.Length)
                    {
                        return "a packet is cut short";
                    }

                    length = ((first - 192) << 8) + data[position++] + 192;
                }
                else if (first == 255)
                {
                    if (position + 4 > data.Length)
                    {
                        return "a packet is cut short";
                    }

                    length = BigEndian32(data, position);
                    position += 4;
                }
                else
                {
                    partial = true;
                    length = 1L << (first & 0x1F);
                    if (chunks.Length == 0 && length < MinFirstPartialLength)
                    {
                        return "the first partial length is under 512 octets";
                    }
                }

                if (position + length > data.Length)
                {
                    return "a packet is cut short";
                }

                chunks.Write(data, position, (int)length);
                position += (int)length;
                if (!partial)
                {
                    break;
                }
            }

            body = chunks.ToArray();
            return null;
        }

        // Old format: the tag is in bits 2 to 5, and the length type in bits 0 and 1. Type 3 runs to the end of the input.
        tag = (header >> 2) & 0x0F;
        long oldLength;
        switch (header & 0x03)
        {
            case 0:
                if (position >= data.Length)
                {
                    return "a packet is cut short";
                }

                oldLength = data[position++];
                break;
            case 1:
                if (position + 2 > data.Length)
                {
                    return "a packet is cut short";
                }

                oldLength = (data[position] << 8) | data[position + 1];
                position += 2;
                break;
            case 2:
                if (position + 4 > data.Length)
                {
                    return "a packet is cut short";
                }

                oldLength = BigEndian32(data, position);
                position += 4;
                break;
            default:
                oldLength = data.Length - position;
                break;
        }

        if (position + oldLength > data.Length)
        {
            return "a packet is cut short";
        }

        body = data[position..(position + (int)oldLength)];
        position += (int)oldLength;
        return null;
    }

    // ---- signature packets ----

    private static OpenPgpSignaturePacket? ParseSignature(byte[] body, out string? reason)
    {
        reason = null;
        if (body.Length == 0)
        {
            reason = "the signature packet is empty";
            return null;
        }

        return body[0] switch
        {
            3 => ParseVersion3(body, out reason),
            4 or 6 => ParseVersion4Or6(body, out reason),
            _ => Unsupported(body[0], out reason),
        };
    }

    private static OpenPgpSignaturePacket? Unsupported(byte version, out string? reason)
    {
        reason = $"signature version {version} is not read";
        return null;
    }

    /// <summary>Version 3: the creation time and the 8-octet key ID are fixed fields, with no subpackets.</summary>
    private static OpenPgpSignaturePacket? ParseVersion3(byte[] body, out string? reason)
    {
        reason = null;
        if (body.Length < 19)
        {
            reason = "a version 3 signature is cut short";
            return null;
        }

        if (body[1] != 5)
        {
            reason = "a version 3 signature has a hashed length other than 5";
            return null;
        }

        return new OpenPgpSignaturePacket(
            3,
            body[2],
            body[15],
            body[16],
            DateTimeOffset.FromUnixTimeSeconds(BigEndian32(body, 3)),
            Convert.ToHexString(body.AsSpan(7, 8)),
            null,
            false);
    }

    /// <summary>Versions 4 and 6: the layouts differ in the width of the subpacket lengths and, for version 6, in the salt.</summary>
    private static OpenPgpSignaturePacket? ParseVersion4Or6(byte[] body, out string? reason)
    {
        reason = null;
        byte version = body[0];
        bool v6 = version == 6;
        int lengthWidth = v6 ? 4 : 2;
        int position = 4;

        if (body.Length < position + lengthWidth)
        {
            reason = "a signature is cut short";
            return null;
        }

        long hashedLength = ReadLength(body, position, lengthWidth);
        position += lengthWidth;
        if (position + hashedLength > body.Length)
        {
            reason = "a signature is cut short";
            return null;
        }

        int hashedStart = position;
        position += (int)hashedLength;

        if (body.Length < position + lengthWidth)
        {
            reason = "a signature is cut short";
            return null;
        }

        long unhashedLength = ReadLength(body, position, lengthWidth);
        position += lengthWidth;
        if (position + unhashedLength > body.Length)
        {
            reason = "a signature is cut short";
            return null;
        }

        int unhashedStart = position;
        position += (int)unhashedLength;

        if (body.Length < position + 2)
        {
            reason = "a signature is cut short";
            return null;
        }

        position += 2;

        if (v6)
        {
            if (position >= body.Length)
            {
                reason = "a version 6 signature is cut short";
                return null;
            }

            int saltLength = body[position++];
            int? expected = V6SaltLength(body[3]);
            if (expected is null)
            {
                reason = $"hash algorithm {HashName(body[3])} is not allowed in a version 6 signature";
                return null;
            }

            if (saltLength != expected)
            {
                reason = "a version 6 salt does not match its hash algorithm";
                return null;
            }

            if (position + saltLength > body.Length)
            {
                reason = "a version 6 signature is cut short";
                return null;
            }
        }

        DateTimeOffset? created = null;
        string? keyId = null;
        string? fingerprint = null;
        bool nonConforming = false;
        // Only the hashed area is covered by the signature. Unhashed subpackets are structure-checked (a malformed one still makes the
        // packet unreadable) but their values are never used, because anyone can rewrite them without breaking the signature.
        foreach ((int start, int length, bool hashed) in new[] { (hashedStart, (int)hashedLength, true), (unhashedStart, (int)unhashedLength, false) })
        {
            int end = start + length;
            int p = start;
            while (p < end)
            {
                if (!TryReadSubpacketLength(body, ref p, end, out long subpacketLength) || subpacketLength < 1 || p + subpacketLength > end)
                {
                    reason = "a subpacket is cut short";
                    return null;
                }

                int type = body[p] & 0x7F;
                int dataStart = p + 1;
                int dataLength = (int)subpacketLength - 1;
                switch (type)
                {
                    case 2:
                        if (dataLength != 4)
                        {
                            reason = "a creation time subpacket has the wrong length";
                            return null;
                        }

                        if (hashed)
                        {
                            created ??= DateTimeOffset.FromUnixTimeSeconds(BigEndian32(body, dataStart));
                        }

                        break;
                    case 16:
                        if (dataLength != 8)
                        {
                            reason = "an issuer key ID subpacket has the wrong length";
                            return null;
                        }

                        if (v6)
                        {
                            nonConforming = true;
                        }
                        else if (hashed)
                        {
                            keyId ??= Convert.ToHexString(body.AsSpan(dataStart, 8));
                        }

                        break;
                    case 33:
                        if (dataLength < 1 || body[dataStart] != version)
                        {
                            reason = "an issuer fingerprint's key version does not match the signature";
                            return null;
                        }

                        int fingerprintLength = v6 ? 32 : 20;
                        if (dataLength - 1 != fingerprintLength)
                        {
                            reason = "an issuer fingerprint has the wrong length";
                            return null;
                        }

                        if (hashed)
                        {
                            fingerprint ??= Convert.ToHexString(body.AsSpan(dataStart + 1, fingerprintLength));
                        }

                        break;
                }

                p += (int)subpacketLength;
            }
        }

        return new OpenPgpSignaturePacket(version, body[1], body[2], body[3], created, keyId, fingerprint, nonConforming);
    }

    /// <summary>The salt size RFC 9580 table 23 gives a version 6 signature, or null when the hash may not be used with version 6.</summary>
    private static int? V6SaltLength(int hash) => hash switch
    {
        8 or 11 or 12 => 16,
        9 => 24,
        10 or 14 => 32,
        _ => null,
    };

    /// <summary>A subpacket length: one octet below 192, two octets from 192 to 254, and four octets for 255 (RFC 9580 section 5.2.3.1).</summary>
    private static bool TryReadSubpacketLength(byte[] body, ref int p, int end, out long length)
    {
        length = 0;
        if (p >= end)
        {
            return false;
        }

        byte first = body[p++];
        if (first < 192)
        {
            length = first;
            return true;
        }

        if (first < 255)
        {
            if (p >= end)
            {
                return false;
            }

            length = ((first - 192) << 8) + body[p++] + 192;
            return true;
        }

        if (p + 4 > end)
        {
            return false;
        }

        length = BigEndian32(body, p);
        p += 4;
        return true;
    }

    private static long ReadLength(byte[] body, int position, int width) =>
        width == 2 ? (body[position] << 8) | body[position + 1] : BigEndian32(body, position);

    private static long BigEndian32(byte[] data, int position) =>
        ((long)data[position] << 24) | ((long)data[position + 1] << 16) | ((long)data[position + 2] << 8) | data[position + 3];
}
