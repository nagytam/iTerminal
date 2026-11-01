namespace iConsole.Prompts;

/// <summary>A yes-or-no question. Y answers yes, N answers no, Enter takes <see cref="DefaultValue"/> if there is one, Esc cancels.</summary>
public sealed class ConfirmationPrompt(string question)
{
    /// <summary>The question shown in the dialog.</summary>
    public string Question { get; } = question;

    /// <summary>The answer Enter gives. Null means Enter does nothing until Y or N is pressed.</summary>
    public bool? DefaultValue { get; set; }
}
