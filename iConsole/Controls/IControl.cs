using iConsole.Buffers;
using iConsole.Commands;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Diagnostics;

namespace iConsole.Controls;

public interface IControl
{
    IControl? Parent { get; set; }
    ConcurrentList<IControl> Controls { get; }
    ICommandLogger CommandLogger { get; set; }
    int ZOrder { get; set; }
    bool IsModal { get; }
    bool CanFocus { get; }
    bool IsFocused { get; }
    bool IsEnabled { get; }
    bool IsVisible { get; set; }
    Position AbsolutePosition { get; }
    Size ViewSize { get; set; }
    Position Position { get; set; }
    string Name { get; set; }
    bool IsCursorVisible { get; }
    Position CursorPosition { get; }
    void AddCommand(ICommand command);
    void SetCommandLogger(ICommandLogger commandLogger);
    Task<bool> HandleKeyEventAsync(KeyEvent keyEvent);
    IControl? GetModalChildControl();
    void Draw(IBuffer buffer);
    bool Render(IBuffer buffer);
    void SetFocus();
    void OnWindowResize(Size size);
    void ResetFocus();
    void Refresh(int zOrder);
    Task OnInitializeAsync();
    void OnInitializeZOrder();
    void OnInitializeCommands();
    void OnInitializeLayout();
    bool IsParentVisible();
    bool HasFocusableControl(out IControl? focusableControl);
    IControl? GetFocusedControl();
    void OnHandleTabForward();
    void OnHandleTabBackward();
    void Refresh();
    void GetControlsToRender(HashSet<IControl> controlsToRender, IEnumerable<int> layersToRefresh);
    Task ShowDialogAsync(Dialog dialog, IControl? parent = null);
    void Close();
    void SetParentFocus();
    void RemoveControl(IControl control);
    IControl GetRootControl();
    IEnumerable<IControl> GetTabStops();
    IControl? GetNextFocusableControl(IControl control);
    IControl? GetPreviousFocusableControl(IControl control);
    void AddControl(IControl control, string name = "");
    Task<bool> HandleMouseEventAsync(MouseEvent mouseEvent);
    void OnIdle();
    Task ShowAsync();
    void Hide();
    int GetMaxZOrder();
}