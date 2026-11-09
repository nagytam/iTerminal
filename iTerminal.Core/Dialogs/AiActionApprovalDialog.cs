using iConsole.Controls;
using iConsole.Controls.Viewers;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Localization;
using iConsole.Services;
using iTerminal.Core.AI;

namespace iTerminal.Core.Dialogs;

public sealed class AiActionApprovalDialog : Dialog
{
    private readonly TextViewerControl _details;
    private readonly Label _hint;
    private readonly TaskCompletionSource<bool> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public AiActionApprovalDialog(AiActionApprovalRequest request, IThemeService themeService)
        : base(nameof(AiActionApprovalDialog), themeService)
    {
        Title = UiText.Get("Ai.Approval.Title", "Approve AI action?");

        _details = new TextViewerControl(false, themeService)
        {
            WrapLines = true,
        };
        _details.TextModel.Lines = [request.Summary, string.Empty, .. request.Actions];
        _hint = new Label(UiText.Get("Ai.Approval.Hint", "Y: approve    N/Esc: cancel"), themeService);

        AddControl(_details);
        AddControl(_hint);
    }

    public Task<bool> Result => _result.Task;

    public override void OnInitializeLayout()
    {
        base.OnInitializeLayout();
        if (Parent is not null)
        {
            long width = Math.Max(1, Math.Min(100, Parent.ViewSize.Width - 2));
            long height = Math.Max(1, Math.Min(30, Parent.ViewSize.Height - 2));
            CenterInParent(new Size(width, height));
        }

        long contentWidth = Math.Max(1, ViewSize.Width - 4);
        long detailsHeight = Math.Max(1, ViewSize.Height - 4);
        _details.Position = new Position(2, 1);
        _details.ViewSize = new Size(contentWidth, detailsHeight);
        _details.ScrollableModel.ViewSize = _details.ViewSize;
        _hint.Position = new Position(2, ViewSize.Height - 2);
        _hint.ViewSize = new Size(contentWidth, 1);
    }

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        yield return ("Y", UiText.Get("Ai.Approval.HintApprove", "Approve the displayed actions"));
        yield return ("N/Esc", UiText.Get("Ai.Approval.HintCancel", "Cancel without changes"));
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        if (!keyEvent.Control && !keyEvent.Alt && !keyEvent.Shift)
        {
            switch (char.ToLowerInvariant(keyEvent.KeyChar))
            {
                case 'y':
                    Answer(true);
                    return true;
                case 'n':
                    Answer(false);
                    return true;
            }

            if (keyEvent.ConsoleKey == ConsoleKey.Enter)
            {
                return true;
            }
        }

        return await base.HandleKeyEventAsync(keyEvent);
    }

    public override void Close()
    {
        _result.TrySetResult(false);
        base.Close();
    }

    private void Answer(bool approved)
    {
        _result.TrySetResult(approved);
        base.Close();
    }
}
