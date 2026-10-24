using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using OpenMcdf;

namespace iConsole.Services;

/// <summary>
/// Turns the Office 97–2003 binary formats into the same lines the Open XML renderers produce: Word (.doc) as
/// paragraphs and box-drawn tables, Excel (.xls) as one grid per sheet, PowerPoint (.ppt) as one framed section
/// per slide with its title, text and speaker notes.
///
/// <para>These files are OLE2 compound files holding binary record streams. Only the streams that carry text
/// are read: the piece table and text stream of a .doc, the BIFF8 cell and shared-string records of a .xls, and
/// the text atoms a .ppt reaches through its persist directory. Formatting, pictures, charts, macros and
/// encrypted content are not read; an encrypted workbook says so instead.</para>
/// </summary>
public static class LegacyOfficeTextRenderer
{
    static LegacyOfficeTextRenderer()
    {
        // The Word code pages (1252 for the 8-bit text of a .doc) are not in the base framework.
        Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
    }

    private const ushort BiffBof = 0x0809;
    private const ushort BiffEof = 0x000A;
    private const ushort BiffContinue = 0x003C;
    private const ushort BiffFilePass = 0x002F;
    private const ushort BiffBoundSheet = 0x0085;
    private const ushort BiffSharedStrings = 0x00FC;
    private const ushort BiffLabelSst = 0x00FD;
    private const ushort BiffLabel = 0x0204;
    private const ushort BiffNumber = 0x0203;
    private const ushort BiffRk = 0x027E;
    private const ushort BiffMulRk = 0x00BD;
    private const ushort BiffBoolErr = 0x0205;
    private const ushort BiffFormula = 0x0006;
    private const ushort BiffString = 0x0207;
    private const ushort BiffFormat = 0x041E;
    private const ushort BiffExtendedFormat = 0x00E0;

    private const ushort PptUserEditAtom = 4085;
    private const ushort PptPersistDirectoryAtom = 6002;
    private const ushort PptSlideListWithText = 4080;
    private const ushort PptSlidePersistAtom = 1011;
    private const ushort PptTextHeaderAtom = 3999;
    private const ushort PptTextCharsAtom = 4000;
    private const ushort PptTextBytesAtom = 4008;

    /// <summary>Word text-type codes of a .ppt TextHeaderAtom that are titles.</summary>
    private const uint PptTitleType = 0;
    private const uint PptCenterTitleType = 6;

    /// <summary>The wide-character flag in a BIFF or compound-file string (fHighByte set means UTF-16).</summary>
    private const byte BiffWideFlag = 0x01;

    #region Word (.doc)

    public static IReadOnlyList<string> RenderWord(byte[] content)
    {
        List<string> lines = [];
        try
        {
            byte[]? word = ReadStream(content, "WordDocument");
            if (word is null || word.Length < 0x1AA)
            {
                lines.Add("[Could not read the document: the WordDocument stream is missing]");
                return lines;
            }

            // The FIB says which table stream holds the piece table (bit 9 of the flags) and where it is.
            ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(word.AsSpan(0x0A));
            string tableName = (flags & 0x0200) != 0 ? "1Table" : "0Table";
            uint clxOffset = BinaryPrimitives.ReadUInt32LittleEndian(word.AsSpan(0x1A2));
            uint clxLength = BinaryPrimitives.ReadUInt32LittleEndian(word.AsSpan(0x1A6));

            byte[]? table = ReadStream(content, tableName);
            if (table is null || clxOffset + clxLength > table.Length)
            {
                lines.Add($"[Could not read the document: the {tableName} stream is missing]");
                return lines;
            }

            string text = ReadPieceTable(word, table.AsSpan((int)clxOffset, (int)clxLength));
            AppendWordText(lines, text);
        }
        catch (Exception exception)
        {
            lines.Add($"[Could not read the document: {exception.Message}]");
        }

        return OfficeTextRenderer.Trim(lines);
    }

    /// <summary>
    /// Concatenates the document's text from its pieces. A piece is either 8-bit text (cp1252, when bit 30 of its
    /// file offset is set, at half that offset) or UTF-16 at the offset.
    /// </summary>
    private static string ReadPieceTable(byte[] word, ReadOnlySpan<byte> clx)
    {
        int position = 0;
        while (position < clx.Length && clx[position] == 0x01)
        {
            // Prc: a property block to skip.
            position += 3 + BinaryPrimitives.ReadUInt16LittleEndian(clx[(position + 1)..]);
        }

        if (position >= clx.Length || clx[position] != 0x02)
        {
            throw new InvalidDataException("the piece table is missing");
        }

        uint plcLength = BinaryPrimitives.ReadUInt32LittleEndian(clx[(position + 1)..]);
        ReadOnlySpan<byte> plc = clx.Slice(position + 5, (int)Math.Min(plcLength, clx.Length - position - 5));
        int pieces = (plc.Length - 4) / 12;
        int pieceTableStart = (pieces + 1) * 4;

        StringBuilder text = new();
        for (int i = 0; i < pieces; i++)
        {
            int characters = (int)(BinaryPrimitives.ReadUInt32LittleEndian(plc[((i + 1) * 4)..])
                - BinaryPrimitives.ReadUInt32LittleEndian(plc[(i * 4)..]));
            uint fileOffset = BinaryPrimitives.ReadUInt32LittleEndian(plc[(pieceTableStart + i * 8 + 2)..]);
            bool compressed = (fileOffset & 0x40000000) != 0;
            int offset = (int)(compressed ? (fileOffset & 0x3FFFFFFF) / 2 : fileOffset & 0x3FFFFFFF);

            if (compressed)
            {
                int length = Math.Min(characters, word.Length - offset);
                if (length > 0)
                {
                    text.Append(Encoding.GetEncoding(1252).GetString(word, offset, length));
                }
            }
            else
            {
                int length = Math.Min(characters * 2, word.Length - offset) & ~1;
                if (length > 0)
                {
                    text.Append(Encoding.Unicode.GetString(word, offset, length));
                }
            }
        }

        return text.ToString();
    }

    /// <summary>
    /// Splits the document text into paragraphs and table rows. A table cell's paragraph is followed by the cell
    /// mark (U+0007), and a row ends with two cell marks. Field codes (between U+0013 and U+0014) are not shown,
    /// only their results.
    /// </summary>
    private static void AppendWordText(List<string> lines, string text)
    {
        List<List<string>> table = [];
        List<string> row = [];
        List<string> cellParagraphs = [];
        StringBuilder paragraph = new();
        Stack<bool> fields = new();
        char previous = '\0';

        bool Hidden() => fields.Any(inInstruction => inInstruction);

        void AddLine(string line)
        {
            if (table.Count > 0)
            {
                OfficeTextRenderer.AppendTextTable(lines, table);
                table.Clear();
                lines.Add(string.Empty);
            }

            lines.Add(line);
        }

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            switch (c)
            {
                case '\u0013':
                    fields.Push(true);
                    break;

                case '\u0014':
                    if (fields.Count > 0)
                    {
                        fields.Pop();
                    }

                    fields.Push(false);
                    break;

                case '\u0015':
                    if (fields.Count > 0)
                    {
                        fields.Pop();
                    }

                    break;

                case '\r':
                case '\f':
                    string finished = paragraph.ToString().Trim();
                    paragraph.Clear();
                    if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\u0007')
                    {
                        cellParagraphs.Add(finished);
                    }
                    else
                    {
                        AddLine(finished);
                    }

                    break;

                case '\u0007':
                    // A cell mark ends the cell's last paragraph, which may not have had its own paragraph mark.
                    string last = paragraph.ToString().Trim();
                    paragraph.Clear();
                    if (last.Length > 0)
                    {
                        cellParagraphs.Add(last);
                    }

                    if (cellParagraphs.Count == 0 && previous == '\u0007')
                    {
                        if (row.Count > 0)
                        {
                            table.Add(row);
                            row = [];
                        }
                    }
                    else
                    {
                        row.Add(string.Join(" ", cellParagraphs.Where(p => p.Length > 0)));
                        cellParagraphs.Clear();
                    }

                    break;

                case '\u000B':
                    // A manual line break inside a paragraph.
                    if (!Hidden())
                    {
                        paragraph.Append(' ');
                    }

                    break;

                default:
                    // Pictures and other embedded objects, and control characters, have no text.
                    if (c >= ' ' && c != '\u0001' && c != '\u0008' && !Hidden())
                    {
                        paragraph.Append(c);
                    }

                    break;
            }

            previous = c;
        }

        if (paragraph.Length > 0)
        {
            AddLine(paragraph.ToString().Trim());
        }

        if (row.Count > 0)
        {
            table.Add(row);
        }

        if (table.Count > 0)
        {
            OfficeTextRenderer.AppendTextTable(lines, table);
        }
    }

    #endregion

    #region Excel (.xls)

    public static IReadOnlyList<string> RenderWorkbook(byte[] content)
    {
        List<string> lines = [];
        try
        {
            byte[]? workbook = ReadStream(content, "Workbook", "Book");
            if (workbook is null)
            {
                lines.Add("[Could not read the workbook: the Workbook stream is missing]");
                return lines;
            }

            List<BiffRecord> records = ReadBiffRecords(workbook);
            if (records.Any(record => record.Type == BiffFilePass))
            {
                lines.Add("[The workbook is password protected and cannot be read]");
                return lines;
            }

            List<string> sharedStrings = ReadSharedStrings(records);
            bool[] dateFormats = ReadDateFormats(records);

            // A sheet's records start where its BOUNDSHEET says, and run up to the next BOF or the EOF.
            Dictionary<int, int> recordIndexByOffset = [];
            for (int i = 0; i < records.Count; i++)
            {
                recordIndexByOffset[records[i].Offset] = i;
            }

            foreach (BoundSheet sheet in ReadBoundSheets(records))
            {
                OfficeTextRenderer.AppendHeading(lines, sheet.Name);
                if (!recordIndexByOffset.TryGetValue(sheet.StreamOffset, out int start))
                {
                    lines.Add("  (not a worksheet)");
                    lines.Add(string.Empty);
                    continue;
                }

                SortedDictionary<int, Dictionary<int, OfficeTextRenderer.GridCell>> rows = ReadSheetCells(
                    records, start, sharedStrings, dateFormats, out bool truncatedRows, out bool truncatedColumns);
                int columns = rows.Values.SelectMany(cells => cells.Keys).DefaultIfEmpty(-1).Max() + 1;

                List<(uint Row, Dictionary<int, OfficeTextRenderer.GridCell> Cells)> grid = [.. rows
                    .Select(pair => ((uint)pair.Key + 1, pair.Value))];
                OfficeTextRenderer.AppendCellGrid(lines, grid, columns, truncatedRows, truncatedColumns);
                lines.Add(string.Empty);
            }
        }
        catch (Exception exception)
        {
            lines.Add($"[Could not read the workbook: {exception.Message}]");
        }

        return OfficeTextRenderer.Trim(lines);
    }

    private readonly record struct BiffRecord(int Offset, ushort Type, byte[] Data);

    private readonly record struct BoundSheet(string Name, int StreamOffset);

    private static List<BiffRecord> ReadBiffRecords(byte[] stream)
    {
        List<BiffRecord> records = [];
        int position = 0;
        while (position + 4 <= stream.Length)
        {
            ushort type = BinaryPrimitives.ReadUInt16LittleEndian(stream.AsSpan(position));
            ushort length = BinaryPrimitives.ReadUInt16LittleEndian(stream.AsSpan(position + 2));
            int available = Math.Min(length, stream.Length - position - 4);
            records.Add(new BiffRecord(position, type, stream.AsSpan(position + 4, available).ToArray()));
            position += 4 + length;
        }

        return records;
    }

    private static IEnumerable<BoundSheet> ReadBoundSheets(List<BiffRecord> records)
    {
        foreach (BiffRecord record in records.Where(r => r.Type == BiffBoundSheet))
        {
            // lbPlyPos, hsState, dt (0 is a worksheet; charts and macro sheets are not shown), then the name.
            if (record.Data.Length < 8 || record.Data[5] != 0)
            {
                continue;
            }

            uint streamOffset = BinaryPrimitives.ReadUInt32LittleEndian(record.Data);
            ReadShortString(record.Data, 6, out string name, out _);
            yield return new BoundSheet(name, (int)streamOffset);
        }
    }

    /// <summary>The cells of one sheet, keyed by row then column, read up to the next sheet's BOF or the end.</summary>
    private static SortedDictionary<int, Dictionary<int, OfficeTextRenderer.GridCell>> ReadSheetCells(
        List<BiffRecord> records,
        int start,
        List<string> sharedStrings,
        bool[] dateFormats,
        out bool truncatedRows,
        out bool truncatedColumns)
    {
        SortedDictionary<int, Dictionary<int, OfficeTextRenderer.GridCell>> rows = [];
        bool rowsCut = false;
        bool columnsCut = false;
        (int Row, int Column)? pendingFormula = null;

        void Put(int row, int column, OfficeTextRenderer.GridCell cell)
        {
            if (cell.Text.Length == 0)
            {
                return;
            }

            if (row >= OfficeTextRenderer.MaxSheetRows)
            {
                rowsCut = true;
                return;
            }

            if (column >= OfficeTextRenderer.MaxSheetColumns)
            {
                columnsCut = true;
                return;
            }

            if (!rows.TryGetValue(row, out Dictionary<int, OfficeTextRenderer.GridCell>? cells))
            {
                cells = [];
                rows[row] = cells;
            }

            cells[column] = cell;
        }

        bool IsDate(int xf) => xf < dateFormats.Length && dateFormats[xf];

        for (int i = start + 1; i < records.Count; i++)
        {
            BiffRecord record = records[i];
            if (record.Type is BiffBof or BiffEof)
            {
                break;
            }

            byte[] data = record.Data;
            switch (record.Type)
            {
                case BiffLabelSst when data.Length >= 10:
                    int index = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(6));
                    Put(Row(data), Column(data), new OfficeTextRenderer.GridCell(
                        index < sharedStrings.Count ? sharedStrings[index] : string.Empty));
                    break;

                case BiffLabel when data.Length >= 9:
                    ReadUnicodeString(data, 6, out string label, out _);
                    Put(Row(data), Column(data), new OfficeTextRenderer.GridCell(label));
                    break;

                case BiffNumber when data.Length >= 14:
                    double number = BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(data.AsSpan(6)));
                    Put(Row(data), Column(data), OfficeTextRenderer.NumberCell(number, IsDate(Xf(data))));
                    break;

                case BiffRk when data.Length >= 10:
                    Put(Row(data), Column(data), OfficeTextRenderer.NumberCell(
                        DecodeRk(BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(6))), IsDate(Xf(data))));
                    break;

                case BiffMulRk when data.Length >= 8:
                    // Several RK numbers in a row: each is an XF index and an RK value, the last column closes the record.
                    int firstColumn = Column(data);
                    int count = (data.Length - 6) / 6;
                    for (int k = 0; k < count; k++)
                    {
                        int at = 4 + k * 6;
                        int xf = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(at));
                        double value = DecodeRk(BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(at + 2)));
                        Put(Row(data), firstColumn + k, OfficeTextRenderer.NumberCell(value, IsDate(xf)));
                    }

                    break;

                case BiffBoolErr when data.Length >= 8:
                    Put(Row(data), Column(data), BooleanOrError(data[6], data[7] != 0));
                    break;

                case BiffFormula when data.Length >= 14:
                    // A string, boolean or error result is either a marker here with the string in the next STRING
                    // record, or the plain bytes; any other result is a number.
                    if (data[12] == 0xFF && data[13] == 0xFF)
                    {
                        switch (data[6])
                        {
                            case 0:
                                pendingFormula = (Row(data), Column(data));
                                break;
                            case 1:
                                Put(Row(data), Column(data), BooleanOrError(data[8], false));
                                break;
                            case 2:
                                Put(Row(data), Column(data), BooleanOrError(data[8], true));
                                break;
                        }
                    }
                    else
                    {
                        double formulaNumber = BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(data.AsSpan(6)));
                        Put(Row(data), Column(data), OfficeTextRenderer.NumberCell(formulaNumber, IsDate(Xf(data))));
                    }

                    break;

                case BiffString when pendingFormula is { } formula:
                    ReadUnicodeString(data, 0, out string result, out _);
                    Put(formula.Row, formula.Column, new OfficeTextRenderer.GridCell(result));
                    pendingFormula = null;
                    break;
            }
        }

        truncatedRows = rowsCut;
        truncatedColumns = columnsCut;
        return rows;
    }

    private static int Row(byte[] data) => BinaryPrimitives.ReadUInt16LittleEndian(data);

    private static int Column(byte[] data) => BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(2));

    private static int Xf(byte[] data) => BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(4));

    private static OfficeTextRenderer.GridCell BooleanOrError(byte value, bool isError)
    {
        if (!isError)
        {
            return new OfficeTextRenderer.GridCell(value != 0 ? "TRUE" : "FALSE", RightAligned: true);
        }

        string text = value switch
        {
            0x00 => "#NULL!",
            0x07 => "#DIV/0!",
            0x0F => "#VALUE!",
            0x17 => "#REF!",
            0x1D => "#NAME?",
            0x24 => "#NUM!",
            0x2A => "#N/A",
            _ => "#ERR",
        };
        return new OfficeTextRenderer.GridCell(text, RightAligned: true);
    }

    /// <summary>An RK number: an integer or the top 30 bits of a double, optionally divided by 100.</summary>
    private static double DecodeRk(uint rk)
    {
        double value = (rk & 0x2) != 0
            ? (int)rk >> 2
            : BitConverter.Int64BitsToDouble((long)(rk & 0xFFFFFFFC) << 32);
        return (rk & 0x1) != 0 ? value / 100 : value;
    }

    /// <summary>Which cell formats (XF records) carry a date or time, indexed by XF index.</summary>
    private static bool[] ReadDateFormats(List<BiffRecord> records)
    {
        Dictionary<int, string> custom = [];
        foreach (BiffRecord record in records.Where(r => r.Type == BiffFormat && r.Data.Length >= 4))
        {
            ReadUnicodeString(record.Data, 2, out string code, out _);
            custom[BinaryPrimitives.ReadUInt16LittleEndian(record.Data)] = code;
        }

        return [.. records.Where(r => r.Type == BiffExtendedFormat && r.Data.Length >= 4).Select(record =>
        {
            int id = BinaryPrimitives.ReadUInt16LittleEndian(record.Data.AsSpan(2));
            if (id is >= 14 and <= 22 or >= 45 and <= 47)
            {
                return true;
            }

            return custom.TryGetValue(id, out string? code) && OfficeTextRenderer.IsDateFormat(code);
        })];
    }

    /// <summary>
    /// The shared string table. It can run on into CONTINUE records, and a string that is cut by one begins the
    /// next record with its own wide-character flag, so the characters are read through a cursor over the records.
    /// </summary>
    private static List<string> ReadSharedStrings(List<BiffRecord> records)
    {
        List<string> strings = [];
        int index = records.FindIndex(r => r.Type == BiffSharedStrings);
        if (index < 0)
        {
            return strings;
        }

        List<byte[]> segments = [records[index].Data];
        for (int i = index + 1; i < records.Count && records[i].Type == BiffContinue; i++)
        {
            segments.Add(records[i].Data);
        }

        SegmentCursor cursor = new(segments);
        try
        {
            // cstTotal (the reference count) comes first, then cstUnique, the number of strings that follow.
            cursor.Skip(4);
            uint unique = cursor.ReadUInt32();
            for (uint n = 0; n < unique; n++)
            {
                int characters = cursor.ReadUInt16();
                byte flags = cursor.ReadByte();
                int runs = (flags & 0x08) != 0 ? cursor.ReadUInt16() : 0;
                uint extended = (flags & 0x04) != 0 ? cursor.ReadUInt32() : 0;
                strings.Add(cursor.ReadCharacters(characters, (flags & BiffWideFlag) != 0));
                cursor.Skip(runs * 4 + (int)extended);
            }
        }
        catch (InvalidDataException)
        {
            // The table ends early: the strings read so far are the ones there are.
        }

        return strings;
    }

    /// <summary>A short string (one length byte) as used by BOUNDSHEET; returns the offset after it.</summary>
    private static int ReadShortString(byte[] data, int offset, out string text, out int next)
    {
        int characters = data[offset];
        byte flags = data[offset + 1];
        text = ReadCharacters(data, offset + 2, characters, (flags & BiffWideFlag) != 0, out next);
        return next;
    }

    /// <summary>A string with a two-byte length (XLUnicodeString): BIFF8 LABEL, STRING and FORMAT records.</summary>
    private static void ReadUnicodeString(byte[] data, int offset, out string text, out int next)
    {
        int characters = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset));
        byte flags = data[offset + 2];
        text = ReadCharacters(data, offset + 3, characters, (flags & BiffWideFlag) != 0, out next);
    }

    private static string ReadCharacters(byte[] data, int offset, int characters, bool wide, out int next)
    {
        int length = wide ? characters * 2 : characters;
        int available = Math.Max(0, Math.Min(length, data.Length - offset));
        next = offset + length;
        return wide
            ? Encoding.Unicode.GetString(data, offset, available & ~1)
            : Encoding.Latin1.GetString(data, offset, available);
    }

    /// <summary>Reads through record data as one stream, moving to the next record when one is used up.</summary>
    private sealed class SegmentCursor(List<byte[]> segments)
    {
        private int _segment;
        private int _position;

        public byte ReadByte()
        {
            while (_segment < segments.Count && _position >= segments[_segment].Length)
            {
                _segment++;
                _position = 0;
            }

            if (_segment >= segments.Count)
            {
                throw new InvalidDataException("the record data ends early");
            }

            return segments[_segment][_position++];
        }

        public ushort ReadUInt16() => (ushort)(ReadByte() | (ReadByte() << 8));

        public uint ReadUInt32() => (uint)(ReadUInt16() | (ReadUInt16() << 16));

        public void Skip(int count)
        {
            for (int i = 0; i < count; i++)
            {
                ReadByte();
            }
        }

        /// <summary>
        /// Reads the characters of one string. When a CONTINUE record starts in the middle of them, its first byte is
        /// the wide flag for the rest of the characters.
        /// </summary>
        public string ReadCharacters(int characters, bool wide)
        {
            StringBuilder text = new(characters);
            while (text.Length < characters)
            {
                if (_segment >= segments.Count)
                {
                    throw new InvalidDataException("the record data ends early");
                }

                if (_position >= segments[_segment].Length)
                {
                    _segment++;
                    _position = 0;
                    if (_segment >= segments.Count)
                    {
                        throw new InvalidDataException("the record data ends early");
                    }

                    wide = (ReadByte() & BiffWideFlag) != 0;
                    continue;
                }

                text.Append(wide ? (char)ReadUInt16() : (char)ReadByte());
            }

            return text.ToString();
        }
    }

    #endregion

    #region PowerPoint (.ppt)

    public static IReadOnlyList<string> RenderPresentation(byte[] content)
    {
        List<string> lines = [];
        try
        {
            byte[]? presentation = ReadStream(content, "PowerPoint Document");
            byte[]? currentUser = ReadStream(content, "Current User");
            if (presentation is null || currentUser is null)
            {
                lines.Add("[Could not read the presentation: the PowerPoint Document stream is missing]");
                return lines;
            }

            // The current user atom points at the newest UserEdit; each UserEdit points at its persist directory and
            // at the one before it, so the whole chain maps every persist id to the record it currently names.
            Dictionary<uint, int> persist = [];
            int edit = (int)BinaryPrimitives.ReadUInt32LittleEndian(currentUser.AsSpan(16));
            uint documentId = 0;
            HashSet<int> visited = [];
            while (edit > 0 && visited.Add(edit))
            {
                PptRecord userEdit = ReadPptRecord(presentation, edit);
                if (userEdit.Type != PptUserEditAtom)
                {
                    break;
                }

                if (documentId == 0)
                {
                    documentId = U32(presentation, userEdit.DataStart + 16);
                }

                ReadPersistDirectory(presentation, (int)U32(presentation, userEdit.DataStart + 12), persist);
                edit = (int)U32(presentation, userEdit.DataStart + 8);
            }

            if (!persist.TryGetValue(documentId, out int documentOffset))
            {
                lines.Add("[Could not read the presentation: the document record is missing]");
                return lines;
            }

            PptRecord document = ReadPptRecord(presentation, documentOffset);
            PptRecord? slideList = Children(presentation, document).Cast<PptRecord?>()
                .FirstOrDefault(record => record is { Type: PptSlideListWithText, Instance: 0 });
            IEnumerable<uint> slideIds = slideList is { } list
                ? Children(presentation, list).Where(r => r.Type == PptSlidePersistAtom).Select(r => U32(presentation, r.DataStart))
                : Enumerable.Empty<uint>();

            // The notes list (instance 2) names each slide's notes page, in slide order.
            PptRecord? notesList = Children(presentation, document).Cast<PptRecord?>()
                .FirstOrDefault(record => record is { Type: PptSlideListWithText, Instance: 2 });
            List<uint> notesIds = notesList is { } notes
                ? [.. Children(presentation, notes).Where(r => r.Type == PptSlidePersistAtom).Select(r => U32(presentation, r.DataStart))]
                : [];

            int number = 0;
            foreach (uint slideId in slideIds)
            {
                number++;
                if (!persist.TryGetValue(slideId, out int slideOffset))
                {
                    continue;
                }

                uint notesId = number <= notesIds.Count ? notesIds[number - 1] : 0;
                AppendSlide(lines, presentation, persist, ReadPptRecord(presentation, slideOffset), notesId, number);
            }
        }
        catch (Exception exception)
        {
            lines.Add($"[Could not read the presentation: {exception.Message}]");
        }

        return OfficeTextRenderer.Trim(lines);
    }

    private static void AppendSlide(
        List<string> lines,
        byte[] presentation,
        Dictionary<uint, int> persist,
        PptRecord slide,
        uint notesId,
        int number)
    {
        List<(uint Type, string Text)> texts = [];
        CollectText(presentation, slide, texts);

        // A title placeholder is marked as one. Writers that leave it as plain text put a single line first.
        int titleIndex = texts.FindIndex(t => t.Type is PptTitleType or PptCenterTitleType);
        if (titleIndex < 0 && texts.Count > 0 && Paragraphs(texts[0].Text).Count() == 1)
        {
            titleIndex = 0;
        }

        string title = titleIndex < 0 ? string.Empty : Clean(texts[titleIndex].Text);
        OfficeTextRenderer.AppendHeading(lines, title.Length > 0 ? $"Slide {number}: {title}" : $"Slide {number}");

        for (int i = 0; i < texts.Count; i++)
        {
            if (i == titleIndex)
            {
                continue;
            }

            foreach (string paragraph in Paragraphs(texts[i].Text))
            {
                lines.Add($"  • {paragraph}");
            }
        }

        // The notes page's body text is the speaker notes.
        List<string> notes = [];
        if (notesId != 0 && persist.TryGetValue(notesId, out int notesOffset))
        {
            List<(uint Type, string Text)> notesTexts = [];
            CollectText(presentation, ReadPptRecord(presentation, notesOffset), notesTexts);
            notes = [.. notesTexts.SelectMany(t => Paragraphs(t.Text))];
        }

        if (notes.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("  Notes:");
            lines.AddRange(notes.Select(note => $"    {note}"));
        }

        lines.Add(string.Empty);
    }

    /// <summary>The text atoms under a container, in order, each with its text type from the TextHeaderAtom before it.</summary>
    private static void CollectText(byte[] presentation, PptRecord container, List<(uint Type, string Text)> texts)
    {
        uint? headerType = null;
        foreach (PptRecord child in Children(presentation, container))
        {
            if (child.IsContainer)
            {
                CollectText(presentation, child, texts);
                continue;
            }

            switch (child.Type)
            {
                case PptTextHeaderAtom when child.Length >= 4:
                    headerType = U32(presentation, child.DataStart);
                    break;

                case PptTextCharsAtom:
                    texts.Add((headerType ?? 4, Encoding.Unicode.GetString(presentation, child.DataStart, child.Length & ~1)));
                    headerType = null;
                    break;

                case PptTextBytesAtom:
                    texts.Add((headerType ?? 4, Encoding.GetEncoding(1252).GetString(presentation, child.DataStart, child.Length)));
                    headerType = null;
                    break;
            }
        }
    }

    /// <summary>A PowerPoint text run: its paragraphs are split on the carriage return and the vertical tab a line break uses.</summary>
    private static IEnumerable<string> Paragraphs(string text) =>
        text.Split(['\r', '\v', '\n']).Select(Clean).Where(p => p.Length > 0);

    private static string Clean(string text) => text.Replace('\u0000', ' ').Trim();

    /// <summary>A persist directory: groups of a header (first persist id and count) and the record offsets they name.</summary>
    private static void ReadPersistDirectory(byte[] presentation, int offset, Dictionary<uint, int> persist)
    {
        PptRecord directory = ReadPptRecord(presentation, offset);
        if (directory.Type != PptPersistDirectoryAtom)
        {
            return;
        }

        int position = directory.DataStart;
        int end = directory.DataStart + directory.Length;
        while (position + 4 <= end)
        {
            uint header = U32(presentation, position);
            uint persistId = header & 0xFFFFF;
            uint count = header >> 20;
            for (uint k = 0; k < count && position + 4 + (int)(4 * k) + 4 <= end; k++)
            {
                persist.TryAdd(persistId + k, (int)U32(presentation, position + 4 + (int)(4 * k)));
            }

            position += 4 + (int)(4 * count);
        }
    }

    /// <summary>A record of a PowerPoint binary stream: an 8-byte header, then its data (children, for a container).</summary>
    private readonly record struct PptRecord(ushort Type, int Version, int Instance, int DataStart, int Length)
    {
        public bool IsContainer => Version == 0xF;
    }

    private static PptRecord ReadPptRecord(byte[] presentation, int offset)
    {
        if (offset < 0 || offset + 8 > presentation.Length)
        {
            throw new InvalidDataException("a record offset is outside the stream");
        }

        ushort versionInstance = BinaryPrimitives.ReadUInt16LittleEndian(presentation.AsSpan(offset));
        ushort type = BinaryPrimitives.ReadUInt16LittleEndian(presentation.AsSpan(offset + 2));
        uint length = U32(presentation, offset + 4);
        int dataStart = offset + 8;
        int available = (int)Math.Min(length, (uint)(presentation.Length - dataStart));
        return new PptRecord(type, versionInstance & 0xF, versionInstance >> 4, dataStart, available);
    }

    /// <summary>The records directly inside a container, in order.</summary>
    private static IEnumerable<PptRecord> Children(byte[] presentation, PptRecord container)
    {
        int position = container.DataStart;
        int end = container.DataStart + container.Length;
        while (position + 8 <= end)
        {
            PptRecord child = ReadPptRecord(presentation, position);
            yield return child;
            position = child.DataStart + child.Length;
        }
    }

    private static uint U32(byte[] data, int offset)
    {
        if (offset < 0 || offset + 4 > data.Length)
        {
            throw new InvalidDataException("a value is outside the stream");
        }

        return BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset));
    }

    #endregion

    #region Shared

    /// <summary>The named stream of a compound file, or null when the file has none of the names.</summary>
    private static byte[]? ReadStream(byte[] content, params string[] names)
    {
        using MemoryStream file = new(content);
        using RootStorage root = RootStorage.Open(file, StorageModeFlags.LeaveOpen);
        foreach (string name in names)
        {
            if (root.TryOpenStream(name, out CfbStream? stream) && stream is not null)
            {
                using MemoryStream copy = new();
                stream.CopyTo(copy);
                return copy.ToArray();
            }
        }

        return null;
    }

    #endregion
}
