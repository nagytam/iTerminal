using iTerminal.Core.AI;

namespace iTerminal.Core.Test.AI;

[TestClass]
public class AiChatModelTest
{
    [TestMethod]
    public void ModelInfoStartsAsTheCheckingTextWithAProperEllipsis()
    {
        var model = new AiChatModel();

        Assert.AreEqual("checking…", model.ModelInfo);
        Assert.AreEqual(AiChatModel.CheckingModelInfo, model.ModelInfo);
    }
}
