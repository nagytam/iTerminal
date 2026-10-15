using iConsole.Buffers;
using iConsole.Data;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Services;

namespace iConsole.Controls;

/// <summary>
/// One <see cref="Notification"/> on screen: a coloured bar and a mark for the severity, the title,
/// and up to <see cref="MaxMessageLines"/> lines of message under it.
///
/// <para>A toast only draws. It does not time itself out and does not decide where it goes: the
/// <see cref="INotificationService"/> owns how long a notification lives and <see cref="ToastHost"/>
/// stacks the toasts — so a toast never takes focus or a key, and whatever the user is doing under
/// it carries on. A click dismisses it.</para>
/// </summary>
public class Toast : Control
{
    public const int MaxMessageLines = 3;

    private const string BarGlyph = "▌";

    /// <summary>The bar, a space, the severity mark and a space.</summary>
    private const int PrefixWidth = 4;

    /// <summary>One blank cell between the text and the right edge.</summary>
    private const int RightPadding = 1;

    public Toast(Notification notification, IThemeService themeService) : base(themeService)
    {
        Notification = notification;
    }

    public Notification Notification { get; }

    /// <summary>Raised when the toast is clicked — the host answers by dismissing the notification.</summary>
    public event EventHandler? Clicked;

    /// <summary>The rows a toast for <paramref name="notification"/> takes at <paramref name="width"/> columns.</summary>
    public static int MeasureHeight(Notification notification, int width) =>
        1 + MessageLines(notification, width).Count;

    public static char SeverityMark(NotificationSeverity severity) => severity switch
    {
        NotificationSeverity.Success => '✓',
        NotificationSeverity.Warning => '!',
        NotificationSeverity.Error => '✗',
        _ => 'i',
    };

    public static Color SeverityColor(NotificationSeverity severity, ColorScheme colorScheme) => severity switch
    {
        NotificationSeverity.Success => Color.Green,
        NotificationSeverity.Warning => Color.Yellow,
        NotificationSeverity.Error => colorScheme.Error,
        _ => Color.Cyan,
    };

    private static int TextWidth(int width) => Math.Max(0, width - PrefixWidth - RightPadding);

    private static List<string> MessageLines(Notification? notification, int width)
    {
        var textWidth = TextWidth(width);
        if (notification?.Message is null || textWidth == 0)
        {
            return [];
        }

        var lines = notification.Message.WrapWords(textWidth);
        if (lines.Count > MaxMessageLines)
        {
            lines = lines[..MaxMessageLines];
            lines[^1] = Ellipsize(lines[^1] + "…", textWidth);
        }
        return lines;
    }

    private static string Ellipsize(string text, int width)
    {
        if (text.Length <= width)
        {
            return text;
        }
        return width <= 1 ? text.Cut(width) : text.Cut(width - 1).TrimEnd('…') + "…";
    }

    public override void Draw(IBuffer buffer)
    {
        if (Notification is null)
        {
            return;
        }

        var width = (int)ViewSize.Width;
        var height = (int)ViewSize.Height;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var colorScheme = ThemeService.CurrentColorScheme;
        var accent = SeverityColor(Notification.Severity, colorScheme);
        var foreground = colorScheme.Foreground;
        var background = colorScheme.ButtonBackground;
        var textWidth = TextWidth(width);
        var messageLines = MessageLines(Notification, width);

        // Every row of the rectangle, text or not: nothing clears a layer between frames.
        for (var row = 0; row < height; row++)
        {
            var line = new ConxelString(BarGlyph, accent, background);
            if (row == 0)
            {
                line.Append($" {SeverityMark(Notification.Severity)} ", accent, background);
                line.Append(Ellipsize(Notification.Title, textWidth), foreground, background);
            }
            else
            {
                line.Append(StringHelper.Repeat(' ', PrefixWidth - BarGlyph.Length), foreground, background);
                line.Append(row - 1 < messageLines.Count ? messageLines[row - 1] : string.Empty, foreground, background);
            }
            line.Expand(width, foreground, background);

            buffer.Write(ZOrder,
                new Position(AbsolutePosition.X, AbsolutePosition.Y + row),
                new Size(width, 1),
                line);
        }
    }

    public override Task<bool> HandleMouseEventAsync(MouseEvent mouseEvent)
    {
        // Not Rectangle.Contains: that takes the row below and the column to the right as inside.
        var position = AbsolutePosition;
        var isInside = mouseEvent.Position.X >= position.X && mouseEvent.Position.X < position.X + ViewSize.Width
            && mouseEvent.Position.Y >= position.Y && mouseEvent.Position.Y < position.Y + ViewSize.Height;
        if (IsVisible
            && isInside
            && mouseEvent.WheelDelta == 0
            && (mouseEvent.LeftClicked || mouseEvent.RightClicked))
        {
            Clicked?.Invoke(this, EventArgs.Empty);
            return Task.FromResult(true);
        }
        return Task.FromResult(false);
    }
}
