using iConsole.Controls.Editors;
using iConsole.Data;
using iConsole.Helper;

namespace iConsole.Commands;

public class TypeCommand : Command
{
    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        if (Control is TextEditorControl editor && !editor.IsReadOnly)
        {
            if (ConsoleKeyHelper.IsTypingKey(keyEvent))
            {
                long cursorY = editor.ScrollableModel.CursorPosition.Y;
                if (cursorY >= editor.TextModel.Lines.Count)
                {
                    editor.TextModel.Lines.Add(string.Empty);
                }
                var line = editor.TextModel.Lines[(int)cursorY];
                long cursorX = editor.ScrollableModel.CursorPosition.X;
                line = line.Insert((int)cursorX, keyEvent.KeyChar.ToString());
                editor.TextModel.Lines[(int)cursorY] = line;
                editor.IsDirty = true;
                editor.ScrollableModel.CursorPosition.X++;
                editor.Refresh();
                return true;
            }
            else if (keyEvent.ConsoleKey == ConsoleKey.Enter)
            {
                long cursorX = editor.ScrollableModel.CursorPosition.X;
                long cursorY = editor.ScrollableModel.CursorPosition.Y;
                var line = editor.TextModel.Lines[(int)cursorY];
                if (cursorY >= editor.TextModel.Lines.Count)
                {
                    editor.TextModel.Lines.Add(string.Empty);
                }
                else
                {
                    editor.TextModel.Lines.Insert((int)cursorY, string.Empty);
                }
                var lineBefore = line[..Math.Min((int)cursorX, line.Length-1)];
                var lineAfter = line[Math.Min((int)cursorX, line.Length-1)..];

                editor.TextModel.Lines[(int)cursorY] = lineBefore;
                editor.TextModel.Lines[(int)cursorY + 1] = lineAfter;
                editor.IsDirty = true;
                editor.ScrollableModel.CursorPosition.X++;
                editor.Refresh();
                return true;
            }
            else if (keyEvent.ConsoleKey == ConsoleKey.Backspace)
            {
                long cursorX = editor.ScrollableModel.CursorPosition.X;
                long cursorY = editor.ScrollableModel.CursorPosition.Y;
                var line = editor.TextModel.Lines[(int)cursorY];
                if (cursorX > 0)
                {
                    var pos = Math.Min(line.Length - 1, (int)cursorX - 1);
                    var newline = line[0..pos];
                    if (cursorX >= 0 && pos < line.Length)
                    {
                        newline += line[pos..line.Length];
                    }
                    line = newline;
                    editor.TextModel.Lines[(int)cursorY] = line;
                    editor.IsDirty = true;
                    editor.ScrollableModel.CursorPosition.X--;
                }
                else if (string.IsNullOrEmpty(line))
                {
                    editor.TextModel.Lines.RemoveAt((int)cursorY);
                }
                editor.Refresh();
                return true;
            }
            else if (keyEvent.ConsoleKey == ConsoleKey.Delete)
            {
                long cursorX = editor.ScrollableModel.CursorPosition.X;
                long cursorY = editor.ScrollableModel.CursorPosition.Y;
                var line = editor.TextModel.Lines[(int)cursorY];
                if (cursorX < line.Length)
                {
                    var newline = line[0..(int)cursorX];
                    if (cursorX >= 0)
                    {
                        newline += line[((int)cursorX + 1)..line.Length];
                    }
                    line = newline;
                    editor.TextModel.Lines[(int)cursorY] = line;
                    editor.IsDirty = true;
                }
                else if (string.IsNullOrEmpty(line))
                {
                    editor.TextModel.Lines.RemoveAt((int)cursorY);
                }
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
