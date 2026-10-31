namespace iTerminal.Test;

[TestClass]
public class TmpEncTest
{
    [TestMethod]
    public async Task Dump()
    {
        await using TerminalApp app = await TerminalApp.StartAsync();
        await app.PressAsync("Ctrl+Shift+W");
        await Task.Delay(500);
        string a = app.Screen.Dump();
        await app.TypeAsync("tulip");
        await app.PressAsync("Enter");
        await app.WaitForTextAsync("The sandbox introduction");
        await Task.Delay(300);
        File.WriteAllText("C:/Users/tamas/AppData/Local/Temp/claude/C--Users-tamas-workspace-iTerminal/a62906cc-6283-424a-ba45-c8299b54a9c6/scratchpad/enc_dump.txt", a + "\n\n" + app.Screen.Dump());
    }
}
