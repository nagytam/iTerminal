using iConsole.Dialogs;

namespace iTerminal.Core.Extensions;

/// <summary>
/// A dialog that a feature outside iTerminal.Core lets the AI assistant open by name. <see cref="Names"/> are lower-case.
/// </summary>
public interface IOpenableDialogContribution
{
    IReadOnlyList<string> Names { get; }

    Dialog Resolve(IServiceProvider services);
}
