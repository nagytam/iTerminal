using iTerminal.Core.Model.Contacts;
using iTerminal.Core.Services;
using Moq;

namespace iTerminal.Core.Test.Model.Contacts;

[TestClass]
public class ContactTest
{
    private static Contact Make(string name, string email = "", string phone = "", string company = "", string notes = "") =>
        new(Guid.NewGuid(), name, email, phone, company, notes);

    [Timeout(60_000)]
    [TestMethod]
    public void ANameIsRequired()
    {
        Assert.IsFalse(Make("  ").Validate(out var error));
        StringAssert.Contains(error, "name");
    }

    [Timeout(60_000)]
    [TestMethod]
    [DataRow("ada@example.com", true)]
    [DataRow("", true)]
    [DataRow("ada", false)]
    [DataRow("@example.com", false)]
    [DataRow("ada@", false)]
    [DataRow("ada@@example.com", false)]
    [DataRow("a da@example.com", false)]
    public void AnEmailNeedsAnAtWithTextOnBothSides(string email, bool valid) =>
        Assert.AreEqual(valid, Make("Ada", email).Validate(out _));

    [Timeout(60_000)]
    [TestMethod]
    [DataRow("+36 30 123-4567", true)]
    [DataRow("(020) 7946.0958", true)]
    [DataRow("", true)]
    [DataRow("call me", false)]
    [DataRow("+-()", false)]
    public void APhoneNumberNeedsADigitAndOnlyPhoneCharacters(string phone, bool valid) =>
        Assert.AreEqual(valid, Make("Ada", phone: phone).Validate(out _));

    [Timeout(60_000)]
    [TestMethod]
    public void MatchesLooksInEveryFieldForEveryWordInAnyCase()
    {
        var contact = Make("Ada Lovelace", "ada@analytical.org", "+44 20 7946 0958", "Analytical Engines", "met at the 1843 fair");

        Assert.IsTrue(contact.Matches(""));
        Assert.IsTrue(contact.Matches("LOVELACE"));
        Assert.IsTrue(contact.Matches("ada engines"));
        Assert.IsTrue(contact.Matches("7946 fair"));
        Assert.IsFalse(contact.Matches("ada babbage"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TrimmedStripsEveryField()
    {
        var trimmed = Make(" Ada ", " a@b.c ", " 1 ", " Co ", " n ").Trimmed();

        Assert.AreEqual(("Ada", "a@b.c", "1", "Co", "n"), (trimmed.Name, trimmed.Email, trimmed.Phone, trimmed.Company, trimmed.Notes));
    }
}

[TestClass]
public class ContactStoreTest
{
    private static (ContactStore Store, Dictionary<string, string> Settings) Create(Dictionary<string, string>? seed = null)
    {
        var settings = seed ?? [];
        var service = new Mock<ISettingsService>();
        service.Setup(s => s.Get(It.IsAny<string>())).Returns((string key) => settings.GetValueOrDefault(key));
        service.Setup(s => s.Set(It.IsAny<string>(), It.IsAny<string>())).Callback((string key, string value) => settings[key] = value);
        return (new ContactStore(service.Object), settings);
    }

    private static Contact Make(string name, string email = "") => new(Guid.NewGuid(), name, email, "", "", "");

    [Timeout(60_000)]
    [TestMethod]
    public void FindListsContactsByNameIgnoringCase()
    {
        var (store, _) = Create();
        store.Add(Make("charlie"));
        store.Add(Make("Alice"));
        store.Add(Make("bob"));

        CollectionAssert.AreEqual(new[] { "Alice", "bob", "charlie" }, store.Find().Select(c => c.Name).ToArray());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void FindNarrowsToTheFilter()
    {
        var (store, _) = Create();
        store.Add(Make("Alice", "alice@example.com"));
        store.Add(Make("Bob", "bob@work.org"));

        Assert.AreEqual("Bob", store.Find("work").Single().Name);
        Assert.AreEqual(2, store.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void UpdateReplacesAndRemoveDeletes()
    {
        var (store, _) = Create();
        var alice = Make("Alice");
        store.Add(alice);

        store.Update(alice with { Name = "Alicia" });
        Assert.AreEqual("Alicia", store.Find().Single().Name);

        store.Update(Make("Stranger"));
        Assert.AreEqual(1, store.Count);

        store.Remove(alice.Id);
        Assert.AreEqual(0, store.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ContactsSurviveANewStoreOverTheSameSettings()
    {
        var (store, settings) = Create();
        store.Add(Make("Alice", "alice@example.com"));

        var (reopened, _) = Create(settings);

        Assert.AreEqual("alice@example.com", reopened.Find().Single().Email);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ADamagedValueOrANullEntryDoesNotStopTheListOpening()
    {
        var (damaged, _) = Create(new() { [ContactStore.SettingsKey] = "{not json" });
        Assert.AreEqual(0, damaged.Count);

        var (withNull, _) = Create(new() { [ContactStore.SettingsKey] = "[null,{\"Id\":\"5b0bcbc5-8f7c-4f0e-9a1c-3f0a0b0c0d0e\",\"Name\":\"Ada\"}]" });
        Assert.AreEqual("Ada", withNull.Find().Single().Name);
        Assert.AreEqual(string.Empty, withNull.Find().Single().Email);
    }
}
