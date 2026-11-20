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
        Assert.IsNull(model.UpdateTask, "the opening update is not a keystroke's: Tab does not wait for it or complete from it");
        for (int i = 0; i < 100 && Volatile.Read(ref model.Updates) == 0; i++)
        {
            await Task.Delay(20);
        }

        Assert.AreEqual(1, model.Updates, "but it did start, off this thread");

        model.Gate.Set();
        for (int i = 0; i < 100 && model.MatchingValues.Count == 0; i++)
        {
            await Task.Delay(20);
        }

        Assert.AreEqual(1, model.MatchingValues.Count, "and it landed");
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
        for (int i = 0; i < 100 && model.MatchingValues.Count == 0; i++)
        {
            await Task.Delay(20);
        }

        await Task.Delay(100);
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

    /// <summary>Blocks only its first call (the opening update) until released, so a later keystroke's update can finish first.</summary>
    private sealed class OpeningIsSlowModel : IBlockingCompletionModel
    {
        private int _calls;
        public int Finished;

        public ManualResetEventSlim OpeningGate { get; } = new();

        public Task? UpdateTask { get; set; }

        public ConcurrentObservableCollection<string> MatchingValues { get; } = [];

        public IEnumerable<string> Completions => ["one"];

        public void InsertCompletion(string value)
        {
        }

        public void UpdateMatchingValues(string value)
        {
            if (Interlocked.Increment(ref _calls) == 1)
            {
                OpeningGate.Wait(TimeSpan.FromSeconds(30));
            }

            MatchingValues.ReplaceAll(["one"]);
            Interlocked.Increment(ref Finished);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AnOpeningUpdateThatFinishesAfterAKeystrokeDoesNotCloseTheList()
    {
        var theme = Theme();
        var model = new OpeningIsSlowModel();
        var textModel = new TextModel("pre");
        var autoCompletion = new AutoCompletion(textModel, model, theme.Object) { ViewSize = new Size(20, 5) };

        // The opening update is still running (blocked) when the user types.
        await autoCompletion.OnInitializeAsync();
        textModel.TriggerPropertyChanged();
        await model.UpdateTask!;

        // Now the opening update lands last. It kept the list down, but its result is older than the keystroke's.
        model.OpeningGate.Set();
        for (int i = 0; i < 100 && Volatile.Read(ref model.Finished) < 2; i++)
        {
            await Task.Delay(20);
        }

        await Task.Delay(200);
        autoCompletion.OnIdle();
        Assert.IsTrue(autoCompletion.IsVisible, "the keystroke opened the list, and the older opening update must not close it again");
        model.OpeningGate.Dispose();
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
