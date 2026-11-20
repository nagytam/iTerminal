using iConsole.Commands;
using iConsole.Data;

namespace iConsole.Test.Commands;

[TestClass]
public class KeyBindingRulesTest
{
    [Timeout(60_000)]
    [TestMethod]
    [DataRow("F5")]
    [DataRow("Ctrl+Shift+F5")]
    [DataRow("Alt+F1")]
    [DataRow("Ctrl+Up")]
    [DataRow("Shift+Del")]
    [DataRow("Ctrl+Space")]
    [DataRow("Ctrl+K")]
    public void AllowsKeysThatDoNotTypeOrCollide(string text)
    {
        Assert.IsTrue(KeyBindingText.TryParse(text, out Key? key, out _));

        Assert.IsNull(KeyBindingRules.Refusal(key!), text);
    }

    [Timeout(60_000)]
    [TestMethod]
    [DataRow("A")]
    [DataRow("Shift+A")]
    [DataRow("5")]
    [DataRow("Space")]
    [DataRow("Num+")]
    [DataRow("=")]
    [DataRow("Alt+A")]
    [DataRow("Alt+5")]
    [DataRow("Alt+Shift+A")]
    [DataRow("Alt+/")]
    [DataRow("Ctrl+Alt+A")]
    [DataRow("Ctrl+Alt+Oem2")]
    [DataRow("Esc")]
    [DataRow("Tab")]
    [DataRow("Shift+Tab")]
    [DataRow("Ctrl+M")]
    [DataRow("Ctrl+Shift+M")]
    [DataRow("Ctrl+I")]
    [DataRow("Ctrl+H")]
    [DataRow("Ctrl+J")]
    [DataRow("Ctrl+C")]
    [DataRow("Ctrl+Z")]
    [DataRow("Ctrl+\\")]
    [DataRow("Ctrl+O")]
    [DataRow("Ctrl+[")]
    public void RefusesKeysThatTypeOrBelongToTheFocusOrTheTerminal(string text)
    {
        Assert.IsTrue(KeyBindingText.TryParse(text, out Key? key, out _));

        string? refusal = KeyBindingRules.Refusal(key!);

        Assert.IsNotNull(refusal, $"{text} should be refused");
        Assert.IsFalse(string.IsNullOrWhiteSpace(refusal));
    }
}
