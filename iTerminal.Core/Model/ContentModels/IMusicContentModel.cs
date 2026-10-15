using iConsole.Interfaces;

namespace iTerminal.Core.Model.ContentModels;

/// <summary>
/// What the music panel commands need from a content model. Keeping it to this pair is what lets the
/// commands live on the ordinary content browser alongside the file-system ones: they test for this
/// interface, and stay inert on every other drive.
/// </summary>
public interface IMusicContentModel : IContentModel
{
    /// <summary>Feedback from the last action, rendered into the panel footer.</summary>
    string StatusMessage { get; }

    /// <summary>
    /// Runs whatever the row means — play this track, select this output, cycle the repeat mode.
    /// Folder rows are navigated by the command instead and never reach this method.
    /// </summary>
    void Activate(IEntry entry);

    /// <summary>Runs a transport or volume action that has no row behind it, i.e. a direct key.</summary>
    void Perform(MusicAction action);
}
