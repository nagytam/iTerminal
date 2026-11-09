using iTerminal.Core.AI;
using iTerminal.Core.Dialogs;
using Microsoft.Extensions.DependencyInjection;

namespace iTerminal.Test;

[TestClass]
public class AiActionApprovalTest
{
    [Timeout(120_000)]
    [TestMethod]
    public async Task ApprovalDialog_ShowsActionsAndRequiresAnExplicitAnswer()
    {
        await using TerminalApp app = await TerminalApp.StartAsync();
        IAiActionApprovalService approvals = app.Driver.GetRequiredService<IAiActionApprovalService>();

        Task<bool> approved = approvals.ConfirmAsync(new AiActionApprovalRequest(
            "Review this delete",
            ["Delete target: target-to-delete.txt"]));
        await app.Driver.WaitForModalAsync<AiActionApprovalDialog>();
        await app.WaitForTextAsync("target-to-delete.txt");
        await app.PressAsync("y");
        Assert.IsTrue(await approved);
        await app.WaitForNoDialogAsync();

        Task<bool> rejected = approvals.ConfirmAsync(new AiActionApprovalRequest(
            "Review this synchronization",
            ["Copy source.txt to target/source.txt"]));
        await app.Driver.WaitForModalAsync<AiActionApprovalDialog>();
        await app.WaitForTextAsync("source.txt");
        await app.PressAsync("Esc");
        Assert.IsFalse(await rejected);
        await app.WaitForNoDialogAsync();
    }
}
