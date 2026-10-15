namespace iTerminal.Core.Controls.Contents;

public interface IContent
{
    void ShowFilePanels();
    void ShowFullTerminal();
    void ShowTerminalBar();
    void ShowAiBar();
    void FocusLeftPanel();
    void FocusRightPanel();
    void FocusAiBar();
    void FocusTerminalBar();
}
