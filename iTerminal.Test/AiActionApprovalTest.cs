using iTerminal.Core.AI;
using iTerminal.Core.Dialogs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.AI;

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

    [Timeout(120_000)]
    [TestMethod]
    public async Task AiFolderCreation_WaitsForTheAppApprovalDialog()
    {
        await using TerminalApp app = await TerminalApp.StartAsync();
        AITool tool = app.Driver.GetRequiredService<AiPanelTools>().Create().Single(t => t.Name == "create_folder");
        AIFunction function = (AIFunction)tool;
        string target = Path.Combine(app.LeftRoot, "ai-approved");
        AIFunctionArguments arguments = new()
        {
            ["name"] = "ai-approved",
            ["panel"] = "left",
            ["confirmed"] = true,
        };
        Task<object?> invocation = function.InvokeAsync(arguments).AsTask();

        await app.Driver.WaitForModalAsync<AiActionApprovalDialog>();
        await app.WaitForTextAsync("ai-approved");
        Assert.IsFalse(Directory.Exists(target), "The tool must not write before the user approves in the app.");
        await app.PressAsync("y");

        StringAssert.Contains((await invocation)?.ToString() ?? string.Empty, "Created");
        Assert.IsTrue(Directory.Exists(target));
        await app.WaitForNoDialogAsync();
    }
}
