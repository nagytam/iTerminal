using iConsole.Buffers;
using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Services;
using iTerminal.Core.Services.RuntimeDependencies;

namespace iTerminal.Core.Dialogs;

/// <summary>
/// The page of one dependency in the <see cref="RuntimeDependencyDialog"/> wizard: what it is, what is
/// installed and what installing would do, and whether it is ticked for the install at the end.
/// It reads the service's current snapshot on every draw, so a state that changed in the background shows.
/// </summary>
public class RuntimeDependencyStepPage(
    string id,
    IRuntimeDependencyService service,
    Func<string, bool> isTicked,
    IThemeService themeService) : Control(themeService)
{
    public string Id => id;

    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);

        int width = (int)ViewSize.Width;
        RuntimeDependencyStatus? status = service.Statuses.FirstOrDefault(s => s.Id == id);
        if (width <= 0 || status is null)
        {
            return;
        }

        ColorScheme scheme = ThemeService.CurrentColorScheme;
        string note = status.Problem ?? status.Note ?? string.Empty;
        Color noteColor = status.Problem is not null ? Color.DarkRed : scheme.DisabledForeground;
        string tick = isTicked(id) ? "[x] Install this one   (Space: leave it out)" : "[ ] Left out   (Space: install it)";
        WriteRow(buffer, 0, $"{status.Name} - {status.Purpose}", scheme.Foreground, width, scheme);
        WriteRow(buffer, 1, StateText(status), Color.DarkYellow, width, scheme);
        WriteRow(buffer, 2, note, noteColor, width, scheme);
        WriteRow(buffer, 3, tick, scheme.Foreground, width, scheme);
    }

    internal static string StateText(RuntimeDependencyStatus status) => status.State switch
    {
        RuntimeDependencyState.Missing => $"Not installed. Install {status.TargetVersion}.",
        RuntimeDependencyState.UpdateAvailable => $"Installed {status.InstalledVersion}. Update to {status.TargetVersion}.",
        _ => $"Installed {status.InstalledVersion}. Up to date.",
    };

    internal void WriteRow(IBuffer buffer, int row, string text, Color color, int width, ColorScheme scheme)
    {
        if (row >= ViewSize.Height)
        {
            return;
        }

        buffer.Write(
            ZOrder,
            new Position(AbsolutePosition.X, AbsolutePosition.Y + row),
            new Size(width, 1),
            new ConxelString((text.Length > width ? text.Cut(width) : text).ExpandRight(width), color, scheme.Background));
    }
}

/// <summary>The last wizard page: what Finish will install, one line per ticked dependency.</summary>
public class RuntimeDependencySummaryPage(
    IRuntimeDependencyService service,
    Func<List<string>> tickedIds,
    IThemeService themeService) : Control(themeService)
{
    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);

        int width = (int)ViewSize.Width;
        if (width <= 0)
        {
            return;
        }

        ColorScheme scheme = ThemeService.CurrentColorScheme;
        List<string> ticked = tickedIds();
        List<(string Text, Color Color)> rows = [];
        rows.Add((ticked.Count == 0 ? "Nothing is ticked." : "Finish will install:", scheme.Foreground));
        foreach (string id in ticked)
        {
            RuntimeDependencyStatus? status = service.Statuses.FirstOrDefault(s => s.Id == id);
            if (status is not null)
            {
                rows.Add(($"  {status.Name}: {RuntimeDependencyStepPage.StateText(status)}", scheme.Foreground));
            }
        }

        for (int i = 0; i < ViewSize.Height; i++)
        {
            (string text, Color color) = i < rows.Count ? rows[i] : (string.Empty, scheme.Foreground);
            buffer.Write(
                ZOrder,
                new Position(AbsolutePosition.X, AbsolutePosition.Y + i),
                new Size(width, 1),
                new ConxelString((text.Length > width ? text.Cut(width) : text).ExpandRight(width), color, scheme.Background));
        }
    }
}
