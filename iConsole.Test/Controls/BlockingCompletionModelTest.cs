using iConsole.Controls;
using iConsole.Data;
using iConsole.Extensions;
using iConsole.Model;
using iConsole.Services;
using Moq;

namespace iConsole.Test.Controls;

/// <summary>
/// A completion model that may do I/O (<see cref="IBlockingCompletionModel"/>) is never updated on the thread that
/// builds the box or initialises the completion: its first update goes to a background task, like every later one.
/// </summary>
[TestClass]
public class BlockingCompletionModelTest
{
    private sealed class RecordingModel : IBlockingCompletionModel
    {
        public int Updates;

        /// <summary>The update waits here, so one that ran on the caller's thread would never let the caller return.</summary>
        public ManualResetEventSlim Gate { get; } = new();

        public Task? UpdateTask { get; set; }

        public ConcurrentObservableCollection<string> MatchingValues { get; } = [];

        public IEnumerable<string> Completions => ["one"];

        public void InsertCompletion(string value)
        {
        }

        public void UpdateMatchingValues(string value)
        {
            Interlocked.Increment(ref Updates);
            Gate.Wait(TimeSpan.FromSeconds(30));
            MatchingValues.Clear();
            MatchingValues.Add("one");
        }
    }

    private static Mock<IThemeService> Theme()
    {
        var theme = new Mock<IThemeService>();
        theme.Setup(t => t.CurrentColorScheme).Returns(new iConsole.Display.ColorScheme());
        return theme;
    }

    private static TextBox Box(Mock<IThemeService> theme) =>
        new(new Mock<IBeepService>().Object, new Mock<IDateTimeProviderService>().Object, theme.Object) { Text = "prefilled" };

    [Timeout(60_000)]
    [TestMethod]
    public async Task BuildingTheBox_AndInitialisingTheCompletion_NeverRunTheModelOnTheCallingThread()
    {
        var theme = Theme();
        var model = new RecordingModel();

        TextBox box = Box(theme).WithAutoCompletion(model);
        Assert.AreEqual(0, model.Updates, "constructing the box did not touch the model");

        // The model is blocked: an update run inline would hold this call until the gate times out.
        Task init = box.AutoCompletion!.OnInitializeAsync();
        Assert.IsTrue(init.Wait(TimeSpan.FromSeconds(10)), "initialising returned while the model was still blocked");
        Task? update = model.UpdateTask;
        Assert.IsNotNull(update, "the first update is the model's UpdateTask, so a Tab pressed at once waits for it");
        Assert.IsFalse(update.IsCompleted);

        model.Gate.Set();
        await update;
        Assert.AreEqual(1, model.Updates, "the first update ran once");
        model.Gate.Dispose();
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheFirstUpdateKeepsTheListDown_AndTheNextKeystrokeOpensIt()
    {
        var theme = Theme();
        var model = new RecordingModel();
        model.Gate.Set();
        var textModel = new TextModel("pre");
        var autoCompletion = new AutoCompletion(textModel, model, theme.Object) { ViewSize = new Size(20, 5) };

        await autoCompletion.OnInitializeAsync();
        await model.UpdateTask!;
        autoCompletion.OnIdle();
        Assert.IsTrue(model.MatchingValues.Any(), "the opening text has matches");
        Assert.IsFalse(autoCompletion.IsVisible, "but the list stays down until something is typed");

        // A keystroke, which is what AutoCompleteCommand and the text box raise.
        textModel.TriggerPropertyChanged();
        await model.UpdateTask!;
        for (int i = 0; i < 100 && !autoCompletion.IsVisible; i++)
        {
            autoCompletion.OnIdle();
            await Task.Delay(20);
        }

        Assert.IsTrue(autoCompletion.IsVisible, "the keystroke's update is not suppressed");
        model.Gate.Dispose();
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AModelThatIsNotBlockingIsStillUpdatedWhenTheBoxIsBuilt()
    {
        var theme = Theme();
        var model = new CompletionModel(["one", "two"]);

        new TextBox(new Mock<IBeepService>().Object, new Mock<IDateTimeProviderService>().Object, theme.Object).WithAutoCompletion(model);

        CollectionAssert.AreEqual(new[] { "one", "two" }, model.MatchingValues.ToList());
    }
}
