using iConsole.Data;

namespace iConsole.Display;

public class ColorScheme
{
    public Color Background { get; set; }
    public Color Foreground { get; set; }
    public Color SelectedForeground { get; set; }
    public Color SelectedBackground { get; set; }
    public Color SelectedFocusedForeground { get; set; }
    public Color SelectedFocusedBackground { get; set; }
    public Color DisabledForeground { get; set; }
    public Color DisabledSelectedForeground { get; set; }
    public Color DisabledBackground { get; set; }
    public Color ButtonBackground { get; set; }
    public Color DisabledButtonBackground { get; set; }
    public Color Error { get; set; }
    public Theme Theme { get; set; } = Theme.Default;

    public ColorScheme()
    {
        Background = Color.Blue;
        Foreground = Color.White;
        SelectedForeground = Color.Blue;
        SelectedBackground = Color.Yellow;
        SelectedFocusedForeground = Color.DarkGreen;
        SelectedFocusedBackground = Color.DarkYellow;
        DisabledSelectedForeground = Color.DarkYellow;
        DisabledForeground = Color.White;
        DisabledBackground = Color.Gray;
        ButtonBackground = Color.DarkBlue;
        DisabledButtonBackground = Color.DarkGray;
        Error = Color.Red;
    }
}
