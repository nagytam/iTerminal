using iConsole.Data;

namespace iConsole.Services;

/// <summary>
/// Remembers where the pointer is, so the <see cref="Controls.ToolTipHost"/> can work out what it is resting on. The input
/// consumer reports every pointer event here, before it is dispatched; nothing else needs to know about tooltips.
/// </summary>
public interface IToolTipService
{
    /// <summary>Where the pointer last moved, or null after a click or key dismissed the tooltip.</summary>
    Position? PointerPosition { get; }

    /// <summary>Changes whenever the pointer moves, is dismissed, or is reported again, so a reader can tell that something happened.</summary>
    long Version { get; }

    /// <summary>The pointer moved with no button held.</summary>
    void ReportPointer(Position position);

    /// <summary>A click, a key or a drag: hide any tooltip and wait for the pointer to move again.</summary>
    void Dismiss();
}
