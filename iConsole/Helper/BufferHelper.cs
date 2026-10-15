using iConsole.Buffers;
using System.Text;

namespace iConsole.Helper;

public static class BufferHelper
{
    public static void DumpBufferToHtml(BufferLayer layer, string filename)
    {
        using StreamWriter outputFile = new(filename);
        outputFile.WriteLine("<html><head></head><body><pre>");
        if (layer != null)
        {
            StringBuilder line = new();
            for (int y = 0; y<layer.Content.Length; y++)
            {
                var row = layer[y];
                foreach (var conxel in row.ToArray())
                {
                    line.Append($"<span style=\"color:{conxel.ForegroundColor}; background:{conxel.BackgroundColor}\">");
                    line.Append(conxel.Char);
                    line.Append("</span>");
                }
                line.AppendLine();
            }
            outputFile.WriteLine(line.ToString());
        }
        outputFile.WriteLine("</pre></body></html>");
    }
}
