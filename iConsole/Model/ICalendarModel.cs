namespace iConsole.Model;

/// <summary>
/// The date state a <see cref="Controls.Calendar"/> shows and edits: which day is selected and which day is
/// today. A host that already keeps such state (the calendar dialog's model) implements this and hands it to
/// the control, so the two always agree and a change from either side — a key the host handles, a click on
/// the grid — reaches the other through <see cref="Changed"/>.
/// </summary>
public interface ICalendarModel
{
    DateOnly Today { get; }

    DateOnly SelectedDate { get; }

    /// <summary>Selects <paramref name="date"/>; raises <see cref="Changed"/> when the selection moved.</summary>
    void Select(DateOnly date);

    /// <summary>Raised after the selected day or today changed.</summary>
    event EventHandler? Changed;
}

/// <summary>The model a <see cref="Controls.Calendar"/> uses when its host does not supply one.</summary>
public sealed class CalendarState : ICalendarModel
{
    private DateOnly _today = DateOnly.FromDateTime(DateTime.Today);
    private DateOnly _selected = DateOnly.FromDateTime(DateTime.Today);

    public event EventHandler? Changed;

    public DateOnly Today
    {
        get => _today;
        set
        {
            if (_today != value)
            {
                _today = value;
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public DateOnly SelectedDate => _selected;

    public void Select(DateOnly date)
    {
        if (_selected != date)
        {
            _selected = date;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
