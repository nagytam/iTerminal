namespace iTerminal.Core.AI;

public interface IUiService
{
    void ShowFilePanels();
    void ShowFullTerminal();
    void ShowTerminalBar();
    void ShowAiBar();
    void FocusLeftPanel();
    void FocusRightPanel();
    Task OpenDialogAsync(string name);
    void Quit();
}
