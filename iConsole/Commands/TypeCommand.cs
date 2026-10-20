using iConsole.Controls.Editors;
using iConsole.Data;
using iConsole.Helper;

namespace iConsole.Commands;

public class TypeCommand : Command
{
    /// <summary>
    /// The editor's lines and the row the cursor is on, with the document given a first line if it has none and the
    /// cursor brought back onto a row that exists.
    /// </summary>
    private static (iConsole.Data.ConcurrentList<string> Lines, int Row) Prepare(TextEditorControl editor)
    {
        var lines = editor.TextModel.Lines;
        if (lines.Count == 0)
        {
            lines.Add(string.Empty);
        }

        var row = (int)Math.Clamp(editor.ScrollableModel.CursorPosition.Y, 0, lines.Count - 1);
        editor.ScrollableModel.CursorPosition.Y = row;
        return (lines, row);
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        if (Control is TextEditorControl editor && !editor.IsReadOnly)
        {
            if (ConsoleKeyHelper.IsTypingKey(keyEvent))
            {
                var (lines, y) = Prepare(editor);
                var line = lines[y];
                var x = Math.Min((int)editor.ScrollableModel.CursorPosition.X, line.Length);
                lines[y] = line.Insert(x, keyEvent.KeyChar.ToString());
                editor.IsDirty = true;
                editor.ScrollableModel.CursorPosition.X = x + 1;
                editor.Refresh();
                return true;
            }
            else if (keyEvent.ConsoleKey == ConsoleKey.Enter)
            {
                // Splits the line at the cursor; the cursor goes to the start of the new line.
                var (lines, y) = Prepare(editor);
                var line = lines[y];
                var x = Math.Min((int)editor.ScrollableModel.CursorPosition.X, line.Length);
                lines[y] = line[..x];
                lines.Insert(y + 1, line[x..]);
                editor.IsDirty = true;
                editor.ScrollableModel.CursorPosition.X = 0;
                editor.ScrollableModel.CursorPosition.Y = y + 1;
                editor.Refresh();
                editor.ScrollableModel.ScrollToVisible();
                return true;
            }
            else if (keyEvent.ConsoleKey == ConsoleKey.Delete)
            {
                // Removes the character under the cursor; at the end of a line, pulls the next line up onto it.
                var (lines, y) = Prepare(editor);
                var line = lines[y];
                var x = Math.Min((int)editor.ScrollableModel.CursorPosition.X, line.Length);
                if (x < line.Length)
                {
                    lines[y] = line.Remove(x, 1);
                    editor.IsDirty = true;
                }
                else if (y + 1 < lines.Count)
                {
                    lines[y] = line + lines[y + 1];
                    lines.RemoveAt(y + 1);
                    editor.IsDirty = true;
                }
                editor.ScrollableModel.CursorPosition.X = x;
                editor.Refresh();
                return true;
            }
        }
        return await base.HandleKeyEventAsync(keyEvent);
    }

    public override Task ExecuteAsync(object? arg)
    {
        //var model = Model as IContentModel;
        //model?.Refresh();
        return Task.CompletedTask;
    }
}
