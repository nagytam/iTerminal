using iConsole.Services;
using System.ComponentModel;

namespace iConsole.Model.Validations;

/// <summary>
/// Validates a model in the background as it changes, newest change wins.
///
/// <para>"Newest" is decided by a stamp taken when the change happens, and the stamps only ever go
/// up. They used to be read from <see cref="IDateTimeProviderService.Now"/> on the background task,
/// a clock cached for half a second, while the dialogs validate directly with the real
/// <see cref="DateTime.Now"/> when they open. So for up to half a second after a dialog opened,
/// every keystroke's result carried an <i>older</i> stamp than the opening one and was thrown away;
/// a name typed quickly (or pasted) and confirmed with Enter left the field "invalid" for good — the
/// create button did nothing and nothing said why. Keystrokes sharing one cached instant also raced,
/// and whichever background validation finished last won, not the one for the newest text.</para>
///
/// <para><see cref="EnsureCurrent"/> is for whoever acts on the result (a command's
/// <c>CanExecute</c>): when a change is still being validated it validates the current text there
/// and then, so a confirm key pressed right after typing sees what was typed.</para>
/// </summary>
public class ValidationModel<T>
    : IValidationModel where T : INotifyPropertyChanged
{
    private readonly Lock _stampLock = new();
    private DateTime _lastIssuedStamp = DateTime.MinValue;
    private IDateTimeProviderService _dateTimeProviderService { get; set; }
    protected T? Model { get; init; }
    public ValidationResult ValidationResult { get; private set; }
    public bool IsValid => ValidationResult.IsValid;
    public string ErrorMessage => ValidationResult.ErrorMessage ?? string.Empty;

    public ValidationModel(IDateTimeProviderService dateTimeProviderService, T? model)
    {
        _dateTimeProviderService = dateTimeProviderService;
        Model = model;
        if (Model is not null)
        {
            Model.PropertyChanged += OnModelPropertyChanged;
        }
        Validate(DateTime.Now);
        ValidationResult ??= new ValidationResult(DateTime.MinValue, true, string.Empty);
    }

    /// <summary>True while a change has been made that no finished validation has seen yet.</summary>
    public bool IsPending
    {
        get
        {
            lock (_stampLock)
            {
                return ValidationResult.TimeStamp < _lastIssuedStamp;
            }
        }
    }

    /// <summary>
    /// Validates now if a change is still pending, and returns whether the model is valid. Cheap when
    /// nothing is pending, so it can sit in a <c>CanExecute</c> that a button reads on every draw.
    /// </summary>
    public bool EnsureCurrent()
    {
        if (IsPending)
        {
            Validate(NextStamp());
        }
        return IsValid;
    }

    /// <summary>A stamp later than every stamp issued so far and than the current result's.</summary>
    protected DateTime NextStamp()
    {
        lock (_stampLock)
        {
            var stamp = _dateTimeProviderService?.Now ?? DateTime.Now;
            var floor = ValidationResult?.TimeStamp ?? DateTime.MinValue;
            if (_lastIssuedStamp > floor)
            {
                floor = _lastIssuedStamp;
            }
            if (stamp <= floor)
            {
                stamp = floor.AddTicks(1);
            }
            _lastIssuedStamp = stamp;
            return stamp;
        }
    }

    private void OnModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Stamped here, when the change happens, not when the background task gets round to it.
        var stamp = NextStamp();
        _ = Task.Run(() => { Validate(stamp); });
    }

    public virtual void Validate(DateTime now)
    {
        SetValidationResult(now, true, string.Empty);
    }

    protected void SetValidationResult(DateTime now, bool isValid, string errorMessage)
    {
        lock (_stampLock)
        {
            if (ValidationResult is null || ValidationResult.TimeStamp.CompareTo(now) <= 0)
            {
                ValidationResult = new ValidationResult(now, isValid, errorMessage);
            }
        }
    }
}
