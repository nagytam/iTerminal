using iConsole.Buffers;
using iConsole.Commands;
using iConsole.Controls.Buttons;
using iConsole.Controls.PopUpWindows;
using iConsole.Controls.Progress;
using iConsole.Data;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Services;
using iConsole.Tasks;
using iTerminal.Core.Tasks;
using System.ComponentModel;

namespace iTerminal.Core.Controls.PopUpWindows;

public class ProgressWindow : PopUpWindow
{
    private BackgroundTask _task { get; set; }
    private ProgressBar _progressBar { get; set; }
    private ProgressTime _progressTime { get; set; }
    private Button _cancelButton { get; set; }

    /// <summary>
    /// Cancels a running task and closes the window; Esc does the same. Its title follows the task:
    /// "Cancel" while there is something to cancel, "Close" once it is over — a failed run keeps the
    /// window up to show why, and a "Cancel" button under a task that has already failed read as if
    /// something were still going on.
    /// </summary>
    private sealed class CancelOrCloseCommand(BackgroundTask task) : iConsole.Commands.Command
    {
        public override string Title => task.Status == iConsole.Data.TaskStatus.Started ? "Cancel" : "Close";

        public override Key? Key { get; init; } = new Key(ConsoleKey.Escape, false, false, false);

        public override Task ExecuteAsync(object? arg)
        {
            if (task.Status == iConsole.Data.TaskStatus.Started)
            {
                task.CancellationTokenSource.Cancel();
            }

            Control?.Parent?.Close();
            return Task.CompletedTask;
        }
    }

    public ProgressWindow(BackgroundTask task, IThemeService themeService) 
        : base((task ?? new ExecuteCommandTask()).Name, themeService)
    {
        task ??= new ExecuteCommandTask();

        IsModal = true;
        _task = task;
        _task.PropertyChanged += OnTaskChanged;
        _progressBar = new ProgressBar(themeService);
        AddControl(_progressBar);
        _progressTime = new ProgressTime(_task, themeService);
        AddControl(_progressTime);
        _cancelButton = new Button(new CancelOrCloseCommand(task), themeService) { HotKeyChar = 'c' };
        AddControl(_cancelButton);
    }

    private void OnTaskChanged(object? sender, PropertyChangedEventArgs e)
    {
        _progressBar.Current = (int)_task.ProgressPercentage;
        StateHasChanged();
    }

    /// <summary>
    /// The base's [X] only clears <see cref="iConsole.Controls.Control.IsVisible"/>. This window is modal, and
    /// <see cref="iConsole.Desktops.Desktop.GetModalChildControl"/> looks for the first modal
    /// control in the tree without asking whether it is visible — so a hidden one still takes every
    /// key and the app looks frozen. Closing removes it from the tree; the task carries on and
    /// stays visible in the task manager, which is what dismissing a progress window should mean.
    /// </summary>
    public override void OnInitializeActionButtons()
    {
        AddActionButton(new ActionButton("Close", "[X]", new CloseParentCommand(), ThemeService));
    }

    public override void OnInitializeLayout()
    {
        base.OnInitializeLayout();
        if (Parent != null)
        {
            // A few lines taller than the base 15 to fit TransferTask's Files/Size/Speed
            // statistics; harmless extra blank space for tasks that aren't a TransferTask.
            CenterInParent(new Size(80, 18));
        }
        _progressBar.Position = new Position(1, 1);
        _progressBar.ViewSize = new Size(ViewSize.Width - 2, 1);

        _progressBar.Min = 0;
        _progressBar.Max = 100;
        _progressBar.Current = 0;

        _progressTime.Position = new Position(1, 2);
        _progressTime.ViewSize = new Size(ViewSize.Width - 2, 1);

        _cancelButton.Position = new Position(1, ViewSize.Height - 2);
        _cancelButton.ViewSize = new Size(ViewSize.Width - 2, 1);
    }

    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);

        // A task that failed still reports Status.Completed — it caught its exception rather than
        // letting it escape — so showing the raw status next to a "failed" description would
        // contradict it on the one screen the user has to read the failure from.
        var status = _task.Error is null ? _task.Status.ToString() : $"failed — {_task.Error}";
        var text = _task.Description + Environment.NewLine
            + $"Task {status}";
        if (_task is TransferTask transferTask)
        {
            text += Environment.NewLine + FormatStatistics(transferTask);
        }
        var width = (int)ViewSize.Width - 2;
        if (width <= 0)
        {
            return;
        }

        // Wrapped, padded and in the theme's colours. The lines were written as they came, in a
        // fixed white on blue: a failure's message — a paragraph, often — was cut at the border, and
        // a shorter line left the end of the longer one before it on screen.
        var colorScheme = ThemeService.CurrentColorScheme;
        var firstRow = 3;
        var lastRow = (int)ViewSize.Height - 3;
        var lines = DetailLines(text, width);
        for (var row = firstRow; row <= lastRow; row++)
        {
            var index = row - firstRow;
            var line = index < lines.Count ? lines[index] : string.Empty;
            if (row == lastRow && lines.Count > lastRow - firstRow + 1)
            {
                line = line.Cut(Math.Max(0, width - 1)) + "…";
            }

            var foreground = _task.Error is not null && index > 0 ? colorScheme.Error : colorScheme.Foreground;
            buffer.Write(ZOrder,
                new Position(AbsolutePosition.X + 1, (uint)(AbsolutePosition.Y + row)),
                new Size(width, 1),
                new ConxelString(line.ExpandRight(width), foreground, colorScheme.Background));
        }
    }

    internal static List<string> DetailLines(string text, int width) =>
        [.. text.Split(Environment.NewLine).SelectMany(line => line.Length == 0 ? [string.Empty] : line.WrapWords(width))];

    internal string CancelButtonTitleForTest => _cancelButton.Title;

    private static string FormatStatistics(TransferTask task)
    {
        var files = $"Files: {task.ItemsProcessed}/{task.TotalItems}";
        var size = task.TotalBytes > 0
            ? $"Size: {task.BytesProcessed.ToFileSize()}/{task.TotalBytes.ToFileSize()}"
            : $"Size: {task.BytesProcessed.ToFileSize()}";
        var speed = $"Speed: {((long)task.BytesPerSecond).ToFileSize()}/s ({task.ItemsPerSecond:N1} files/s)";
        return string.Join(Environment.NewLine, files, size, speed);
    }

    /// <summary>
    /// Dismisses the window as soon as the operation is over. This is deliberately driven from the
    /// idle pass rather than from the task's <c>PropertyChanged</c>: that event arrives on a
    /// <see cref="iConsole.Tasks.TaskManager"/> worker thread, and removing a control from the tree
    /// there would race the renderer walking it. Reading the status here also covers the task that
    /// finished before this window was ever built and subscribed — a small local file copy usually
    /// does — which no completion event can, because it was raised before anyone was listening.
    /// A failed run (see <see cref="BackgroundTask.Error"/>) stays up: its message is only on this
    /// window, and dismissing it would leave the user with nothing to explain why nothing happened.
    /// </summary>
    public override void OnIdle()
    {
        base.OnIdle();

        if (IsVisible && _task.Status != iConsole.Data.TaskStatus.Started && _task.Error is null)
        {
            Close();
            return;
        }

        StateHasChanged();
    }

    public override void Close()
    {
        // The task outlives this window when it is dismissed while still running, so leaving the
        // handler attached would keep the closed window (and everything it draws with) alive and
        // marking itself changed for the rest of the operation.
        _task.PropertyChanged -= OnTaskChanged;
        Parent?.RemoveControl(this);
        base.Close();
    }
}
