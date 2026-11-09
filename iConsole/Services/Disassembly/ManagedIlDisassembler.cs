using System.Buffers.Binary;
using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;

namespace iConsole.Services.Disassembly;

/// <summary>
/// Lists the IL of every method body in a managed assembly, with the metadata tokens resolved to names. The assembly is
/// only read as bytes through <see cref="MetadataReader"/>; it is never loaded and nothing in it runs.
/// </summary>
internal static class ManagedIlDisassembler
{
    private const int MaxStringChars = 80;
    private const int MaxSwitchTargetsShown = 16;
    private const int MaxNestingDepth = 16;

    private static readonly OpCode[] OneByteOpCodes = new OpCode[256];
    private static readonly OpCode[] TwoByteOpCodes = new OpCode[256];

    static ManagedIlDisassembler()
    {
        foreach (FieldInfo field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.GetValue(null) is not OpCode opCode)
            {
                continue;
            }

            ushort value = (ushort)opCode.Value;
            if (opCode.Size == 1)
            {
                OneByteOpCodes[value & 0xFF] = opCode;
            }
            else if ((value >> 8) == 0xFE)
            {
                TwoByteOpCodes[value & 0xFF] = opCode;
            }
        }
    }

    /// <summary>
    /// Adds the IL listing to <paramref name="budget"/>. Returns false when the bytes are not a managed assembly, so the
    /// caller can try the native path.
    /// </summary>
    public static bool TryDisassemble(byte[] bytes, DisassemblyBudget budget)
    {
        PEReader? peReader = null;
        MetadataReader metadata;
        try
        {
            peReader = new PEReader(new MemoryStream(bytes, writable: false));
            if (!peReader.HasMetadata)
            {
                peReader.Dispose();
                return false;
            }

            metadata = peReader.GetMetadataReader();
        }
        catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException or IOException)
        {
            peReader?.Dispose();
            return false;
        }

        using (peReader)
        {
            budget.Add("── Disassembly (IL) ────────────────────────────────────────────");
            int methods = 0;
            try
            {
                foreach (TypeDefinitionHandle typeHandle in metadata.TypeDefinitions)
                {
                    if (budget.Exhausted)
                    {
                        break;
                    }

                    TypeDefinition type = metadata.GetTypeDefinition(typeHandle);
                    foreach (MethodDefinitionHandle methodHandle in type.GetMethods())
                    {
                        MethodDefinition method = metadata.GetMethodDefinition(methodHandle);
                        if (method.RelativeVirtualAddress == 0)
                        {
                            continue;
                        }

                        if (methods >= DisassemblyBudget.MaxMethods)
                        {
                            budget.Truncate($"the {DisassemblyBudget.MaxMethods:N0} method limit was reached");
                            break;
                        }

                        if (budget.Exhausted)
                        {
                            break;
                        }

                        methods++;
                        DisassembleMethod(peReader, metadata, method, budget);
                    }

                    if (budget.TruncatedBecause is not null)
                    {
                        break;
                    }
                }
            }
            catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException)
            {
                budget.Lines.Add($"  Metadata could not be read any further: {ex.Message}");
            }

            if (methods == 0 && budget.TruncatedBecause is null)
            {
                budget.Lines.Add("  This assembly has no method bodies.");
            }

            budget.FinishTruncation();
        }

        return true;
    }

    private static void DisassembleMethod(PEReader peReader, MetadataReader metadata, MethodDefinition method, DisassemblyBudget budget)
    {
        string name = MethodName(metadata, method);
        byte[] il;
        int maxStack;
        try
        {
            MethodBodyBlock body = peReader.GetMethodBody(method.RelativeVirtualAddress);
            il = body.GetILBytes() ?? [];
            maxStack = body.MaxStack;
        }
        catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException)
        {
            budget.Add(string.Empty);
            budget.Add($"  .method {name}  // body unreadable: {ex.Message}");
            return;
        }

        budget.Add(string.Empty);
        budget.Add($"  .method {name}  // RVA 0x{method.RelativeVirtualAddress:x}, {il.Length} bytes of IL, maxstack {maxStack}");

        int position = 0;
        int count = 0;
        while (position < il.Length)
        {
            if (count++ >= DisassemblyBudget.MaxInstructionsPerMethod)
            {
                budget.Add("    … method truncated: instruction limit per method reached");
                return;
            }

            int start = position;
            byte first = il[position++];
            OpCode opCode;
            if (first == 0xFE)
            {
                if (position >= il.Length)
                {
                    budget.Add($"    IL_{start:x4}: .byte 0xfe  // truncated");
                    return;
                }

                opCode = TwoByteOpCodes[il[position++]];
            }
            else
            {
                opCode = OneByteOpCodes[first];
            }

            if (opCode.Name is null)
            {
                budget.Add($"    IL_{start:x4}: .byte 0x{first:x2}");
                continue;
            }

            string? operand = ReadOperand(metadata, opCode, il, ref position);
            if (operand is null)
            {
                budget.Add($"    IL_{start:x4}: {opCode.Name}  // operand runs past the end of the method");
                return;
            }

            if (!budget.Add(operand.Length == 0
                ? $"    IL_{start:x4}: {opCode.Name}"
                : $"    IL_{start:x4}: {opCode.Name} {operand}"))
            {
                return;
            }
        }
    }

    /// <summary>Reads one instruction's operand and advances <paramref name="position"/>; null when it would run past the end.</summary>
    private static string? ReadOperand(MetadataReader metadata, OpCode opCode, byte[] il, ref int position)
    {
        int size = OperandSize(opCode.OperandType);
        if (size > il.Length - position)
        {
            return null;
        }

        ReadOnlySpan<byte> span = il.AsSpan(position);
        string text;
        switch (opCode.OperandType)
        {
            case OperandType.InlineNone:
                text = string.Empty;
                break;
            case OperandType.ShortInlineBrTarget:
                text = $"IL_{position + 1 + (sbyte)span[0]:x4}";
                break;
            case OperandType.InlineBrTarget:
                text = $"IL_{position + 4 + BinaryPrimitives.ReadInt32LittleEndian(span):x4}";
                break;
            case OperandType.ShortInlineI:
                text = ((sbyte)span[0]).ToString(CultureInfo.InvariantCulture);
                break;
            case OperandType.ShortInlineVar:
                text = span[0].ToString(CultureInfo.InvariantCulture);
                break;
            case OperandType.InlineVar:
                text = BinaryPrimitives.ReadUInt16LittleEndian(span).ToString(CultureInfo.InvariantCulture);
                break;
            case OperandType.InlineI:
                text = BinaryPrimitives.ReadInt32LittleEndian(span).ToString(CultureInfo.InvariantCulture);
                break;
            case OperandType.InlineI8:
                text = BinaryPrimitives.ReadInt64LittleEndian(span).ToString(CultureInfo.InvariantCulture);
                break;
            case OperandType.ShortInlineR:
                text = BinaryPrimitives.ReadSingleLittleEndian(span).ToString("R", CultureInfo.InvariantCulture);
                break;
            case OperandType.InlineR:
                text = BinaryPrimitives.ReadDoubleLittleEndian(span).ToString("R", CultureInfo.InvariantCulture);
                break;
            case OperandType.InlineString:
                text = UserString(metadata, BinaryPrimitives.ReadInt32LittleEndian(span));
                break;
            case OperandType.InlineMethod:
            case OperandType.InlineField:
            case OperandType.InlineType:
            case OperandType.InlineTok:
            case OperandType.InlineSig:
                text = TokenName(metadata, BinaryPrimitives.ReadInt32LittleEndian(span));
                break;
            case OperandType.InlineSwitch:
                return ReadSwitch(il, ref position);
            default:
                text = string.Empty;
                break;
        }

        position += size;
        return text;
    }

    private static string? ReadSwitch(byte[] il, ref int position)
    {
        uint declared = BinaryPrimitives.ReadUInt32LittleEndian(il.AsSpan(position));
        int afterCount = position + 4;
        // A crafted count must not make the loop run past the bytes that are there.
        long available = (il.Length - afterCount) / 4;
        if (declared > available)
        {
            return null;
        }

        int count = (int)declared;
        int baseOffset = afterCount + (count * 4);
        StringBuilder text = new("(");
        for (int i = 0; i < count && i < MaxSwitchTargetsShown; i++)
        {
            int relative = BinaryPrimitives.ReadInt32LittleEndian(il.AsSpan(afterCount + (i * 4)));
            text.Append(i == 0 ? string.Empty : ", ").Append($"IL_{baseOffset + relative:x4}");
        }

        if (count > MaxSwitchTargetsShown)
        {
            text.Append($", … {count - MaxSwitchTargetsShown} more");
        }

        text.Append(')');
        position = baseOffset;
        return text.ToString();
    }

    private static int OperandSize(OperandType type) => type switch
    {
        OperandType.InlineNone => 0,
        OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
        OperandType.InlineVar => 2,
        OperandType.InlineI8 or OperandType.InlineR => 8,
        // InlineSwitch carries its own count word; the targets are checked in ReadSwitch.
        _ => 4,
    };

    private static string UserString(MetadataReader metadata, int token)
    {
        try
        {
            string value = metadata.GetUserString(MetadataTokens.UserStringHandle(token & 0x00FFFFFF));
            return Quote(value);
        }
        catch (Exception ex) when (ex is BadImageFormatException or ArgumentException)
        {
            return $"<string 0x{token:x8}>";
        }
    }

    private static string Quote(string value)
    {
        StringBuilder text = new("\"");
        foreach (char c in value.Length > MaxStringChars ? value[..MaxStringChars] : value)
        {
            switch (c)
            {
                case '"': text.Append("\\\""); break;
                case '\\': text.Append("\\\\"); break;
                case '\n': text.Append("\\n"); break;
                case '\r': text.Append("\\r"); break;
                case '\t': text.Append("\\t"); break;
                default:
                    if (char.IsControl(c))
                    {
                        text.Append($"\\u{(int)c:x4}");
                    }
                    else
                    {
                        text.Append(c);
                    }

                    break;
            }
        }

        text.Append('"');
        if (value.Length > MaxStringChars)
        {
            text.Append('…');
        }

        return text.ToString();
    }

    private static string TokenName(MetadataReader metadata, int token)
    {
        try
        {
            return EntityName(metadata, MetadataTokens.EntityHandle(token), 0);
        }
        catch (Exception ex) when (ex is BadImageFormatException or ArgumentException or InvalidOperationException)
        {
            return $"<token 0x{token:x8}>";
        }
    }

    private static string EntityName(MetadataReader metadata, EntityHandle handle, int depth)
    {
        if (depth > MaxNestingDepth)
        {
            return "…";
        }

        switch (handle.Kind)
        {
            case HandleKind.MethodDefinition:
                return MethodName(metadata, metadata.GetMethodDefinition((MethodDefinitionHandle)handle));
            case HandleKind.MemberReference:
                MemberReference member = metadata.GetMemberReference((MemberReferenceHandle)handle);
                return $"{EntityName(metadata, member.Parent, depth + 1)}::{metadata.GetString(member.Name)}";
            case HandleKind.MethodSpecification:
                return EntityName(metadata, metadata.GetMethodSpecification((MethodSpecificationHandle)handle).Method, depth + 1) + "<…>";
            case HandleKind.FieldDefinition:
                FieldDefinition field = metadata.GetFieldDefinition((FieldDefinitionHandle)handle);
                return $"{TypeName(metadata, field.GetDeclaringType(), 0)}::{metadata.GetString(field.Name)}";
            case HandleKind.TypeDefinition:
                return TypeName(metadata, (TypeDefinitionHandle)handle, 0);
            case HandleKind.TypeReference:
                return TypeReferenceName(metadata, (TypeReferenceHandle)handle);
            case HandleKind.TypeSpecification:
                return "<constructed type>";
            case HandleKind.StandaloneSignature:
                return "<signature>";
            default:
                return $"<{handle.Kind}>";
        }
    }

    private static string MethodName(MetadataReader metadata, MethodDefinition method) =>
        $"{TypeName(metadata, method.GetDeclaringType(), 0)}::{metadata.GetString(method.Name)}";

    private static string TypeName(MetadataReader metadata, TypeDefinitionHandle handle, int depth)
    {
        TypeDefinition type = metadata.GetTypeDefinition(handle);
        string name = metadata.GetString(type.Name);
        TypeDefinitionHandle declaring = type.GetDeclaringType();
        if (!declaring.IsNil && depth < MaxNestingDepth)
        {
            return $"{TypeName(metadata, declaring, depth + 1)}/{name}";
        }

        string ns = metadata.GetString(type.Namespace);
        return ns.Length == 0 ? name : $"{ns}.{name}";
    }

    private static string TypeReferenceName(MetadataReader metadata, TypeReferenceHandle handle)
    {
        TypeReference type = metadata.GetTypeReference(handle);
        string name = metadata.GetString(type.Name);
        string ns = metadata.GetString(type.Namespace);
        return ns.Length == 0 ? name : $"{ns}.{name}";
    }
}
