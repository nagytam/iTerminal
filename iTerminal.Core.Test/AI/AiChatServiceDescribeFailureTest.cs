using iTerminal.Core.AI;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace iTerminal.Core.Test.AI;

/// <summary>
/// What the AI bar says when the availability check is refused. It used to be the status code
/// alone — "server error (401)" — which left the user to work out that an API key was missing.
/// </summary>
[TestClass]
public class AiChatServiceDescribeFailureTest
{
    [TestMethod]
    public void AMissingKeyNamesTheVariableToSet()
    {
        Assert.AreEqual("no API key — set ANTHROPIC_API_KEY",
            AiChatService.DescribeFailure(401, apiKeySet: false, "ANTHROPIC_API_KEY"));
    }

    [TestMethod]
    public void ARefusedKeySaysSo()
    {
        StringAssert.StartsWith(AiChatService.DescribeFailure(403, apiKeySet: true, "KEY"), "API key refused (403)");
    }

    [TestMethod]
    public void AServerFaultIsAServerError()
    {
        Assert.AreEqual("server error (502)", AiChatService.DescribeFailure(502, apiKeySet: true, "KEY"));
    }

    [TestMethod]
    public void AWrongUrlPointsAtTheUrl()
    {
        StringAssert.Contains(AiChatService.DescribeFailure(404, apiKeySet: true, "KEY"), "API URL");
    }
}
