using System.Globalization;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace iConsole.Services;

/// <summary>
/// Turns Office Open XML documents into lines a terminal can show: Word as headings, bullets and
/// box-drawn tables, Excel as one grid per sheet with column letters and row numbers, PowerPoint as
/// one framed section per slide with its title, outline and speaker notes.
///
/// <para>Everything is read through the Open XML SDK the converter already uses, so there is no Office
/// interop and no process to start. The renderers never throw for a damaged document: they return what
/// they could read, with a final line saying the rest was unreadable.</para>
/// </summary>
public static class OfficeTextRenderer
{
    /// <summary>Rows and columns past these are left out of a sheet, with a line saying so.</summary>
    internal const int MaxSheetRows = 5000;
    internal const int MaxSheetColumns = 100;

    private const int MaxSheetColumnWidth = 30;
    private const int TableBudgetWidth = 110;

    private readonly record struct GridCell(string Text, bool RightAligned = false);

    public static IReadOnlyList<string> RenderWord(string path)
    {
        List<string> lines = [];
        try
        {
            using WordprocessingDocument doc = WordprocessingDocument.Open(path, false);
            MainDocumentPart? main = doc.MainDocumentPart;

            foreach (HeaderPart header in main?.HeaderParts ?? [])
            {
                if (header.Header is { } headerRoot)
                {
                    AppendWordBlocks(lines, headerRoot, "Header");
                }
            }

            if (main?.Document?.Body is { } body)
            {
                AppendWordBlocks(lines, body, null);
            }

            foreach (FooterPart footer in main?.FooterParts ?? [])
            {
                if (footer.Footer is { } footerRoot)
                {
                    AppendWordBlocks(lines, footerRoot, "Footer");
                }
            }
        }
        catch (Exception exception)
        {
            lines.Add($"[Could not read the document: {exception.Message}]");
        }

        return Trim(lines);
    }

    public static IReadOnlyList<string> RenderSpreadsheet(string path)
    {
        List<string> lines = [];
        try
        {
            using SpreadsheetDocument doc = SpreadsheetDocument.Open(path, false);
            WorkbookPart? workbook = doc.WorkbookPart;
            if (workbook?.Workbook?.Sheets is null)
            {
                return lines;
            }

            List<string> shared = [.. workbook.SharedStringTablePart?.SharedStringTable
                ?.Elements<SharedStringItem>().Select(item => item.InnerText) ?? []];
            bool[] dateStyles = ReadDateStyles(workbook);

            foreach (Sheet sheet in workbook.Workbook.Sheets.Elements<Sheet>())
            {
                AppendHeading(lines, sheet.Name?.Value ?? "(sheet)");
                if (sheet.Id?.Value is not { } id || workbook.GetPartById(id) is not WorksheetPart part)
                {
                    lines.Add("  (not a worksheet)");
                    lines.Add(string.Empty);
                    continue;
                }

                AppendSheet(lines, part, shared, dateStyles);
                lines.Add(string.Empty);
            }
        }
        catch (Exception exception)
        {
            lines.Add($"[Could not read the workbook: {exception.Message}]");
        }

        return Trim(lines);
    }

    public static IReadOnlyList<string> RenderPresentation(string path)
    {
        List<string> lines = [];
        try
        {
            using PresentationDocument doc = PresentationDocument.Open(path, false);
            PresentationPart? presentation = doc.PresentationPart;
            if (presentation?.Presentation?.SlideIdList is null)
            {
                return lines;
            }

            int number = 0;
            foreach (P.SlideId slideId in presentation.Presentation.SlideIdList.Elements<P.SlideId>())
            {
                number++;
                if (slideId.RelationshipId?.Value is not { } id
                    || presentation.GetPartById(id) is not SlidePart slidePart
                    || slidePart.Slide is null)
                {
                    continue;
                }

                AppendSlide(lines, slidePart, number);
            }
        }
        catch (Exception exception)
        {
            lines.Add($"[Could not read the presentation: {exception.Message}]");
        }

        return Trim(lines);
    }

    #region Word

    private static void AppendWordBlocks(List<string> lines, DocumentFormat.OpenXml.OpenXmlElement container, string? label)
    {
        List<string> block = [];
        foreach (DocumentFormat.OpenXml.OpenXmlElement child in container.ChildElements)
        {
            switch (child)
            {
                case W.Paragraph paragraph:
                    AppendWordParagraph(block, paragraph);
                    break;
                case W.Table table:
                    AppendWordTable(block, table);
                    block.Add(string.Empty);
                    break;
                case W.SdtBlock sdt when sdt.SdtContentBlock is { } content:
                    AppendWordBlocks(block, content, null);
                    break;
            }
        }

        if (label is not null)
        {
            if (!block.Any(line => line.Length > 0))
            {
                return;
            }

            AppendHeading(lines, label);
        }

        lines.AddRange(block);
        if (label is not null)
        {
            lines.Add(string.Empty);
        }
    }

    private static void AppendWordParagraph(List<string> lines, W.Paragraph paragraph)
    {
        string text = ParagraphText(paragraph);
        string style = paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value ?? string.Empty;

        if (text.Trim().Length == 0)
        {
            // One blank line stands for any run of empty paragraphs.
            if (lines.Count > 0 && lines[^1].Length > 0)
            {
                lines.Add(string.Empty);
            }

            return;
        }

        if (style.Equals("Title", StringComparison.OrdinalIgnoreCase))
        {
            lines.Add(text);
            lines.Add(new string('═', Math.Min(text.Length, TableBudgetWidth)));
            return;
        }

        if (style.StartsWith("Heading", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(style["Heading".Length..], out int level))
        {
            if (lines.Count > 0 && lines[^1].Length > 0)
            {
                lines.Add(string.Empty);
            }

            lines.Add($"{new string('#', Math.Clamp(level, 1, 6))} {text}");
            return;
        }

        if (paragraph.ParagraphProperties?.NumberingProperties is { } numbering)
        {
            int indent = numbering.NumberingLevelReference?.Val?.Value ?? 0;
            lines.Add($"{new string(' ', 2 + indent * 2)}• {text}");
            return;
        }

        lines.Add(text);
    }

    private static string ParagraphText(W.Paragraph paragraph)
    {
        StringBuilder builder = new();
        foreach (DocumentFormat.OpenXml.OpenXmlElement element in paragraph.Descendants())
        {
            switch (element)
            {
                case W.Text text:
                    builder.Append(text.Text);
                    break;
                case W.TabChar:
                    builder.Append('\t');
                    break;
                case W.Break or W.CarriageReturn:
                    builder.Append(' ');
                    break;
            }
        }

        return builder.ToString().Replace("\t", "    ");
    }

    private static void AppendWordTable(List<string> lines, W.Table table)
    {
        List<GridCell[]> rows = [];
        foreach (W.TableRow row in table.Elements<W.TableRow>())
        {
            rows.Add([.. row.Elements<W.TableCell>().Select(cell =>
                new GridCell(string.Join(" ", cell.Descendants<W.Paragraph>()
                    .Select(ParagraphText).Where(text => text.Length > 0))))]);
        }

        AppendGrid(lines, rows, headerRows: 0, widthCap: null);
    }

    #endregion

    #region Excel

    private static void AppendSheet(List<string> lines, WorksheetPart part, List<string> shared, bool[] dateStyles)
    {
        SheetData? data = part.Worksheet?.GetFirstChild<SheetData>();
        if (data is null)
        {
            lines.Add("  (empty)");
            return;
        }

        List<(uint Row, Dictionary<int, GridCell> Cells)> rows = [];
        int columns = 0;
        bool truncatedRows = false;
        bool truncatedColumns = false;

        foreach (Row row in data.Elements<Row>())
        {
            if (rows.Count >= MaxSheetRows)
            {
                truncatedRows = true;
                break;
            }

            Dictionary<int, GridCell> cells = [];
            int next = 0;
            foreach (Cell cell in row.Elements<Cell>())
            {
                int column = cell.CellReference?.Value is { } reference ? ColumnIndex(reference) : next;
                next = column + 1;
                if (column >= MaxSheetColumns)
                {
                    truncatedColumns = true;
                    continue;
                }

                GridCell value = ReadCell(cell, shared, dateStyles);
                if (value.Text.Length > 0)
                {
                    cells[column] = value;
                    columns = Math.Max(columns, column + 1);
                }
            }

            if (cells.Count > 0)
            {
                rows.Add((row.RowIndex?.Value ?? (rows.Count > 0 ? rows[^1].Row + 1 : 1), cells));
            }
        }

        if (rows.Count == 0)
        {
            lines.Add("  (empty)");
            return;
        }

        List<GridCell[]> grid = [];
        grid.Add([new GridCell(string.Empty), .. Enumerable.Range(0, columns).Select(c => new GridCell(ColumnName(c)))]);
        foreach ((uint rowNumber, Dictionary<int, GridCell> cells) in rows)
        {
            GridCell[] line = new GridCell[columns + 1];
            line[0] = new GridCell(rowNumber.ToString(CultureInfo.InvariantCulture), RightAligned: true);
            for (int c = 0; c < columns; c++)
            {
                line[c + 1] = cells.TryGetValue(c, out GridCell value) ? value : new GridCell(string.Empty);
            }

            grid.Add(line);
        }

        AppendGrid(lines, grid, headerRows: 1, widthCap: MaxSheetColumnWidth);

        if (truncatedRows || truncatedColumns)
        {
            lines.Add($"  … only the first {MaxSheetRows} rows and {MaxSheetColumns} columns are shown.");
        }
    }

    private static GridCell ReadCell(Cell cell, List<string> shared, bool[] dateStyles)
    {
        string raw = cell.CellValue?.Text ?? string.Empty;
        CellValues? type = cell.DataType?.Value;

        if (type == CellValues.SharedString)
        {
            return new GridCell(int.TryParse(raw, out int index) && index >= 0 && index < shared.Count
                ? shared[index] : string.Empty);
        }

        if (type == CellValues.InlineString)
        {
            return new GridCell(cell.InlineString?.InnerText ?? string.Empty);
        }

        if (type == CellValues.Boolean)
        {
            return new GridCell(raw == "1" ? "TRUE" : "FALSE", RightAligned: true);
        }

        if (type == CellValues.String || type == CellValues.Error)
        {
            return new GridCell(raw);
        }

        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
        {
            return new GridCell(raw);
        }

        uint style = cell.StyleIndex?.Value ?? 0;
        if (type == CellValues.Date || (style < dateStyles.Length && dateStyles[style]))
        {
            try
            {
                DateTime date = DateTime.FromOADate(number);
                return new GridCell(
                    number % 1 == 0 ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                                    : date.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                    RightAligned: true);
            }
            catch (ArgumentException)
            {
                // Out of the OADate range: show the number as it is.
            }
        }

        return new GridCell(number.ToString("G12", CultureInfo.InvariantCulture), RightAligned: true);
    }

    /// <summary>Which cell styles carry a date or time format, indexed by style index.</summary>
    private static bool[] ReadDateStyles(WorkbookPart workbook)
    {
        Stylesheet? stylesheet = workbook.WorkbookStylesPart?.Stylesheet;
        if (stylesheet?.CellFormats is null)
        {
            return [];
        }

        Dictionary<uint, string> custom = [];
        foreach (NumberingFormat format in stylesheet.NumberingFormats?.Elements<NumberingFormat>() ?? [])
        {
            if (format.NumberFormatId?.Value is { } id && format.FormatCode?.Value is { } code)
            {
                custom[id] = code;
            }
        }

        return [.. stylesheet.CellFormats.Elements<CellFormat>().Select(format =>
        {
            uint id = format.NumberFormatId?.Value ?? 0;
            if (id is >= 14 and <= 22 or >= 45 and <= 47)
            {
                return true;
            }

            return custom.TryGetValue(id, out string? code) && IsDateFormat(code);
        })];
    }

    private static bool IsDateFormat(string code)
    {
        bool quoted = false;
        bool bracketed = false;
        foreach (char c in code)
        {
            if (c == '"')
            {
                quoted = !quoted;
            }
            else if (c == '[')
            {
                bracketed = true;
            }
            else if (c == ']')
            {
                bracketed = false;
            }
            else if (!quoted && !bracketed && char.ToLowerInvariant(c) is 'd' or 'y' or 'h' or 's')
            {
                return true;
            }
        }

        return false;
    }

    public static int ColumnIndex(string cellReference)
    {
        int index = 0;
        foreach (char c in cellReference)
        {
            if (!char.IsAsciiLetter(c))
            {
                break;
            }

            index = index * 26 + (char.ToUpperInvariant(c) - 'A' + 1);
        }

        return Math.Max(0, index - 1);
    }

    public static string ColumnName(int index)
    {
        StringBuilder name = new();
        for (int n = index + 1; n > 0; n = (n - 1) / 26)
        {
            name.Insert(0, (char)('A' + (n - 1) % 26));
        }

        return name.ToString();
    }

    #endregion

    #region PowerPoint

    private static void AppendSlide(List<string> lines, SlidePart slidePart, int number)
    {
        P.Slide slide = slidePart.Slide!;
        List<P.Shape> shapes = [.. slide.Descendants<P.Shape>()];
        P.Shape? titleShape = shapes.FirstOrDefault(IsTitle);
        string title = titleShape is null ? string.Empty : string.Join(" ", ShapeParagraphs(titleShape).Select(p => p.Text));

        AppendHeading(lines, title.Length > 0 ? $"Slide {number}: {title}" : $"Slide {number}");

        foreach (DocumentFormat.OpenXml.OpenXmlElement element in slide.Descendants()
            .Where(e => e is P.Shape or P.GraphicFrame))
        {
            if (element is P.Shape shape && !ReferenceEquals(shape, titleShape))
            {
                foreach ((string text, int level) in ShapeParagraphs(shape))
                {
                    lines.Add($"{new string(' ', 2 + level * 2)}• {text}");
                }
            }
            else if (element is P.GraphicFrame frame && frame.Descendants<A.Table>().FirstOrDefault() is { } table)
            {
                lines.Add(string.Empty);
                AppendDrawingTable(lines, table);
            }
        }

        string[] notes = [.. slidePart.NotesSlidePart?.NotesSlide?.Descendants<P.Shape>()
            .Where(shape => shape.NonVisualShapeProperties?.ApplicationNonVisualDrawingProperties
                ?.PlaceholderShape?.Type?.Value == P.PlaceholderValues.Body)
            .SelectMany(ShapeParagraphs).Select(p => p.Text) ?? []];
        if (notes.Length > 0)
        {
            lines.Add(string.Empty);
            lines.Add("  Notes:");
            lines.AddRange(notes.Select(note => $"    {note}"));
        }

        lines.Add(string.Empty);
    }

    private static bool IsTitle(P.Shape shape)
    {
        P.PlaceholderValues? type = shape.NonVisualShapeProperties?.ApplicationNonVisualDrawingProperties
            ?.PlaceholderShape?.Type?.Value;
        return type == P.PlaceholderValues.Title || type == P.PlaceholderValues.CenteredTitle;
    }

    private static IEnumerable<(string Text, int Level)> ShapeParagraphs(P.Shape shape)
    {
        foreach (A.Paragraph paragraph in shape.TextBody?.Elements<A.Paragraph>() ?? [])
        {
            string text = string.Concat(paragraph.Descendants().Select(e => e switch
            {
                A.Text t => t.Text,
                A.Break => " ",
                _ => string.Empty,
            })).Trim();

            if (text.Length > 0)
            {
                yield return (text, paragraph.ParagraphProperties?.Level?.Value ?? 0);
            }
        }
    }

    private static void AppendDrawingTable(List<string> lines, A.Table table)
    {
        List<GridCell[]> rows = [];
        foreach (A.TableRow row in table.Elements<A.TableRow>())
        {
            rows.Add([.. row.Elements<A.TableCell>().Select(cell =>
                new GridCell(string.Join(" ", cell.Descendants<A.Paragraph>()
                    .Select(p => p.InnerText).Where(text => text.Length > 0))))]);
        }

        AppendGrid(lines, rows, headerRows: 0, widthCap: null);
        lines.Add(string.Empty);
    }

    #endregion

    #region Shared drawing

    private static void AppendHeading(List<string> lines, string heading) =>
        lines.Add($"── {heading} {new string('─', Math.Max(0, 60 - heading.Length))}");

    private static string Clean(string text) =>
        text.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ').Replace('\t', ' ');

    /// <summary>
    /// Draws a box-drawn table. Columns are as wide as their widest cell up to a cap — a fixed cap for
    /// sheets, a share of <see cref="TableBudgetWidth"/> for document tables — and longer text is cut
    /// with an ellipsis rather than wrapped, so every row stays one line.
    /// </summary>
    private static void AppendGrid(List<string> lines, List<GridCell[]> rows, int headerRows, int? widthCap)
    {
        int columns = rows.Count == 0 ? 0 : rows.Max(row => row.Length);
        if (columns == 0)
        {
            return;
        }

        int cap = widthCap ?? Math.Clamp(TableBudgetWidth / columns, 8, 40);
        int[] widths = new int[columns];
        for (int c = 0; c < columns; c++)
        {
            widths[c] = Math.Clamp(
                rows.Max(row => c < row.Length ? Clean(row[c].Text).Length : 0), 1, cap);
        }

        string Border(char left, char middle, char right) =>
            left + string.Join(middle, widths.Select(w => new string('─', w + 2))) + right;

        lines.Add(Border('┌', '┬', '┐'));
        for (int r = 0; r < rows.Count; r++)
        {
            StringBuilder line = new("│");
            for (int c = 0; c < columns; c++)
            {
                GridCell cell = c < rows[r].Length ? rows[r][c] : default;
                string text = Clean(cell.Text ?? string.Empty);
                if (text.Length > widths[c])
                {
                    text = text[..(widths[c] - 1)] + "…";
                }

                line.Append(' ')
                    .Append(cell.RightAligned ? text.PadLeft(widths[c]) : text.PadRight(widths[c]))
                    .Append(" │");
            }

            lines.Add(line.ToString());
            if (r + 1 == headerRows && r + 1 < rows.Count)
            {
                lines.Add(Border('├', '┼', '┤'));
            }
        }

        lines.Add(Border('└', '┴', '┘'));
    }

    private static List<string> Trim(List<string> lines)
    {
        while (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return lines;
    }

    #endregion
}
