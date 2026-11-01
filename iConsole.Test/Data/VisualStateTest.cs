using iConsole.Data;

namespace iConsole.Test.Data;

[TestClass]
public class VisualStateTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void VisualState_Normal_HasValue()
    {
        var state = VisualState.Normal;
        Assert.AreEqual(0, (int)state);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void VisualState_Focused_HasValue()
    {
        var state = VisualState.Focused;
        Assert.IsTrue((int)state > 0);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void VisualState_Selected_HasValue()
    {
        var state = VisualState.Selected;
        Assert.IsTrue((int)state > 0);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void VisualState_Disabled_HasValue()
    {
        var state = VisualState.Disabled;
        Assert.IsTrue((int)state > 0);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void VisualState_SelectedDisabled_HasValue()
    {
        var state = VisualState.SelectedDisabled;
        Assert.IsTrue((int)state > 0);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void VisualState_AllValuesAreUnique()
    {
        var states = new[]
        {
            VisualState.Normal,
            VisualState.Focused,
            VisualState.Selected,
            VisualState.Disabled,
            VisualState.SelectedDisabled
        };

        var distinctStates = states.Distinct().ToArray();
        Assert.AreEqual(states.Length, distinctStates.Length);
    }
}
