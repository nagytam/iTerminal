namespace iTerminal.DevelopmentStudio.Model;

/// <summary>Where a language server session is.</summary>
public enum LanguageServerState
{
    /// <summary>No session: nothing was opened, or the session ended.</summary>
    Stopped,

    /// <summary>The server is not installed (or could not be started).</summary>
    Missing,

    /// <summary>The process runs and the client is initializing.</summary>
    Starting,

    /// <summary>The server answered its initialization and open files are being checked.</summary>
    Ready,
}
