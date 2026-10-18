namespace iConsole.Controls.Wizard;

/// <summary>
/// One page of a <see cref="Wizard"/>: a title, the control shown while the step is current and an optional
/// validation that must pass before the wizard moves forward.
/// </summary>
/// <param name="Title">Shown in the wizard header.</param>
/// <param name="Content">The page body; the wizard shows it only while this step is current.</param>
/// <param name="ValidateAsync">Returns an error message to stay on the step, or null to continue.</param>
public record WizardStep(string Title, Control Content, Func<Task<string?>>? ValidateAsync = null);
