using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace iTerminal.Core.Test.AI;

[TestClass]
public class AiChatServiceCurrentPathTest
{
    [Timeout(60_000)]
    [TestMethod]
    public async Task GetCurrentPath_ReportsThePanelThatWasFocusedNotTheOtherOne()
    {
        // The AI bar holds the focus while it is used, so no panel counts as focused: Url then falls back to the right
        // panel. The tool has to ask for the panel that was focused last, which is the one the user was working in.
        var left = new Mock<IContentModel>();
        left.SetupGet(m => m.Url).Returns(new Url(@"C:\left"));
        var contentModelService = new Mock<IContentModelService>();
        contentModelService.Setup(s => s.GetLastContentModel()).Returns(left.Object);
        contentModelService.SetupGet(s => s.Url).Returns(new Url(@"C:\right"));

        var service = new AiChatService(
            new Mock<IChatClient>().Object,
            contentModelService.Object,
            new Mock<IUiService>().Object,
            Options.Create(new AiOptions()));

        var tools = (List<AITool>)typeof(AiChatService)
            .GetField("_tools", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(service)!;
        var getCurrentPath = tools.OfType<AIFunction>().Single(f => f.Name == "get_current_path");

        var result = await getCurrentPath.InvokeAsync(new AIFunctionArguments());

        Assert.AreEqual(@"C:\left", result?.ToString());
    }
}
