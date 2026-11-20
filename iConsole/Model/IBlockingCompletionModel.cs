namespace iConsole.Model;

/// <summary>
/// A completion model whose <see cref="ICompletionModel.UpdateMatchingValues"/> may do I/O (list a folder, ask a
/// service), so it must never run on the thread that builds or initialises the box. <see cref="Controls.AutoCompletion"/>
/// and <c>TextBoxExtensions.WithAutoCompletion</c> run the model's first update on a background task, the way the
/// update that follows every keystroke already is.
/// </summary>
public interface IBlockingCompletionModel : ICompletionModel
{
}
