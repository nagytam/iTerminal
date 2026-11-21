using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace iConsole.Formats.Pdb;

/// <summary>
/// The summary of a Windows PDB, an MSF 7.00 container: the superblock's block size and count, the stream directory, and the
/// PDB info stream (version, timestamp, age, GUID and the names of the named streams). Only the directory and that one stream
/// are read; the streams themselves are not interpreted.
/// </summary>
/// <remarks>
/// The layout follows the published MSF description. Every index is checked against the file's length, so a truncated or
/// damaged container is refused with <see cref="FormatException"/> rather than read past its end.
/// </remarks>
internal static class MsfPdbReader
{
    private const int SuperblockLength = 56;
    private const uint NilStream = 0xFFFFFFFF;
    private const uint PdbInfoStream = 1;
    private const int MinBlockSize = 512;
    private const int MaxBlockSize = 65536;

    public static IReadOnlyList<string> Summarize(byte[] content)
    {
        if (content.Length < SuperblockLength)
        {
            throw new FormatException("the MSF superblock is cut short");
        }

        uint blockSize = BinaryPrimitives.ReadUInt32LittleEndian(content.AsSpan(32));
        uint numBlocks = BinaryPrimitives.ReadUInt32LittleEndian(content.AsSpan(40));
        uint numDirectoryBytes = BinaryPrimitives.ReadUInt32LittleEndian(content.AsSpan(44));
        uint blockMapAddress = BinaryPrimitives.ReadUInt32LittleEndian(content.AsSpan(52));

        if (blockSize < MinBlockSize || blockSize > MaxBlockSize || (blockSize & (blockSize - 1)) != 0)
        {
            throw new FormatException("the MSF block size is not a power of two in range");
        }

        if ((long)numBlocks * blockSize > content.Length)
        {
            throw new FormatException("the MSF container is shorter than its superblock says");
        }

        var container = new Container(content, (int)blockSize);
        byte[] directory = container.ReadDirectory(numDirectoryBytes, blockMapAddress);
        IReadOnlyList<uint[]> streams = ReadStreamBlocks(directory, container);
        byte[] pdbInfo = container.ReadStream(streams, PdbInfoStream);

        return Lines(content.Length, blockSize, numBlocks, streams, pdbInfo);
    }

    /// <summary>The block-list of every stream, read from the directory's bytes.</summary>
    private static IReadOnlyList<uint[]> ReadStreamBlocks(byte[] directory, Container container)
    {
        if (directory.Length < 4)
        {
            throw new FormatException("the MSF stream directory is cut short");
        }

        uint count = BinaryPrimitives.ReadUInt32LittleEndian(directory);
        if (count > container.BlockCount * 2)
        {
            throw new FormatException("the MSF stream directory claims more streams than it can hold");
        }

        var sizes = new uint[count];
        int at = 4;
        for (int i = 0; i < count; i++)
        {
            sizes[i] = Read(directory, ref at);
        }

        var streams = new uint[count][];
        for (int i = 0; i < count; i++)
        {
            int blocks = sizes[i] == NilStream ? 0 : container.BlocksFor(sizes[i]);
            streams[i] = new uint[blocks];
            for (int b = 0; b < blocks; b++)
            {
                streams[i][b] = Read(directory, ref at);
            }
        }

        return streams;
    }

    private static uint Read(byte[] bytes, ref int at)
    {
        if (at + 4 > bytes.Length)
        {
            throw new FormatException("the MSF stream directory is cut short");
        }

        uint value = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at));
        at += 4;
        return value;
    }

    private static List<string> Lines(int fileLength, uint blockSize, uint numBlocks, IReadOnlyList<uint[]> streams, byte[] pdbInfo)
    {
        var lines = new List<string> { "Windows debug symbols (PDB, MSF 7.00)" };
        lines.Add($"Container: {numBlocks} blocks of {blockSize} bytes ({fileLength:N0} bytes)");

        int largestIndex = 0;
        for (int i = 1; i < streams.Count; i++)
        {
            if (streams[i].Length > streams[largestIndex].Length)
            {
                largestIndex = i;
            }
        }

        lines.Add(streams.Count == 0
            ? "Streams: 0"
            : $"Streams: {streams.Count}, the largest is stream {largestIndex} ({streams[largestIndex].Length} blocks)");

        if (pdbInfo.Length < 28)
        {
            throw new FormatException("the PDB info stream is cut short");
        }

        uint version = BinaryPrimitives.ReadUInt32LittleEndian(pdbInfo);
        uint signature = BinaryPrimitives.ReadUInt32LittleEndian(pdbInfo.AsSpan(4));
        uint age = BinaryPrimitives.ReadUInt32LittleEndian(pdbInfo.AsSpan(8));
        var guid = new Guid(pdbInfo.AsSpan(12, 16));

        lines.Add($"Format: {VersionName(version)}");
        lines.Add($"Timestamp: {DateTimeOffset.FromUnixTimeSeconds(signature):yyyy-MM-dd HH:mm:ss} UTC");
        lines.Add($"Age: {age}");
        lines.Add($"GUID: {guid:D}");

        string[] names = NamedStreams(pdbInfo);
        lines.Add(names.Length == 0 ? "Named streams: none" : $"Named streams: {string.Join(", ", names)}");
        return lines;
    }

    /// <summary>The names table after the GUID: a byte count, then null-terminated names. Empty names are skipped.</summary>
    private static string[] NamedStreams(byte[] pdbInfo)
    {
        if (pdbInfo.Length < 32)
        {
            return [];
        }

        uint nameBytes = BinaryPrimitives.ReadUInt32LittleEndian(pdbInfo.AsSpan(28));
        if (nameBytes > pdbInfo.Length - 32)
        {
            throw new FormatException("the PDB names table is cut short");
        }

        return Encoding.UTF8
            .GetString(pdbInfo, 32, (int)nameBytes)
            .Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }

    private static string VersionName(uint version) => version switch
    {
        20000404 => "VC70 (20000404)",
        19990604 => "VC70 pre-release (19990604)",
        _ => version.ToString(CultureInfo.InvariantCulture),
    };

    /// <summary>The blocks of the file, each read in range.</summary>
    private sealed class Container(byte[] content, int blockSize)
    {
        public uint BlockCount { get; } = (uint)(content.Length / blockSize);

        public int BlocksFor(uint bytes) => (int)(((long)bytes + blockSize - 1) / blockSize);

        /// <summary>The directory: its bytes are the blocks named by the block map, which lists the directory's blocks.</summary>
        public byte[] ReadDirectory(uint numDirectoryBytes, uint blockMapAddress)
        {
            int blocks = BlocksFor(numDirectoryBytes);
            if ((long)blocks * 4 > blockSize)
            {
                throw new FormatException("the MSF block map does not fit in one block");
            }

            var directory = new byte[(long)blocks * blockSize];
            for (int i = 0; i < blocks; i++)
            {
                uint block = BinaryPrimitives.ReadUInt32LittleEndian(Block(blockMapAddress).Slice(i * 4));
                Block(block).CopyTo(directory.AsSpan(i * blockSize));
            }

            if (numDirectoryBytes > directory.Length)
            {
                throw new FormatException("the MSF directory is longer than its blocks");
            }

            return directory[..(int)numDirectoryBytes];
        }

        /// <summary>One stream, assembled from its blocks and cut to its size.</summary>
        public byte[] ReadStream(IReadOnlyList<uint[]> streams, uint index)
        {
            if (index >= streams.Count)
            {
                throw new FormatException("the MSF container has no PDB info stream");
            }

            uint[] blocks = streams[(int)index];
            var bytes = new byte[(long)blocks.Length * blockSize];
            for (int i = 0; i < blocks.Length; i++)
            {
                Block(blocks[i]).CopyTo(bytes.AsSpan(i * blockSize));
            }

            return bytes;
        }

        private ReadOnlySpan<byte> Block(uint index)
        {
            if (index >= BlockCount)
            {
                throw new FormatException("an MSF block index is past the end of the file");
            }

            return content.AsSpan((int)(index * (uint)blockSize), blockSize);
        }
    }
}
