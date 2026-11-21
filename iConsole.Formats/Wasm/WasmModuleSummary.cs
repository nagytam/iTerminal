using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using iConsole.Services;
using iConsole.Formats.Services;

namespace iConsole.Formats.Wasm;

/// <summary>
/// The summary the F3 viewer shows for a WebAssembly module: its version, sections, imports, exports and counts, read
/// from the bytes without compiling, instantiating or running it. A module is binary, so its bytes are never shown as text.
/// </summary>
public static class WasmModuleSummary
{
    /// <summary>The extensions the viewer summarizes.</summary>
    public static readonly IReadOnlySet<string> Extensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "wasm" };

    private static readonly byte[] Magic = [0x00, 0x61, 0x73, 0x6D];

    private const int MaxListed = 50;

    private const byte FunctionKind = 0;
    private const byte TableKind = 1;
    private const byte MemoryKind = 2;
    private const byte GlobalKind = 3;

    /// <summary>The lines to show. Throws <see cref="FormatException"/> when the bytes are not a WebAssembly module.</summary>
    public static IReadOnlyList<string> Summarize(byte[] content)
    {
        if (content.Length < 8 || !content.AsSpan(0, 4).SequenceEqual(Magic))
        {
            throw new FormatException("the file does not start with the WebAssembly header");
        }

        uint version = BinaryPrimitives.ReadUInt32LittleEndian(content.AsSpan(4));
        var module = new Module();
        var cursor = new Cursor(content, 8, content.Length);
        while (!cursor.AtEnd)
        {
            byte id = cursor.Byte();
            uint size = cursor.U32();
            Cursor body = cursor.Slice((int)size);
            module.Sections++;
            Read(id, body, module);
        }

        return Lines(version, module);
    }

    /// <summary>Reads one section into the summary. Sections the summary does not count are skipped by their size.</summary>
    private static void Read(byte id, Cursor body, Module module)
    {
        switch (id)
        {
            case 0:
                module.Custom.Add(body.Name());
                break;
            case 1:
                module.Types = (int)body.U32();
                break;
            case 2:
                ReadImports(body, module);
                break;
            case 3:
                module.DefinedFunctions = (int)body.U32();
                break;
            case 4:
                module.Tables = (int)body.U32();
                break;
            case 5:
                module.Memories = (int)body.U32();
                break;
            case 6:
                module.Globals = (int)body.U32();
                break;
            case 7:
                ReadExports(body, module);
                break;
            case 8:
                module.Start = (int)body.U32();
                break;
            case 9:
            case 10:
            case 12:
                break;
            case 11:
                module.DataSegments = (int)body.U32();
                break;
            default:
                throw new FormatException($"unknown section {id}");
        }
    }

    private static void ReadImports(Cursor body, Module module)
    {
        uint count = body.U32();
        for (uint i = 0; i < count; i++)
        {
            string name = body.Name() + "." + body.Name();
            byte kind = body.Byte();
            switch (kind)
            {
                case FunctionKind:
                    body.U32();
                    module.ImportedFunctions++;
                    break;
                case TableKind:
                    body.Byte();
                    body.Limits();
                    break;
                case MemoryKind:
                    body.Limits();
                    break;
                case GlobalKind:
                    body.Byte();
                    body.Byte();
                    break;
                default:
                    throw new FormatException($"unknown import kind {kind}");
            }

            module.Imports.Add((name, kind));
        }
    }

    private static void ReadExports(Cursor body, Module module)
    {
        uint count = body.U32();
        for (uint i = 0; i < count; i++)
        {
            string name = body.Name();
            byte kind = body.Byte();
            body.U32();
            module.Exports.Add((name, kind));
        }
    }

    private static List<string> Lines(uint version, Module module)
    {
        var lines = new List<string> { Label("Heading", "WebAssembly module") };
        lines.Add($"{Label("Version", "Version")}: {version.ToString(CultureInfo.InvariantCulture)}");
        lines.Add($"{Label("Sections", "Sections")}: {module.Sections}");
        lines.Add($"{Label("Types", "Types")}: {module.Types}");

        lines.Add($"{Label("Imports", "Imports")}: {module.Imports.Count}");
        AddListed(lines, module.Imports);

        lines.Add($"{Label("Functions", "Functions (defined plus imported)")}: {module.DefinedFunctions + module.ImportedFunctions}");
        lines.Add($"{Label("Tables", "Tables")}: {module.Tables}");
        lines.Add($"{Label("Memories", "Memories")}: {module.Memories}");
        lines.Add($"{Label("Globals", "Globals")}: {module.Globals}");

        lines.Add($"{Label("Exports", "Exports")}: {module.Exports.Count}");
        AddListed(lines, module.Exports);

        lines.Add($"{Label("Start", "Start function")}: {(module.Start is { } start ? start.ToString(CultureInfo.InvariantCulture) : Label("None", "none"))}");
        lines.Add($"{Label("DataSegments", "Data segments")}: {module.DataSegments}");
        lines.Add($"{Label("Custom", "Custom sections")}: {(module.Custom.Count == 0 ? Label("None", "none") : string.Join(", ", module.Custom))}");
        return lines;
    }

    private static void AddListed(List<string> lines, List<(string Name, byte Kind)> entries)
    {
        foreach (var (name, kind) in entries.Take(MaxListed))
        {
            lines.Add($"  {name} ({KindName(kind)})");
        }

        if (entries.Count > MaxListed)
        {
            lines.Add("  …");
        }
    }

    private static string KindName(byte kind) => kind switch
    {
        FunctionKind => Label("Kind.function", "function"),
        TableKind => Label("Kind.table", "table"),
        MemoryKind => Label("Kind.memory", "memory"),
        GlobalKind => Label("Kind.global", "global"),
        _ => kind.ToString(CultureInfo.InvariantCulture),
    };

    private static string Label(string key, string fallback) => ViewerText.Get("Wasm." + key, fallback);

    /// <summary>What the module declares, counted as the sections are read.</summary>
    private sealed class Module
    {
        public int Sections;
        public int Types;
        public int DefinedFunctions;
        public int ImportedFunctions;
        public int Tables;
        public int Memories;
        public int Globals;
        public int DataSegments;
        public int? Start;
        public readonly List<(string Name, byte Kind)> Imports = [];
        public readonly List<(string Name, byte Kind)> Exports = [];
        public readonly List<string> Custom = [];
    }

    /// <summary>A bounds-checked reader over one range of the module. Every read past the end is a <see cref="FormatException"/>.</summary>
    private sealed class Cursor(byte[] bytes, int position, int end)
    {
        private int _position = position;

        public bool AtEnd => _position >= end;

        public byte Byte()
        {
            if (_position >= end)
            {
                throw new FormatException("the module ends inside a section");
            }

            return bytes[_position++];
        }

        /// <summary>An unsigned LEB128 integer of at most 32 bits.</summary>
        public uint U32()
        {
            uint result = 0;
            for (int shift = 0; shift < 35; shift += 7)
            {
                byte b = Byte();
                result |= (uint)(b & 0x7F) << shift;
                if ((b & 0x80) == 0)
                {
                    return result;
                }
            }

            throw new FormatException("a number in the module is too long");
        }

        public string Name()
        {
            uint length = U32();
            if (length > (uint)(end - _position))
            {
                throw new FormatException("a name in the module runs past its section");
            }

            string text = Encoding.UTF8.GetString(bytes, _position, (int)length);
            _position += (int)length;
            return text;
        }

        /// <summary>A limits record: a flag, a minimum and, when the flag says so, a maximum.</summary>
        public void Limits()
        {
            byte flag = Byte();
            U32();
            if ((flag & 1) != 0)
            {
                U32();
            }
        }

        /// <summary>A range of the module of <paramref name="length"/> bytes, which this reader then moves past.</summary>
        public Cursor Slice(int length)
        {
            if (length < 0 || length > end - _position)
            {
                throw new FormatException("a section runs past the end of the module");
            }

            var slice = new Cursor(bytes, _position, _position + length);
            _position += length;
            return slice;
        }
    }
}
