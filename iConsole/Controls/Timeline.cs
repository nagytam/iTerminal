using iConsole.Buffers;
using iConsole.Data;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Services;

namespace iConsole.Controls;

/// <summary>One event on a <see cref="Timeline"/>.</summary>
public sealed record TimelineEvent(string Time, string Title, BadgeKind Kind = BadgeKind.Neutral);

/// <summary>
/// A read-only vertical history: each event is a dot, its time and its title, joined to the next by a
/// connector (<c>● 10:02  Build started</c> / <c>│</c> / <c>● 10:05  Tests passed</c>). The dot takes the
/// colour of the event's <see cref="TimelineEvent.Kind"/>. Events take two rows each but the last; when
/// they do not all fit, the newest are shown. Every row is painted, so a shorter history leaves nothing behind.
/// </summary>
public class Timeline : Control
{
    private readonly List<TimelineEvent> _events = [];

    public Timeline(IThemeService themeService) : base(themeService)
    {
        ViewSize = new Size(40, 5);
    }

    public IReadOnlyList<TimelineEvent> Events => _events;

    public void SetEvents(IEnumerable<TimelineEvent> events)
    {
        _events.Clear();
        _events.AddRange(events);
        StateHasChanged();
    }

    public void Add(TimelineEvent timelineEvent)
    {
        _events.Add(timelineEvent);
        StateHasChanged();
    }

    /// <summary>Rows needed to show every event.</summary>
    public int RowsNeeded => Math.Max(0, _events.Count * 2 - 1);

    /// <summary>Index of the first event drawn for a view <paramref name="height"/> rows tall (newest events win).</summary>
    public static int FirstVisibleEvent(int eventCount, int height)
    {
        int fit = Math.Max(0, (height + 1) / 2);
        return Math.Max(0, eventCount - fit);
    }

    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);
        if (ViewSize.IsEmpty)
        {
            return;
        }

        ColorScheme colorScheme = ThemeService.CurrentColorScheme;
        int width = (int)ViewSize.Width;
        int height = (int)ViewSize.Height;
        int first = FirstVisibleEvent(_events.Count, height);
        int timeWidth = _events.Count == 0 ? 0 : _events.Max(e => e.Time.Length);
        for (int y = 0; y < height; y++)
        {
            int index = first + y / 2;
            ConxelString line = new(string.Empty, colorScheme.Foreground, colorScheme.Background);
            if (index < _events.Count)
            {
                if (y % 2 == 0)
                {
                    TimelineEvent e = _events[index];
                    line.Append("●", Badge.BackgroundFor(e.Kind, colorScheme.Foreground), colorScheme.Background);
                    line.Append(" " + e.Time.PadRight(timeWidth) + "  ", colorScheme.DisabledForeground, colorScheme.Background);
                    line.Append(e.Title.Cut(Math.Max(0, width - timeWidth - 4)), colorScheme.Foreground, colorScheme.Background);
                }
                else if (index < _events.Count - 1)
                {
                    line.Append("│", colorScheme.DisabledForeground, colorScheme.Background);
                }
            }

            line.Expand(width, colorScheme.Foreground, colorScheme.Background);
            buffer.Write(ZOrder, new Position(AbsolutePosition.X, AbsolutePosition.Y + (uint)y), new Size(width, 1), line);
        }
    }
}
