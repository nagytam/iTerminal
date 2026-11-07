using iTerminal.Core.Services.SystemSettings;

namespace iTerminal.Core.Test.Services.SystemSettings;

[TestClass]
public class SessionEnvironmentTest
{
    private readonly string _name = "ITERMINAL_TEST_" + Guid.NewGuid().ToString("N");

    [TestCleanup]
    public void RemoveVariable() => Environment.SetEnvironmentVariable(_name, null);

    [Timeout(60_000)]
    [TestMethod]
    public void ASetVariableIsInheritedByThisProcessAndListed()
    {
        var session = new SessionEnvironment();

        var result = session.Set(_name, "value one");

        Assert.IsTrue(result.Success, result.Message);
        Assert.AreEqual("value one", Environment.GetEnvironmentVariable(_name));
        Assert.AreEqual("value one", session.Read().Single(v => v.Name == _name).Value);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ASessionChangeNeverReachesTheSavedUserEnvironment()
    {
        new SessionEnvironment().Set(_name, "x");

        // The Target overloads would write HKCU\Environment on Windows; the session must stay in the process.
        Assert.IsNull(Environment.GetEnvironmentVariable(_name, EnvironmentVariableTarget.User));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AProcessMayHoldANameTheSavedStoresWouldRefuse()
    {
        var name = _name + ".dotted";
        try
        {
            Assert.IsTrue(new SessionEnvironment().Set(name, "x").Success);
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, null);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ARemovedVariableIsGone()
    {
        var session = new SessionEnvironment();
        session.Set(_name, "x");

        session.Remove(_name);

        Assert.IsNull(Environment.GetEnvironmentVariable(_name));
        Assert.IsFalse(session.Read().Any(v => v.Name == _name));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ABadNameOrValueIsRefusedAndNothingIsSet()
    {
        var session = new SessionEnvironment();

        Assert.IsFalse(session.Set("A=B", "x").Success);
        Assert.IsFalse(session.Set(_name, "two\nlines").Success);
        Assert.IsNull(Environment.GetEnvironmentVariable(_name));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheListIsSortedByName()
    {
        var names = new SessionEnvironment().Read().Select(v => v.Name).ToArray();

        CollectionAssert.AreEqual(names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray(), names);
    }
}
