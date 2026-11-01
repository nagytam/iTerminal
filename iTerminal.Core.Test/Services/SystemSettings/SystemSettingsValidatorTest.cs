using iTerminal.Core.Services.SystemSettings;

namespace iTerminal.Core.Test.Services.SystemSettings;

[TestClass]
public class SystemSettingsValidatorTest
{
    [Timeout(60_000)]
    [TestMethod]
    [DataRow("WORKSTATION-01", true)]
    [DataRow("a", true)]
    [DataRow("web1", false)]
    public void AComputerNameWithLettersDigitsAndHyphensIsAccepted(string name, bool windows)
    {
        Assert.IsNull(SystemSettingsValidator.ValidateComputerName(name, windows));
    }

    [Timeout(60_000)]
    [TestMethod]
    [DataRow("", true)]
    [DataRow("   ", false)]
    [DataRow("-start", true)]
    [DataRow("end-", true)]
    [DataRow("has space", true)]
    [DataRow("under_score", false)]
    [DataRow("dötzig", true)]
    [DataRow("1234", true)]
    [DataRow("ABCDEFGHIJKLMNOP", true)] // 16 characters: one over NetBIOS.
    public void AnInvalidComputerNameIsRefusedWithAReason(string name, bool windows)
    {
        Assert.IsNotNull(SystemSettingsValidator.ValidateComputerName(name, windows));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AllDigitsIsFineOnLinuxAndTooLongForNetBiosIsFineForDns()
    {
        Assert.IsNull(SystemSettingsValidator.ValidateComputerName("1234", windows: false));
        Assert.IsNull(SystemSettingsValidator.ValidateComputerName("ABCDEFGHIJKLMNOP", windows: false));
        Assert.IsNotNull(SystemSettingsValidator.ValidateComputerName(new string('a', 64), windows: false));
    }

    [Timeout(60_000)]
    [TestMethod]
    [DataRow("WORKGROUP", true)]
    [DataRow("Home Office", true)]
    [DataRow("", false)]
    [DataRow("a;b", false)]
    [DataRow("a\\b", false)]
    [DataRow("ABCDEFGHIJKLMNOP", false)]
    public void AWorkgroupFollowsTheNetBiosRules(string name, bool valid)
    {
        Assert.AreEqual(valid, SystemSettingsValidator.ValidateWorkgroup(name) is null);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ADescriptionIsOneShortLine()
    {
        Assert.IsNull(SystemSettingsValidator.ValidateDescription(string.Empty), "clearing it is allowed");
        Assert.IsNull(SystemSettingsValidator.ValidateDescription("Tamas' desktop"));
        Assert.IsNotNull(SystemSettingsValidator.ValidateDescription("two\nlines"));
        Assert.IsNotNull(SystemSettingsValidator.ValidateDescription(new string('x', 257)));
    }

    [Timeout(60_000)]
    [TestMethod]
    [DataRow("PATH", true)]
    [DataRow("My Var (x86)", true)]
    [DataRow("EDITOR", false)]
    [DataRow("_private", false)]
    public void AVariableNameIsAccepted(string name, bool windows)
    {
        Assert.IsNull(SystemSettingsValidator.ValidateVariableName(name, windows));
    }

    [Timeout(60_000)]
    [TestMethod]
    [DataRow("", true)]
    [DataRow("A=B", true)]
    [DataRow(" padded", true)]
    [DataRow("1ST", false)]
    [DataRow("has space", false)]
    [DataRow("dash-ed", false)]
    public void AnInvalidVariableNameIsRefused(string name, bool windows)
    {
        Assert.IsNotNull(SystemSettingsValidator.ValidateVariableName(name, windows));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AValueIsASingleLine()
    {
        Assert.IsNull(SystemSettingsValidator.ValidateVariableValue(string.Empty, windows: false), "an empty value is a value");
        Assert.IsNull(SystemSettingsValidator.ValidateVariableValue("C:\\Tools;%USERPROFILE%\\bin", windows: true));
        Assert.IsNotNull(SystemSettingsValidator.ValidateVariableValue("a\nb", windows: true));
        Assert.IsNotNull(SystemSettingsValidator.ValidateVariableValue(null, windows: true));
    }
}
