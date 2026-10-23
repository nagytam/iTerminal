using iConsole.Buffers;
using iConsole.Commands;
using iConsole.Controls.PopUpWindows;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Diagnostics;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Services;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace iConsole.Controls;

[DebuggerDisplay("{Name}")]
public class Control : INotifyPropertyChanged, IControl
{
    public IControl? Parent { get; set; } = null;
    public string Name { get; set; }
    public virtual ConcurrentList<IControl> Controls { get; internal set; } = [];
    public Position Position { get; set; } = new Position(0, 0);
    public Position AbsolutePosition => (Parent is null) ? Position : Position + Parent.AbsolutePosition;
    public Size ViewSize { get; set; } = new Size(0, 0);
    public Rectangle BoundRect => new(Position, ViewSize);
    public Rectangle AbsoluteBoundRect => new(AbsolutePosition, ViewSize);
    public int ZOrder { get; set; } = 0;
    public bool IsModal { get; set; } = false;
    public bool CanFocus { get; set; } = false;
    public bool IsFocused { get; private set; } = false;

    private static long _treeVersion;

    /// <summary>
    /// Moves whenever a control is attached, detached, shown or hidden anywhere. The renderer reads it
    /// to know the screen changed shape: a layer is never cleared between frames, and a buffer layer
    /// composites everything inside the rectangle it has ever been written to — so a dialog closed
    /// while anything else still drew on its Z-order stayed on screen, a picture of a window that no
    /// longer existed, until something happened to paint over every one of its cells.
    /// </summary>
    public static long TreeVersion => Interlocked.Read(ref _treeVersion);

    /// <summary>Tells the renderer the set of controls on screen has changed.</summary>
    protected static void InvalidateTree() => Interlocked.Increment(ref _treeVersion);

    private bool _isVisible = true;
    public virtual bool IsVisible
    {
        get
        {
            return _isVisible && IsParentVisible();
        }
        set
        {
            if (_isVisible != value)
            {
                Task.Run(() => OnIsVisibleChanged());
                _isVisible = value;
                InvalidateTree();
                OnPropertyChanged(nameof(IsVisible));
            }
        }
    }
    public virtual bool IsEnabled { get; set; } = true;
    private bool _hasStateChanged = true;
    public virtual bool HasStateChanged 
    {
        get 
        { 
            return _hasStateChanged; 
        }
        set
        {
            if (_hasStateChanged != value)
            {
                // TODO
                //Task.Run(() => OnHasStateChangedChanged());
                OnHasStateChangedChange(value);
                _hasStateChanged = value;
            }
        } 
    } 
    public virtual ConcurrentList<ICommand> Commands { get; internal set; } = [];
    public IThemeService ThemeService { get; private set; }
    public IRenderingDumpHelper Dump { get; set; } = NullRenderingDumpHelper.Instance;
    public ICommandLogger CommandLogger { get; set; } = NullCommandLogger.Instance;
    private Position _cursorPosition = new(0, 0);
    public virtual Position CursorPosition 
    { 
        get
        {
            var cursorPosition = _cursorPosition;   
            if (cursorPosition is null)
            {
                foreach (var control in Controls.AsSnapshot())
                {
                    cursorPosition = control.CursorPosition;
                    if (cursorPosition is not null)
                    {
                        break;
                    }
                }
            }
            return cursorPosition!;
        }
        set
        {
            if (_cursorPosition != value)
            {
                _cursorPosition = value;
                OnPropertyChanged(nameof(CursorPosition));
            }
        }
    } 
    private bool _isCursorVisible = false;
    public virtual bool IsCursorVisible 
    { 
        get
        {
            // `IsCursorVisible` indicates whether this control (or any child) wants the cursor to be visible.
            // Focus is handled elsewhere (e.g., when determining the cursor position).
            // This allows Desktop-level logic to enable the cursor whenever any control requests it.
            var isCursorVisible = _isCursorVisible && IsEnabled;
            foreach (var control in Controls.AsSnapshot())
            {
                isCursorVisible = isCursorVisible || control.IsCursorVisible;
                if (isCursorVisible)
                {
                    break;
                }
            }
            return isCursorVisible;
        }
        set
        {
            if (_isCursorVisible != value)
            {
                _isCursorVisible = value;
                if (!_isCursorVisible)
                {
                    CursorPosition = null!;
                }
                OnPropertyChanged(nameof(IsCursorVisible));
            }
        }
    }

    public Control(IThemeService themeService)
    {
        // Some tests (reflection-based instantiation) may pass null for services.
        // Use a null-object implementation to avoid NullReferenceException during construction
        // and allow types to be instantiated without a fully wired DI container.
        ThemeService = themeService ?? Services.NullThemeService.Instance;
        Name = GetType().FullName ?? string.Empty;
        PropertyChanged += (sender, e) => StateHasChanged();
    }

    public virtual async Task OnInitializeAsync()
    {
        OnInitializeZOrder();
        OnInitializeLayout();

        foreach (var control in Controls.AsSnapshot())
        {
            await control.OnInitializeAsync();
        }

        OnInitializeCommands();
        StateHasChanged();
    }

    public virtual void OnInitializeZOrder()
    {
        ZOrder = Parent?.ZOrder ?? 0;
        foreach (var control in Controls.AsSnapshot())
        {
            control.OnInitializeZOrder();
        }
        StateHasChanged();
    }

    public virtual void OnInitializeLayout()
    {
        foreach (var control in Controls.AsSnapshot())
        {
            control.OnInitializeLayout();
        }
        StateHasChanged();
    }

    public virtual void OnInitializeCommands()
    {
        foreach (var control in Controls.AsSnapshot())
        {
            control.OnInitializeCommands();
        }
    }

    public bool IsParentVisible()
    {
        return (Parent == null) ? _isVisible : _isVisible && Parent.IsParentVisible();
    }

    protected virtual void OnIsVisibleChanged()
    {
    }

    protected virtual void OnHasStateChangedChange(bool hasStateChanged)
    {
    }

    public virtual void OnWindowSizeChanged(object? sender, Size size)
    {
        Debug.WriteLine($"Window size changed to [{size.Width},{size.Height}]");

        // Apply new size first so layout/draw uses the actual console dimensions.
        ViewSize = size;

        OnWindowResize(size);
        foreach (var control in Controls.AsSnapshot())
        {
            control.ViewSize = size;
            control.OnWindowResize(size);
        }
        StateHasChanged();
    }

    public void AddControl(IControl control, string name = "")
    {
        control.Parent = this;
        if (!string.IsNullOrEmpty(name))
        {
            control.Name = name;
        }
        Controls.Add(control);
        InvalidateTree();
    }

    #region INotifyPropertyChanged
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
    #endregion

    #region Render
    public virtual bool Render(IBuffer buffer)
    {
        bool isIdle = true;
        // A hidden control draws nothing, and neither do its children (they are hidden with it).
        // It used to draw whenever it was marked changed — and every repaint from scratch marks the
        // whole tree — so the status bar's hidden date pop-up wrote its blank rectangle into layer 1
        // on every repaint. Nothing showed it until a menu was opened, which lifts the drop-down to
        // that same layer: the layer was composited, blanks and all, and the panels vanished under
        // an open menu. The change flag is kept, so the control draws once it is shown.
        if (!IsVisible)
        {
            return isIdle;
        }
        if (HasStateChanged)
        {
            try
            {
                Draw(buffer);
                isIdle = false;
                HasStateChanged = false;
            }
            catch (Exception exception)
            {
                Debug.WriteLine("Error rendering {Message}", exception.Message);
            }
        }

        foreach (var control in Controls.AsSnapshot())
        {
            if (control is null) continue;
            isIdle = isIdle && control.Render(buffer);
        }

        return isIdle;
    }

    public virtual void GetControlsToRender(HashSet<IControl> controlsToRender, IEnumerable<int> layersToRefresh)
    {
        if ((layersToRefresh.Contains(ZOrder)) || (HasStateChanged && IsVisible))
        {
            controlsToRender.Add(this);
        }

        foreach (var control in Controls.AsSnapshot())
        {
            control.GetControlsToRender(controlsToRender, layersToRefresh);
        }
    }

    public virtual void Draw(IBuffer buffer)
    {
        if (IsVisible)
        {
            var controls = Controls.AsSnapshot();
            foreach (var control in controls
                .Where(c => c is not null && c.IsVisible)
                .ToList())
            {
                try
                {
                    Dump.AppendSessionTrace(
                        $"control:draw:start;parent={GetType().FullName};child={control.GetType().FullName};pos={control.AbsolutePosition.X},{control.AbsolutePosition.Y};size={control.ViewSize.Width}x{control.ViewSize.Height}");
                    control.Draw(buffer);
                    Dump.AppendSessionTrace(
                        $"control:draw:done;parent={GetType().FullName};child={control.GetType().FullName}");
                }
                catch (Exception exception)
                {
                    Dump.AppendError(exception);
                    Dump.AppendSessionTrace(
                        $"control:draw:error;parent={GetType().FullName};child={control.GetType().FullName};type={exception.GetType().FullName};message={exception.Message}");
                    control.Refresh();
                }
            }
        }
    }

    protected void StateHasChanged()
    {
        if (IsVisible)
        {
            // Debug.WriteLine($"{Name} StateHasChanged");
            HasStateChanged = true;
        }
        GetRootControl().Refresh(ZOrder + 1);
    }

    public void Refresh(int zOrder)
    {
        if (ZOrder >= zOrder && IsVisible)
        {
            HasStateChanged = true;
        }
        foreach (var control in Controls.AsSnapshot())
        {
            if (control is null) continue;
            control.Refresh(zOrder);
        }
    }
    #endregion

    public void OnWindowResize(Size size)
    {
        OnInitializeLayout();
        StateHasChanged();
    }

    public virtual async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        foreach (var control in Controls.AsSnapshot().Where(c => c.IsFocused))
        {
            var isHandled = await control.HandleKeyEventAsync(keyEvent);
            if (isHandled)
            {
                StateHasChanged();
                return true;
            }
        }
        if (IsEnabled)
        {
            var isHandled = await HandleHotKeyEventAsync(keyEvent);
            if (isHandled)
            {
                return true;
            }

            foreach (var command in Commands.AsSnapshot())
            {
                isHandled = await command.HandleKeyEventAsync(keyEvent);
                if (isHandled)
                {
                    StateHasChanged();
                    return isHandled;
                }
            }
        }
        foreach (var control in Controls.AsSnapshot())
        {
            var isHandled = await control.HandleKeyEventAsync(keyEvent);
            if (isHandled)
            {
                StateHasChanged();
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// The modal control the keyboard belongs to: the <b>last</b> one in the tree, not the first.
    /// Children are drawn in list order, so the last modal is the one the user is actually looking
    /// at — a dialog opened over a dialog (F1 help over the folder selector, say) is a *sibling*
    /// attached to the root, listed after the dialog that opened it, never inside it. Read
    /// first-modal-wins, every key went to the dialog underneath instead: the overlay on screen
    /// ignored Escape while the one hidden behind it closed.
    /// </summary>
    public IControl? GetModalChildControl()
    {
        foreach (var control in Controls.AsSnapshot().Reverse())
        {
            // Look inside before taking the control itself: anything nested is drawn after it.
            var modalControl = control.GetModalChildControl();
            if (modalControl is not null)
            {
                return modalControl;
            }
            if (control.IsModal)
            {
                return control;
            }
        }
        return null;
    }

    public void SetFocus()
    {
        if (CanFocus)
        {
            if (!IsFocused)
            {
                var root = GetRootControl();
                root.ResetFocus();
                IsFocused = true;
                Parent?.SetParentFocus();
                StateHasChanged();
            }
        }
        else if (HasFocusableControl(out var focusableControl))
        {
            if ((focusableControl is not null) && (!focusableControl.IsFocused))
            {
                focusableControl.SetFocus();
            }
        }
        OnFocusChanged();
    }

    public void SetParentFocus()
    {
        IsFocused = true;
        OnFocusChanged();
        Parent?.SetParentFocus();
        StateHasChanged();
    }

    protected virtual void OnFocusChanged()
    {
        IsCursorVisible = CanFocus && IsFocused && IsVisible && IsEnabled;
        Debug.WriteLine($"Focus changed to {Name} {IsFocused}");
    }

    public IControl GetRootControl()
    {
        if (Parent != null)
        {
            return Parent.GetRootControl();
        }
        else
        {
            return this;
        }
    }

    public void ResetFocus()
    {
        if (IsFocused)
        {
            IsFocused = false;
        }
        OnFocusChanged();
        foreach (var control in Controls.AsSnapshot())
        {
            control.ResetFocus();
        }
        StateHasChanged();
    }

    public virtual VisualState GetVisualState()
    {
        var visualState = VisualState.Normal;
        if (IsFocused && IsEnabled)
        {
            visualState = VisualState.Focused;
        }
        else if (!IsEnabled)
        {
            visualState = VisualState.Disabled;
        }
        return visualState;
    }

    public void AddCommand(ICommand command)
    {
        command.Control = this;
        command.CommandLogger = CommandLogger;
        Commands.Add(command);
    }

    public void SetCommandLogger(ICommandLogger commandLogger)
    {
        CommandLogger = commandLogger;
        foreach (var command in Commands.AsSnapshot())
        {
            command.CommandLogger = commandLogger;
        }
        foreach (var control in Controls.AsSnapshot())
        {
            control.SetCommandLogger(commandLogger);
        }
    }

    public IControl? GetFocusedControl()
    {
        IControl focusedControl = this;
        foreach (var control in Controls.AsSnapshot())
        {
            var childFocusedControl = control.GetFocusedControl();
            if (childFocusedControl is not null && 
                childFocusedControl.CanFocus && 
                childFocusedControl.IsFocused)
            {
                focusedControl = childFocusedControl;
                break;
            }
        }
        return focusedControl;
    }

    public IControl? GetNextFocusableControl(IControl focusedControl)
    {
        var nextControl = focusedControl;
        var controlList = Controls.AsSnapshot();
        var index = controlList.IndexOf(focusedControl);
        var orderedList = controlList.Skip(index + 1).ToList();
        orderedList.AddRange(controlList.Take(index));
        foreach (var control in orderedList)
        {
            if (control.CanFocus)
            {
                nextControl = control;
                break;
            }
        }
        if (nextControl == focusedControl)
        {
            nextControl = Parent?.GetNextFocusableControl(this) ?? this;
        }
        return nextControl;
    }

    public bool HasFocusableControl(out IControl? focusableControl)
    {
        foreach (var control in Controls.AsSnapshot())
        {
            if (control.CanFocus)
            {
                focusableControl = control;
                return true;
            }
            if (control.HasFocusableControl(out focusableControl))
            {
                return true;
            }
        }
        focusableControl = null;
        return false;
    }

    public IControl? GetPreviousFocusableControl(IControl focusedControl)
    {
        var previousControl = focusedControl;
        var controlList = Controls.AsSnapshot().Reverse().ToList();
        var index = controlList.IndexOf(focusedControl);
        var orderedList = controlList.Skip(index + 1).ToList();
        orderedList.AddRange(controlList.Take(index));
        foreach (var control in orderedList)
        {
            if (control.CanFocus)
            {
                previousControl = control;
                break;
            }
        }
        if (previousControl == focusedControl)
        {
            previousControl = Parent?.GetPreviousFocusableControl(this) ?? this;
        }
        return previousControl;
    }

    /// <summary>
    /// The controls Tab visits below this one, in the order they are listed: those that can take the focus, are enabled
    /// and are on screen. A container that cannot take the focus itself is looked into — a group box or a form panel is
    /// not a stop, what is inside it is — while a control that can take it is one stop, whatever it holds.
    /// </summary>
    public IEnumerable<IControl> GetTabStops()
    {
        foreach (var control in Controls.AsSnapshot())
        {
            if (control is null || !control.IsVisible)
            {
                continue;
            }

            if (control.CanFocus)
            {
                if (control.IsEnabled)
                {
                    yield return control;
                }

                continue;
            }

            foreach (var inner in control.GetTabStops())
            {
                yield return inner;
            }
        }
    }

    private void MoveFocus(int direction)
    {
        var stops = GetTabStops().ToList();
        if (stops.Count == 0)
        {
            return;
        }

        var current = GetFocusedControl();
        var index = current is null ? -1 : stops.IndexOf(current);
        var next = index < 0
            ? (direction > 0 ? stops[0] : stops[^1])
            : stops[(index + direction + stops.Count) % stops.Count];
        if (ReferenceEquals(next, current))
        {
            return;
        }

        current?.ResetFocus();
        next.SetFocus();
    }

    public virtual void OnHandleTabForward() => MoveFocus(1);

    public virtual void OnHandleTabBackward() => MoveFocus(-1);

    #region 
    public Task AutoRefreshTask { get; set; } = Task.CompletedTask;
    private ConcurrentStack<IControl>? _focusedStack;

    /// <summary>
    /// Where a dialog or modal popup remembers who had focus, to give it back on close. One per
    /// <i>root</i>: it used to be a single static stack for the whole process, so two screens — or two
    /// tests, each running an app of its own — pushed and popped each other's entries, and a dialog
    /// closing in one could hand focus to a control in the other. A dialog keeps its <c>Parent</c>
    /// after it is removed, so the close resolves the same root its show pushed to.
    /// </summary>
    protected ConcurrentStack<IControl> FocusedStack
    {
        get
        {
            var root = GetRootControl() as Control ?? this;
            return LazyInitializer.EnsureInitialized(ref root._focusedStack);
        }
    }

    protected void StartAutoRefresh()
    {
        AutoRefreshTask = Task.Run(async () =>
        {
            while (true)
            {
                await Task.Delay(100);
                Refresh();
            }
        });
    }

    public virtual void Refresh()
    {
        if (IsVisible)
        {
            StateHasChanged();
        }
        foreach (var control in Controls.AsSnapshot())
        {
            if (control is null) continue;
            control.Refresh();
        }
    }
    #endregion

    public static string ProgressChar()
    {
        const string progressChars = "-\\|/";
        var now = DateTime.Now.Second;
        var progressChar = progressChars[now % progressChars.Length];
        return progressChar.ToString();
    }

    public void RemoveControl(IControl control)
    {
        Controls.Remove(control);
        InvalidateTree();
    }

    #region Dialog
    public virtual void Close()
    {
        OnBeforeClose();
        Hide();

        foreach (var control in Controls.AsSnapshot())
        {
            control.Close();
        }

        GetRootControl()?.Refresh();
    }

    protected virtual void OnBeforeClose()
    {
    }

    public async Task ShowDialogAsync(Dialog dialog, IControl? parent = null)
    {
        var focusedControl = GetRootControl().GetFocusedControl();
        if (focusedControl is not null)
        {
            FocusedStack.Push(focusedControl);
        }
        parent ??= GetRootControl();
        dialog.Parent = parent;
        dialog.ZOrder = ZOrder + 1;
        await dialog.ShowAsync();
        //await dialog.OnInitializeAsync();
        parent.AddControl(dialog);
    }

    public async Task ShowPopUpWindowAsync(PopUpWindow popUpWindow)
    {
        popUpWindow.IsEnabled = true;
        popUpWindow.IsVisible = true;
        var rootControl = GetRootControl();
        popUpWindow.Parent = rootControl;
        await popUpWindow.OnInitializeAsync();
        rootControl.AddControl(popUpWindow);
    }

    public virtual async Task<bool> HandleMouseEventAsync(MouseEvent mouseEvent)
    {
        var isHandled = false;

        foreach (var control in Controls.AsSnapshot())
        {
            isHandled = await control.HandleMouseEventAsync(mouseEvent);
            if (isHandled)
            {
                break;
            }
        }

        return isHandled;
    }
    #endregion

    #region HotKey

    public virtual char? HotKeyChar { get; set; }
    public virtual ConsoleKey? HotConsoleKey { get; set; }

    public virtual async Task<bool> HandleHotKeyEventAsync(KeyEvent keyEvent)
    {
        if (HotKeyChar is not null)
        {
            if ((HotKeyChar.Value.ToString()).Equals(keyEvent.ConsoleKey.ToString(), StringComparison.InvariantCultureIgnoreCase)
                && keyEvent.Control)
            {
                await TriggerHotKeyAsync();
                return true;
            }
        }
        return false;
    }

    public virtual Task TriggerHotKeyAsync()
    {
        SetFocus();
        return Task.CompletedTask;
    }
    #endregion

    /// <summary>
    /// Describes key bindings this control handles directly in <see cref="HandleKeyEventAsync"/>
    /// rather than via an <see cref="ICommand"/> (e.g. contextual arrow-key navigation), purely so
    /// they can be surfaced in help/documentation. Empty by default.
    /// </summary>
    public virtual IEnumerable<(string Key, string Description)> GetKeyHints() => [];

    #region Idle
    public virtual void OnIdle()
    {
        foreach (var control in Controls)
        {
            try
            {
                control.OnIdle();
            }
            catch
            {
            }
        }
    }
    #endregion

    #region Show & Hide
    public virtual async Task ShowAsync()
    {
        IsVisible = true;
        await OnInitializeAsync();
        foreach (var control in Controls.AsSnapshot())
        {
            await control.ShowAsync();
        }
        StateHasChanged();
    }

    public virtual void Hide()
    {
        IsVisible = false;
        foreach (var control in Controls.AsSnapshot())
        {
            control?.Hide();
        }
        // Refresh from z=0 upward so that all underlying visible controls
        // (especially the base layer panels) are marked for re-render.
        // Without this, only ZOrder > this.ZOrder controls would be refreshed,
        // leaving stale / black pixels where the hidden control used to be until
        // the periodic ForceRedraw timer fires.
        GetRootControl().Refresh(0);
    }
    #endregion

    #region Border
    protected void DrawBorder(IBuffer buffer, int zOrder,
        Position position, Size size,
        string header, string footer,
        ColorScheme colorScheme, VisualState visualState)
    {
        GetColor(colorScheme, visualState, out var foreground, out var background);
        var line = Border.TopLeft +
                   Border.Horizontal.Repeat((int)size.Width - 2) +
                   Border.TopRight;
        line = StringHelper.AlignCenter(line, header);
        var lineString = new ConxelString(line, foreground, background);
        buffer.Write(zOrder, position, size, lineString);
        var vertical = new ConxelString(Border.Vertical, foreground, background);
        for (uint y = 1; y < size.Height - 1; y++)
        {
            buffer.Write(zOrder, new Position(position.X, y + position.Y), SizeHelper.Size1x1, vertical);
            buffer.Write(zOrder, new Position(position.X + size.Width - 1, y + position.Y), SizeHelper.Size1x1, vertical);
        }
        line = Border.BottomLeft +
                   Border.Horizontal.Repeat((int)size.Width - 2) +
                   Border.BottomRight;
        line = StringHelper.AlignCenter(line, footer);
        lineString = new ConxelString(line, foreground, colorScheme.Background);
        buffer.Write(zOrder, 
            new Position(position.X, position.Y + size.Height - 1), 
            new Size(lineString.Count, 1), 
            lineString);
    }

    public virtual void GetColor(ColorScheme colorScheme, VisualState visualState, out Color foreground, out Color background)
    {
        switch (visualState)
        {
            case VisualState.Normal:
                {
                    foreground = colorScheme.Foreground;
                    background = colorScheme.Background;
                    break;
                }
            case VisualState.Selected:
                {
                    foreground = colorScheme.SelectedForeground;
                    background = colorScheme.SelectedBackground;
                    break;
                }
            case VisualState.SelectedDisabled:
                {
                    // The selected row of a disabled control keeps the disabled look, as ListBox and SegmentedControl give it.
                    foreground = colorScheme.DisabledSelectedForeground;
                    background = colorScheme.DisabledBackground;
                    break;
                }
            case VisualState.Disabled:
                {
                    foreground = colorScheme.DisabledForeground;
                    background = colorScheme.DisabledBackground;
                    break;
                }
            default:
                {
                    foreground = colorScheme.Foreground;
                    background = colorScheme.Background;
                    break;
                }
        }
    }
    #endregion

    public int GetMaxZOrder()
    {
        var maxZOrder = ZOrder;
        foreach (var control in Controls.AsSnapshot().Where(c => c.IsVisible))
        {
            maxZOrder = Math.Max(maxZOrder, control.GetMaxZOrder());
        }
        return maxZOrder;
    }
}
