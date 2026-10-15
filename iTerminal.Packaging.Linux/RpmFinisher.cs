using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

/// <summary>
/// Finishes an RPM written by Packaging.Targets so that a current rpm accepts it, and so that it
/// installs on more than one RPM distribution.
///
/// <para><b>Digests.</b> The tool writes a signature header holding two placeholder signatures, a
/// SHA-1 and an MD5, and no digest of the payload. rpm 6 (Fedora 44) finds no usable digest in that
/// and refuses the package outright: "does not verify: no digest". This puts the payload's SHA-256
/// into the main header (PAYLOADDIGEST) and writes a new signature header holding the header's SHA-1
/// and SHA-256, the MD5 and the sizes — and no signatures, since there is no key to sign with.</para>
///
/// <para><b>Dependencies.</b> The tool's are Fedora package names (openssl-libs, libicu, krb5-libs),
/// which openSUSE does not have, so zypper refuses the package. <see cref="Requires"/> replaces
/// them, and is meant to be shared-library names (libssl.so.3()(64bit)), which every RPM
/// distribution's packages provide whatever they are called. One that starts with "(" is a rich
/// dependency — "(a or b)" — and gets the rpmlib requirement that says so.</para>
///
/// <para>The rest of the header — the file list, the scripts — and the payload are kept byte for
/// byte. Inline-task code is compiled against .NET Standard 2.0, which is why it reads like it.</para>
/// </summary>
public class RpmFinisher : Task
{
    [Required]
    public string RpmPath { get; set; }

    /// <summary>Replaces every dependency the tool wrote other than rpmlib(...), rtld(...) and file paths.</summary>
    public ITaskItem[] Requires { get; set; }

    private static readonly byte[] Magic = { 0x8e, 0xad, 0xe8, 0x01, 0, 0, 0, 0 };
    private const int LeadSize = 96;

    private const int Int32Type = 4, StringType = 6, BinType = 7, StringArrayType = 8;
    private const int RegionSignatures = 62, RegionImmutable = 63;
    private const int RequireFlags = 1048, RequireName = 1049, RequireVersion = 1050;
    private const int PayloadDigest = 5092, PayloadDigestAlgorithm = 5093;
    private const int SigSize = 1000, SigMd5 = 1004, SigPayloadSize = 1007, SigSha1 = 269, SigSha256 = 273;
    private const uint SenseLess = 0x02, SenseEqual = 0x08, SenseRpmlib = 1u << 24;
    private const int Sha256Algorithm = 8;

    private sealed class Entry
    {
        public Entry(int tag, int type, byte[] data, int count)
        {
            Tag = tag;
            Type = type;
            Data = data;
            Count = count;
        }

        public int Tag { get; }
        public int Type { get; }
        public byte[] Data { get; }
        public int Count { get; }
    }

    public override bool Execute()
    {
        var data = File.ReadAllBytes(RpmPath);
        int signatureEnd;
        var signature = ReadHeader(data, LeadSize, out signatureEnd);
        var headerStart = signatureEnd + (8 - signatureEnd % 8) % 8;
        int headerEnd;
        var header = ReadHeader(data, headerStart, out headerEnd);
        var payloadLength = data.Length - headerEnd;

        var requires = (Requires ?? new ITaskItem[0]).Select(item => item.ItemSpec).ToList();
        if (requires.Count > 0)
        {
            header = ReplaceRequires(header, requires);
        }

        byte[] payloadSha256;
        using (var sha256 = SHA256.Create())
        {
            payloadSha256 = sha256.ComputeHash(data, headerEnd, payloadLength);
        }

        header.RemoveAll(entry => entry.Tag == PayloadDigest || entry.Tag == PayloadDigestAlgorithm);
        header.Add(new Entry(PayloadDigest, StringArrayType, Terminated(Hex(payloadSha256)), 1));
        header.Add(new Entry(PayloadDigestAlgorithm, Int32Type, BigEndian(Sha256Algorithm), 1));
        var headerBytes = WriteHeader(header, RegionImmutable);

        byte[] headerSha1, headerSha256, md5Digest;
        using (var sha1 = SHA1.Create())
        {
            headerSha1 = sha1.ComputeHash(headerBytes);
        }
        using (var sha256 = SHA256.Create())
        {
            headerSha256 = sha256.ComputeHash(headerBytes);
        }
        using (var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5))
        {
            md5.AppendData(headerBytes);
            md5.AppendData(data, headerEnd, payloadLength);
            md5Digest = md5.GetHashAndReset();
        }

        var payloadSize = signature.First(entry => entry.Tag == SigPayloadSize).Data;
        var signatureBytes = WriteHeader(new List<Entry>
        {
            new Entry(SigSha1, StringType, Terminated(Hex(headerSha1)), 1),
            new Entry(SigSha256, StringType, Terminated(Hex(headerSha256)), 1),
            new Entry(SigSize, Int32Type, BigEndian(headerBytes.Length + payloadLength), 1),
            new Entry(SigMd5, BinType, md5Digest, 16),
            new Entry(SigPayloadSize, Int32Type, payloadSize, 1),
        }, RegionSignatures);

        var temporary = RpmPath + ".tmp";
        using (var output = File.Create(temporary))
        {
            output.Write(data, 0, LeadSize);
            output.Write(signatureBytes, 0, signatureBytes.Length);
            var padding = (8 - signatureBytes.Length % 8) % 8;
            output.Write(new byte[padding], 0, padding);
            output.Write(headerBytes, 0, headerBytes.Length);
            output.Write(data, headerEnd, payloadLength);
        }

        File.Delete(RpmPath);
        File.Move(temporary, RpmPath);
        Log.LogMessage(MessageImportance.High, "Finished " + Path.GetFileName(RpmPath) + ": SHA-256 digests, " + requires.Count + " dependencies");
        return !Log.HasLoggedErrors;
    }

    private static List<Entry> ReplaceRequires(List<Entry> header, List<string> requires)
    {
        var names = Strings(header.First(entry => entry.Tag == RequireName));
        var versions = Strings(header.First(entry => entry.Tag == RequireVersion));
        var flagsData = header.First(entry => entry.Tag == RequireFlags).Data;

        // Kept: what rpm itself and the package's scripts need (rpmlib(...), /sbin/ldconfig, rtld(...)).
        var keptNames = new List<string>();
        var keptVersions = new List<string>();
        var keptFlags = new List<uint>();
        for (var i = 0; i < names.Count; i++)
        {
            if (names[i].StartsWith("rpmlib(", StringComparison.Ordinal) || names[i].StartsWith("/", StringComparison.Ordinal) || names[i].StartsWith("rtld(", StringComparison.Ordinal))
            {
                keptNames.Add(names[i]);
                keptVersions.Add(versions[i]);
                keptFlags.Add(ReadUInt32(flagsData, i * 4));
            }
        }

        foreach (var name in requires)
        {
            keptNames.Add(name);
            keptVersions.Add(string.Empty);
            keptFlags.Add(0);
        }

        if (requires.Any(name => name.StartsWith("(", StringComparison.Ordinal)) && !keptNames.Contains("rpmlib(RichDependencies)"))
        {
            keptNames.Add("rpmlib(RichDependencies)");
            keptVersions.Add("4.12.0-1");
            keptFlags.Add(SenseRpmlib | SenseLess | SenseEqual);
        }

        var result = header.Where(entry => entry.Tag != RequireName && entry.Tag != RequireVersion && entry.Tag != RequireFlags).ToList();
        result.Add(new Entry(RequireName, StringArrayType, StringArray(keptNames), keptNames.Count));
        result.Add(new Entry(RequireVersion, StringArrayType, StringArray(keptVersions), keptVersions.Count));
        result.Add(new Entry(RequireFlags, Int32Type, keptFlags.SelectMany(flag => BigEndian((int)flag)).ToArray(), keptFlags.Count));
        return result;
    }

    /// <summary>The entries of the header at <paramref name="offset"/>, each with its data cut out of the store.</summary>
    private static List<Entry> ReadHeader(byte[] data, int offset, out int end)
    {
        for (var i = 0; i < Magic.Length; i++)
        {
            if (data[offset + i] != Magic[i])
            {
                throw new InvalidDataException("No RPM header at offset " + offset + ".");
            }
        }

        var count = (int)ReadUInt32(data, offset + 8);
        var size = (int)ReadUInt32(data, offset + 12);
        var store = offset + 16 + count * 16;
        var entries = new List<Entry>(count);
        for (var i = 0; i < count; i++)
        {
            var at = offset + 16 + i * 16;
            var tag = (int)ReadUInt32(data, at);
            var type = (int)ReadUInt32(data, at + 4);
            var start = store + (int)ReadUInt32(data, at + 8);
            var n = (int)ReadUInt32(data, at + 12);
            int length;
            switch (type)
            {
                case 1: case 2: case BinType: length = n; break;
                case 3: length = 2 * n; break;
                case Int32Type: length = 4 * n; break;
                case 5: length = 8 * n; break;
                case StringType: length = Array.IndexOf(data, (byte)0, start) + 1 - start; break;
                case StringArrayType: case 9: length = StringsLength(data, start, n); break;
                default: length = 0; break;
            }

            var copy = new byte[length];
            Array.Copy(data, start, copy, 0, length);
            entries.Add(new Entry(tag, type, copy, n));
        }

        end = store + size;
        return entries;
    }

    /// <summary>
    /// A header whose entries are all inside one region: the region tag first in the index, the rest
    /// in tag order, each entry's data aligned to its type, the region's trailer last in the store.
    /// </summary>
    private static byte[] WriteHeader(IEnumerable<Entry> entries, int regionTag)
    {
        var sorted = entries.Where(entry => entry.Tag != regionTag).OrderBy(entry => entry.Tag).ToList();
        var store = new MemoryStream();
        var index = new MemoryStream();
        foreach (var entry in sorted)
        {
            var alignment = entry.Type == 3 ? 2 : entry.Type == Int32Type ? 4 : entry.Type == 5 ? 8 : 1;
            while (store.Length % alignment != 0)
            {
                store.WriteByte(0);
            }

            Write(index, IndexEntry(entry.Tag, entry.Type, (int)store.Length, entry.Count));
            Write(store, entry.Data);
        }

        var count = sorted.Count + 1;
        var trailerOffset = (int)store.Length;
        Write(store, IndexEntry(regionTag, BinType, -(count * 16), 16));

        var header = new MemoryStream();
        Write(header, Magic);
        Write(header, BigEndian(count));
        Write(header, BigEndian((int)store.Length));
        Write(header, IndexEntry(regionTag, BinType, trailerOffset, 16));
        Write(header, index.ToArray());
        Write(header, store.ToArray());
        return header.ToArray();
    }

    private static void Write(Stream stream, byte[] bytes) => stream.Write(bytes, 0, bytes.Length);

    private static byte[] IndexEntry(int tag, int type, int offset, int count) =>
        BigEndian(tag).Concat(BigEndian(type)).Concat(BigEndian(offset)).Concat(BigEndian(count)).ToArray();

    private static int StringsLength(byte[] data, int start, int count)
    {
        var end = start;
        for (var i = 0; i < count; i++)
        {
            end = Array.IndexOf(data, (byte)0, end) + 1;
        }

        return end - start;
    }

    private static List<string> Strings(Entry entry) =>
        Encoding.UTF8.GetString(entry.Data).Split('\0').Take(entry.Count).ToList();

    private static byte[] StringArray(IEnumerable<string> values) =>
        values.SelectMany(Terminated).ToArray();

    private static byte[] Terminated(string value) => Encoding.UTF8.GetBytes(value).Concat(new byte[] { 0 }).ToArray();

    private static string Hex(byte[] bytes) => BitConverter.ToString(bytes).Replace("-", string.Empty).ToLowerInvariant();

    private static byte[] BigEndian(int value) =>
        new[] { (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value };

    private static uint ReadUInt32(byte[] data, int offset) =>
        (uint)(data[offset] << 24 | data[offset + 1] << 16 | data[offset + 2] << 8 | data[offset + 3]);
}
