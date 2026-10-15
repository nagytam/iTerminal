using iConsole.Buffers;
using iConsole.Commands;
using iConsole.Controls.Buttons;
using iConsole.Data;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Layouts;
using iConsole.Services;

namespace iConsole.Controls.PopUpWindows;

public class PopUpWindow : Window
{
    private IThemeService _themeService { get; set; }
    public override string Title { get; set; }

    public PopUpWindow(string title, IThemeService themeService)
        : base(themeService)
    {
        _themeService = themeService;
        Title = title;
        IsVisible = false;
    }

    public override async Task OnInitializeAsync()
    {
        await base.OnInitializeAsync();

        OnInitializeActionButtons();
        foreach (var popUpWindows in Controls.AsSnapshot()
            .Where(c => c is PopUpWindow)
            .Cast<PopUpWindow>())
        {
            popUpWindows.OnInitializeActionButtons();
        }
    }

    public virtual void OnInitializeActionButtons()
    {
        AddActionButton(new ActionButton("Hide", "[X]", new HideCommand(), _themeService));
    }

    protected void AddActionButton(ActionButton actionButton)
    {
        if (!Controls.AsSnapshot().Any(c => 
            c is ActionButton && 
            (c as ActionButton)?.Title == actionButton.Title))
        {
            AddControl(actionButton);
        }
    }

    public override void OnInitializeZOrder()
    {
        // A PopUpWindow should be above its parent when present. In unit tests
        // the dialog may not be added to a parent control, so avoid throwing
        // when Parent is null and default to ZOrder 1 in that case.
        ZOrder = (Parent?.ZOrder + 1) ?? 1;
        foreach (var control in Controls.AsSnapshot())
        {
            control.OnInitializeZOrder();
        }
        StateHasChanged();
    }

    /// <summary>
    /// Sets <see cref="Control.ViewSize"/> to <paramref name="size"/> and centers this window on
    /// <see cref="Control.Parent"/>, clamping to (0,0) when the window is larger than its parent
    /// (e.g. a fixed-size dialog opened in a narrower/shorter terminal) so Position's non-negative
    /// invariant is never violated.
    /// </summary>
    protected void CenterInParent(Size size)
    {
        ViewSize = size;
        Layout.Center(this);
    }

    protected string GetActionButtons()
    {
        var actionButtons = string.Empty;
        foreach (var actionButton in Controls.ToList().Where(c => c is ActionButton).Cast<ActionButton>())
        {
            actionButtons += actionButton.ActionView;
        }
        return actionButtons;
    }

    protected override void DrawChrome(IBuffer buffer)
    {
        var colorScheme = _themeService.CurrentColorScheme;
        var visualState = GetVisualState();
        var headerLine = " " + Title + " ";
        var actionButtons = GetActionButtons();
        headerLine = headerLine.Expand(Border.Horizontal, (int)ViewSize.Width - 2 - actionButtons.Length) + actionButtons;
        Buffers.Buffer.Clear(buffer, ZOrder, AbsolutePosition, ViewSize, colorScheme);
        DrawBorder(buffer, ZOrder, AbsolutePosition, ViewSize, headerLine, string.Empty, colorScheme, visualState);
    }
}