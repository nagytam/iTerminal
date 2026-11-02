namespace iConsole.IWork;

/// <summary>One object of an iWork document: the messages (type and protobuf payload) its archive holds under one identifier.</summary>
internal sealed class IwaObject(ulong identifier)
{
    public ulong Identifier { get; } = identifier;
    public List<(uint Type, byte[] Payload)> Messages { get; } = [];

    /// <summary>The payload of the first message of <paramref name="type"/>, or null.</summary>
    public byte[]? Message(uint type)
    {
        foreach ((uint messageType, byte[] payload) in Messages)
        {
            if (messageType == type)
            {
                return payload;
            }
        }

        return null;
    }
}

/// <summary>
/// The object graph of an iWork document, read from its .iwa archives. An .iwa file is a run of Snappy-compressed
/// chunks (a 0x00 byte and a three-byte length, then the block); the decompressed bytes are archives, each a varint
/// length, an ArchiveInfo message naming the object and its messages, then the messages' payloads. Objects refer to one
/// another by identifier, which is how a sheet reaches its tables and a table its strings.
/// </summary>
internal sealed class IwaDocument
{
    private readonly Dictionary<ulong, IwaObject> _objects = [];

    /// <summary>Every object, in the order the archives give them (file by file, then position in the file).</summary>
    public IReadOnlyList<IwaObject> Objects { get; private set; } = [];

    /// <summary>Reads one .iwa file into the document. Objects with the same identifier in later files add to the earlier ones.</summary>
    public void AddArchive(byte[] iwa)
    {
        using MemoryStream plain = new();
        int position = 0;
        while (position < iwa.Length)
        {
            if (iwa[position] != 0 || position + 4 > iwa.Length)
            {
                throw new InvalidDataException("an archive chunk has no header");
            }

            int length = iwa[position + 1] | (iwa[position + 2] << 8) | (iwa[position + 3] << 16);
            position += 4;
            if (position + length > iwa.Length)
            {
                throw new InvalidDataException("an archive chunk runs past the end of its file");
            }

            plain.Write(SnappyDecompressor.Decompress(iwa.AsSpan(position, length)));
            position += length;
        }

        ReadSegments(plain.ToArray());
        Objects = [.. _objects.Values];
    }

    /// <summary>The object with this identifier, or null when the document does not hold it.</summary>
    public IwaObject? Find(ulong identifier) => _objects.GetValueOrDefault(identifier);

    /// <summary>The object a Reference message points at (its field <paramref name="number"/>, as a TSP.Reference), or null.</summary>
    public IwaObject? Follow(List<WireField> fields, int number)
    {
        ReadOnlyMemory<byte>? reference = ProtobufWire.Bytes(fields, number);
        if (reference is not { } bytes)
        {
            return null;
        }

        ulong? identifier = ProtobufWire.Varint(ProtobufWire.Parse(bytes), 1);
        return identifier is { } id ? Find(id) : null;
    }

    private void ReadSegments(byte[] plain)
    {
        int position = 0;
        while (position < plain.Length)
        {
            ulong infoLength = ProtobufWire.ReadVarint(plain, ref position);
            if (infoLength == 0 || infoLength > (ulong)(plain.Length - position))
            {
                throw new InvalidDataException("an archive header is malformed");
            }

            List<WireField> info = ProtobufWire.Parse(plain.AsMemory(position, (int)infoLength));
            position += (int)infoLength;
            ulong identifier = ProtobufWire.Varint(info, 1) ?? 0;
            if (!_objects.TryGetValue(identifier, out IwaObject? owner))
            {
                owner = new IwaObject(identifier);
                _objects[identifier] = owner;
            }

            foreach (ReadOnlyMemory<byte> messageInfo in ProtobufWire.AllBytes(info, 2))
            {
                List<WireField> message = ProtobufWire.Parse(messageInfo);
                uint type = (uint)(ProtobufWire.Varint(message, 1) ?? 0);
                int length = (int)(ProtobufWire.Varint(message, 3) ?? 0);
                if (length < 0 || length > plain.Length - position)
                {
                    throw new InvalidDataException("a message runs past the end of its archive");
                }

                owner.Messages.Add((type, plain[position..(position + length)]));
                position += length;
            }
        }
    }
}
