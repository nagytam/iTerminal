using iTerminal.Core.Services;
using iTerminal.Core.Model;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace iTerminal.Core.Model.Test;

/// <summary>The remembered values behind a combo box: loaded from settings, saved back as one ';'-separated list, and matched as the user types.</summary>
[TestClass]
public class PersistedValuesCompletionModelTest
{
    private const string Key = "history";

    private static Mock<ISettingsService> Settings(string? stored = null)
    {
        var settings = new Mock<ISettingsService>();
        settings.Setup(s => s.Get(Key)).Returns(stored!);
        return settings;
    }

    [TestMethod]
    public void Load_SkipsBlankEntriesAndDuplicates()
    {
        var model = new PersistedValuesCompletionModel(Settings("alpha;;beta; ;alpha;").Object, Key);

        CollectionAssert.AreEqual(new[] { "alpha", "beta" }, model.Completions.ToList());
    }

    [TestMethod]
    public void Insert_AddsTheValueAndSavesTheWholeList()
    {
        var settings = Settings("alpha");
        var model = new PersistedValuesCompletionModel(settings.Object, Key);

        model.InsertCompletion("gamma");

        settings.Verify(s => s.Set(Key, "alpha;gamma"), Times.Once);
    }

    [TestMethod]
    public void Insert_OfAValueAlreadyKept_IsNotAddedTwice()
    {
        var settings = Settings("alpha;beta");
        var model = new PersistedValuesCompletionModel(settings.Object, Key);

        model.InsertCompletion("alpha");

        CollectionAssert.AreEqual(new[] { "alpha", "beta" }, model.Completions.ToList());
    }

    [TestMethod]
    public void Insert_OfABlankValue_IsNotRememberedOrSaved()
    {
        var settings = Settings("alpha");
        var model = new PersistedValuesCompletionModel(settings.Object, Key);

        model.InsertCompletion("   ");
        model.InsertCompletion(string.Empty);

        CollectionAssert.AreEqual(new[] { "alpha" }, model.Completions.ToList());
        settings.Verify(s => s.Set(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [TestMethod]
    public void Insert_OfAValueWithASemicolon_IsNotRemembered_SoItDoesNotSplitTheHistory()
    {
        // Stored as "a;b" it would read back as the two entries "a" and "b".
        var settings = Settings("alpha");
        var model = new PersistedValuesCompletionModel(settings.Object, Key);

        model.InsertCompletion("a;b");

        CollectionAssert.AreEqual(new[] { "alpha" }, model.Completions.ToList());
        settings.Verify(s => s.Set(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [TestMethod]
    public void UpdateMatchingValues_MatchesAnywhereIgnoringCase()
    {
        var model = new PersistedValuesCompletionModel(Settings("Documents;downloads;My Docs").Object, Key);

        model.UpdateMatchingValues("docs");

        CollectionAssert.AreEqual(new[] { "My Docs" }, model.MatchingValues.ToList());

        model.UpdateMatchingValues("DOC");

        CollectionAssert.AreEqual(new[] { "Documents", "My Docs" }, model.MatchingValues.ToList());
    }

    [TestMethod]
    public void UpdateMatchingValues_EmptyText_OffersEverything()
    {
        var model = new PersistedValuesCompletionModel(Settings("alpha;beta").Object, Key);

        model.UpdateMatchingValues(string.Empty);

        CollectionAssert.AreEqual(new[] { "alpha", "beta" }, model.MatchingValues.ToList());
    }
}
