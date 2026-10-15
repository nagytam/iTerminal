using iConsole.Interfaces;

namespace iConsole.Test.Core;

public sealed class MockMemoryFile : IMemoryFile
{
    public MockMemoryFile(byte[] content)
    {
        Content = content;
    }

    public byte[] Content { get; }
}
