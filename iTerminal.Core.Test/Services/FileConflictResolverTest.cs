using iConsole.Data;
using iConsole.Services;
using iTerminal.Core.Services;
using iTerminal.Core.Test.Helper;

namespace iTerminal.Core.Services.Test;

[TestClass]
public class FileConflictResolverTest
{
    private static readonly Url Source = new("/tmp/conflict-resolver/src.txt");
    private static readonly Url Target = new("/tmp/conflict-resolver/dst.txt");

    private static bool NothingExists(Url _) => false;

    private static async Task<T> ThrowsAsync<T>(Func<Task> action) where T : Exception
    {
        try
        {
            await action();
        }
        catch (T expected)
        {
            return expected;
        }
        Assert.Fail($"Expected {typeof(T).Name}.");
        return null!;
    }

    [TestMethod]
    public async Task OverwriteAll_NeverPrompts()
    {
        var prompts = new FakeConflictPromptService();
        var resolver = new FileConflictResolver(FileConflictMode.OverwriteAll, prompts);

        var decision = await resolver.DecideAsync(Source, Target, NothingExists, CancellationToken.None);

        Assert.IsTrue(decision.Replace);
        Assert.AreEqual(0, prompts.PromptCount);
    }

    [TestMethod]
    public async Task Legacy_NeverPrompts_AndThrowsAlreadyExists()
    {
        var prompts = new FakeConflictPromptService();
        var resolver = new FileConflictResolver(FileConflictMode.Legacy, prompts);

        var error = await ThrowsAsync<IOException>(() => resolver.DecideAsync(Source, Target, NothingExists, CancellationToken.None));

        StringAssert.Contains(error.Message, "already exists");
        Assert.AreEqual(0, prompts.PromptCount);
    }

    [TestMethod]
    public async Task ApplyToAll_CarriesTheAnswerToLaterConflicts()
    {
        var prompts = new FakeConflictPromptService(FileConflictChoice.Skip, applyToAll: true);
        var resolver = new FileConflictResolver(FileConflictMode.Ask, prompts);

        var first = await resolver.DecideAsync(Source, Target, NothingExists, CancellationToken.None);
        var second = await resolver.DecideAsync(Source, Target, NothingExists, CancellationToken.None);

        Assert.IsTrue(first.Skip);
        Assert.IsTrue(second.Skip);
        Assert.AreEqual(1, prompts.PromptCount);
    }

    [TestMethod]
    public async Task Cancel_InvokesCancelRun_AndThrows()
    {
        var prompts = new FakeConflictPromptService(FileConflictChoice.Cancel);
        var resolver = new FileConflictResolver(FileConflictMode.Ask, prompts);
        var cancelled = 0;
        resolver.CancelRun = () => cancelled++;

        await ThrowsAsync<OperationCanceledException>(() => resolver.DecideAsync(Source, Target, NothingExists, CancellationToken.None));

        Assert.AreEqual(1, cancelled);
    }
}
