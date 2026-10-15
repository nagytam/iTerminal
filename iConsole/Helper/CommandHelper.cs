using iConsole.Commands;
using iConsole.Controls;
using iConsole.Model;

namespace iConsole.Helper;

public static class CommandHelper
{
    public static void AddVerticalScrollingCommands(IControl control, IScrollableModel scrollableModel, bool includeHomeEnd)
    {
        ArgumentNullException.ThrowIfNull(control);

        control.AddCommand(new ScrollDownCommand(scrollableModel));
        control.AddCommand(new ScrollUpCommand(scrollableModel));
        control.AddCommand(new ScrollPageDownCommand(scrollableModel));
        control.AddCommand(new ScrollPageUpCommand(scrollableModel));
        if (includeHomeEnd)
        {
            control.AddCommand(new ScrollToHomeVerticalCommand(scrollableModel));
            control.AddCommand(new ScrollToEndVerticalCommand(scrollableModel));
        }
    }

    public static void AddHorizontalScrollingCommands(IControl control, IScrollableModel scrollableModel, ITextModel textModel, ISelectionTextModel selectionTextModel, bool isCursorEnabled)
    {
        ArgumentNullException.ThrowIfNull(control);

        control.AddCommand(new ScrollLeftCommand(isCursorEnabled, scrollableModel));
        control.AddCommand(new ScrollRightCommand(isCursorEnabled, scrollableModel));
        control.AddCommand(new ScrollToHomeHorizontalCommand(scrollableModel));
        control.AddCommand(new ScrollToEndHorizontalCommand(scrollableModel, textModel));
        control.AddCommand(new ScrollNextTokenCommand(isCursorEnabled, scrollableModel, textModel));
        control.AddCommand(new ScrollPreviousTokenCommand(isCursorEnabled, scrollableModel, textModel));
        control.AddCommand(new SelectLeftCommand(isCursorEnabled, scrollableModel, selectionTextModel));
        control.AddCommand(new SelectRightCommand(isCursorEnabled, scrollableModel, selectionTextModel));
    }
}
