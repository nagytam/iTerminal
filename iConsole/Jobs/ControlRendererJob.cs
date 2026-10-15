using iConsole.Controls;
using iConsole.Helper;

namespace iConsole.Jobs;

public class ControlRendererJob() : Job
{
    // Ensure changed controls are refreshed within 1 second under all circumstances
    private static readonly TimeSpan FullRedrawFallbackInterval = TimeSpan.FromSeconds(1);

    public override JobPriority Priority => JobPriority.Rendering;

    private long _renderedTreeVersion = -1;

    protected override Task RunAsync(CancellationToken cancellationToken)
    {
        var lastRedraw = DateTime.Now;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var now = DateTime.Now;
                if (now - lastRedraw >= FullRedrawFallbackInterval)
                {
                    ForceRedraw();
                    lastRedraw = now;
                }
                var isIdle = Render();
                if (isIdle)
                {
                    Thread.Sleep(1);
                    Desktop?.OnIdle();
                }
            }
            catch
            {
            }
        }
        return Task.CompletedTask;
    }

    private void ForceRedraw()
    {
        if (Desktop != null)
        {
            foreach (var screen in Desktop.Screens)
            {
                screen.GetRootControl().Refresh();
            }
        }
    }

    protected bool Render()
    {
        bool isIdle = true;

        var modalControl = Desktop?.GetModalChildControl();
        if (Desktop is not null && Desktop.Buffer is not null && modalControl is not null)
        {
            if (Desktop.Dump.IsEnabled)
            {
                Desktop.Dump.AppendSessionTrace($"control-render:start;screens={Desktop.Screens.Count};modal={modalControl.GetType().FullName}");
            }

            // Read before walking the tree: a change made while this pass runs is picked up by the next.
            var treeVersion = Control.TreeVersion;
            var treeChanged = treeVersion != _renderedTreeVersion;
            _renderedTreeVersion = treeVersion;

            foreach (var screen in Desktop.Screens.AsSnapshot())
            {
                var controlsToRender = new HashSet<IControl>();
                var maxZOrder = screen.GetMaxZOrder();
                var newLayers = Desktop.Buffer.UpdateLayers(maxZOrder);
                if (treeChanged)
                {
                    RepaintFromScratch(screen);
                }
                screen.GetControlsToRender(controlsToRender, newLayers);
                var renderList = controlsToRender.ToList();
                if (Desktop.Dump.IsEnabled)
                {
                    Desktop.Dump.AppendSessionTrace(
                        $"control-render:screen={screen.GetType().FullName};layers={Desktop.Buffer.Layers.Count};controls={renderList.Count};cursor-visible={screen.IsCursorVisible}");
                }

                bool didRender = false;
                if (renderList.Count > 0)
                {
                    foreach (var control in renderList)
                    {
                        var controlIdle = control?.Render(Desktop.Buffer) ?? true;
                        if (!controlIdle)
                        {
                            didRender = true;
                        }
                        isIdle = isIdle && controlIdle;
                    }
#if DEBUG
                    // BufferHelper.DumpBufferToHtml(Desktop.Buffer.Layers.First(), $"c:\\temp\\iConsole\\{DateTime.Now:yyyy-MM-dd_HH-mm-ss_fff}.html");
#endif

                }
                #region Cursor
                var isCursorVisible = screen.IsCursorVisible;
                if (isCursorVisible)
                {
                    var cursorPosition = screen.CursorPosition;
                    Desktop.Buffer.SetCursorPosition(
                        cursorPosition,
                        isCursorVisible);
                }
                #endregion

                // Only merge when at least one control was actually re-rendered.
                // Merging when only new (still-blank) layers were allocated would overwrite
                // the MergedLayer with empty content, causing a blank screen on startup.
                // New layers without rendered content will be picked up on the next cycle
                // once their controls set HasStateChanged = true.
                if (didRender)
                {
                    lock (Desktop.Buffer.SyncLock)
                    {
                        Desktop.Buffer.Merge();
                    }

                    var renderedControlNames = renderList.Select(control => control?.GetType().FullName ?? "<null>").ToArray();
                    Desktop.ScreenLogger.Capture(
                        $"control-render:{screen.GetType().Name}",
                        Desktop.Buffer.MergedLayer,
                        renderedControlNames);

                    if (Desktop.Dump.IsEnabled)
                    {
                        var visibleCharacters = RenderingDumpHelper.CountVisibleCharacters(Desktop.Buffer.MergedLayer);
                        Desktop.Dump.AppendSessionTrace(
                            $"control-render:merged;screen={screen.GetType().FullName};size={Desktop.Buffer.MergedLayer.Size.Width}x{Desktop.Buffer.MergedLayer.Size.Height};non-space={visibleCharacters}");
                        Desktop.Dump.DumpBufferStage(
                            "buffer-control-render",
                            Desktop.Buffer.MergedLayer,
                            renderedControlNames);

                        if (visibleCharacters == 0)
                        {
                            Desktop.Dump.AppendSessionTrace("control-render:warning;merged-buffer-is-empty");
                        }
                    }
                }
            }
        }

        return isIdle;
    }

    /// <summary>
    /// Wipes every layer and marks the whole tree for repaint, after a control was attached, detached,
    /// shown or hidden. Nothing else ever clears a layer, and a layer is composited over the ones
    /// below across the whole rectangle it has been written to — so without this a closed dialog
    /// stayed on screen whenever anything else still drew on its Z-order, and a window opened later
    /// appeared on top of the ghost of the last one. Nothing is merged until the repaint has happened
    /// in this same pass, so the wiped layers are never seen.
    /// </summary>
    private void RepaintFromScratch(iConsole.Screens.IScreen screen)
    {
        lock (Desktop!.Buffer.SyncLock)
        {
            foreach (var layer in Desktop.Buffer.Layers.AsSnapshot())
            {
                layer.Clear();
            }
        }
        screen.GetRootControl().Refresh();
    }
}
