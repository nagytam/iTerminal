using iConsole.Buffers;
using iConsole.Commands;
using iConsole.Controls.Buttons;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;

namespace iConsole.Controls.Wizard;

/// <summary>
/// A multi-step flow like the Wizard in Terminal.Gui: a header ("Step 2 of 3: Title"), the current step page,
/// an error line and Back / Next buttons, where Next reads Finish on the last step. Moving forward runs the
/// step validation first and stays put, showing the message, when it fails.
/// </summary>
public class Wizard : Control
{
    private const long ChromeRows = 3; // header + error line + button row

    private readonly List<WizardStep> _steps = [];
    private readonly Button _backButton;
    private readonly Button _nextButton;
    private int _currentIndex;
    private string _error = string.Empty;

    public Wizard(IThemeService themeService) : base(themeService)
    {
        _backButton = new Button(string.Empty, new WizardBackCommand(this), themeService) { ViewSize = new Size(8, 1) };
        _nextButton = new Button(string.Empty, new WizardNextCommand(this), themeService) { ViewSize = new Size(10, 1) };
        AddControl(_backButton);
        AddControl(_nextButton);
    }

    /// <summary>Raised after the last step validated and Finish was chosen.</summary>
    public event EventHandler? Finished;

    /// <summary>Raised when the current step changes.</summary>
    public event EventHandler? StepChanged;

    public IReadOnlyList<WizardStep> Steps => _steps;
    public int CurrentIndex => _currentIndex;
    public WizardStep? CurrentStep => _currentIndex < _steps.Count ? _steps[_currentIndex] : null;
    public bool IsFirstStep => _currentIndex == 0;
    public bool IsLastStep => _currentIndex >= _steps.Count - 1;
    public bool IsFinished { get; private set; }

    /// <summary>The validation message of the last failed forward move; empty otherwise.</summary>
    public string Error => _error;

    public void AddStep(WizardStep step)
    {
        _steps.Add(step);
        step.Content.IsVisible = _steps.Count == 1;
        AddControl(step.Content);
        OnInitializeLayout();
        StateHasChanged();
    }

    /// <summary>Goes one step back. Never validates, so the user can always leave a half-filled page.</summary>
    public bool Back()
    {
        if (IsFirstStep)
        {
            return false;
        }

        MoveTo(_currentIndex - 1);
        return true;
    }

    /// <summary>
    /// Validates the current step and moves on, or finishes the wizard on the last step.
    /// Returns false, with <see cref="Error"/> set, when the validation refused.
    /// </summary>
    public async Task<bool> NextAsync()
    {
        WizardStep? step = CurrentStep;
        if (step is null || IsFinished)
        {
            return false;
        }

        string? error = step.ValidateAsync is null ? null : await step.ValidateAsync();
        if (!string.IsNullOrEmpty(error))
        {
            _error = error;
            StateHasChanged();
            return false;
        }

        if (IsLastStep)
        {
            _error = string.Empty;
            IsFinished = true;
            Finished?.Invoke(this, EventArgs.Empty);
            StateHasChanged();
            return true;
        }

        MoveTo(_currentIndex + 1);
        return true;
    }

    private void MoveTo(int index)
    {
        _currentIndex = index;
        _error = string.Empty;
        for (int i = 0; i < _steps.Count; i++)
        {
            _steps[i].Content.IsVisible = i == index;
        }

        StepChanged?.Invoke(this, EventArgs.Empty);
        StateHasChanged();
    }

    public override async Task ShowAsync()
    {
        await base.ShowAsync();

        // The base makes every child visible; only the current page may be.
        for (int i = 0; i < _steps.Count; i++)
        {
            _steps[i].Content.IsVisible = i == _currentIndex;
        }
    }

    public override void OnInitializeLayout()
    {
        long width = ViewSize.Width;
        long height = ViewSize.Height;
        long contentHeight = height > ChromeRows ? height - ChromeRows : 0;
        foreach (WizardStep step in _steps)
        {
            step.Content.Position = new Position(0, 1);
            step.Content.ViewSize = new Size(width, contentHeight);
        }

        long buttonRow = height > 0 ? height - 1 : 0;
        long nextX = width > _nextButton.ViewSize.Width ? width - _nextButton.ViewSize.Width : 0;
        _nextButton.Position = new Position(nextX, buttonRow);
        _backButton.Position = new Position(nextX > _backButton.ViewSize.Width + 1 ? nextX - _backButton.ViewSize.Width - 1 : 0, buttonRow);
        base.OnInitializeLayout();
    }

    public override void Draw(IBuffer buffer)
    {
        if (ViewSize.IsEmpty)
        {
            return;
        }

        ColorScheme colorScheme = ThemeService.CurrentColorScheme;
        int width = (int)ViewSize.Width;
        string header = CurrentStep is null
            ? string.Empty
            : $"Step {_currentIndex + 1} of {_steps.Count}: {CurrentStep.Title}";
        WriteLine(buffer, 0, header, colorScheme.Foreground, colorScheme.Background, width);

        long errorRow = ViewSize.Height > 1 ? ViewSize.Height - 2 : 0;
        WriteLine(buffer, errorRow, _error, Color.Red, colorScheme.Background, width);

        base.Draw(buffer);
    }

    private void WriteLine(IBuffer buffer, long row, string text, Color foreground, Color background, int width)
    {
        string line = (text.Length > width ? text[..width] : text).PadRight(width);
        buffer.Write(
            ZOrder,
            new Position(AbsolutePosition.X, AbsolutePosition.Y + row),
            new Size(width, 1),
            new ConxelString(line, foreground, background));
    }

    private sealed class WizardBackCommand(Wizard wizard) : Command
    {
        public override string Title => "Back";
        public override bool CanExecute() => !wizard.IsFirstStep && !wizard.IsFinished;

        public override Task ExecuteAsync(object? args)
        {
            wizard.Back();
            return Task.CompletedTask;
        }
    }

    private sealed class WizardNextCommand(Wizard wizard) : Command
    {
        public override string Title => wizard.IsLastStep ? "Finish" : "Next";
        public override bool CanExecute() => !wizard.IsFinished && wizard.Steps.Count > 0;

        public override async Task ExecuteAsync(object? args) => await wizard.NextAsync();
    }
}
