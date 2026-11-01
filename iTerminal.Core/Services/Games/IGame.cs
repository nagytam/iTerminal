using iConsole.Display;
using iConsole.Localization;

namespace iTerminal.Core.Services.Games;

/// <summary>One piece of a drawn board: some text and, optionally, the colours it is drawn in (null takes the theme's).</summary>
public readonly record struct GameCell(string Text, Color? Foreground = null, Color? Background = null, bool Cursor = false)
{
    public GameCell WithCursor() => this with { Cursor = true };
}

/// <summary>
/// A board game the games dialog can host. The game owns its rules, its cursor and its drawing as rows of
/// <see cref="GameCell"/>; the dialog only forwards keys and paints what <see cref="Render"/> returns.
/// </summary>
public interface IGame
{
    string Name { get; }

    /// <summary>A line or two on how the game is played here.</summary>
    string Rules { get; }

    /// <summary>When on, the computer plays the second side; the person at the keyboard plays the first.</summary>
    bool VsComputer { get; set; }

    /// <summary>What is going on: whose turn it is, a check, the result.</summary>
    string Status { get; }

    bool IsOver { get; }

    bool CanUndo { get; }

    /// <summary>True while it is the computer's turn and the game is still on.</summary>
    bool IsComputerTurn { get; }

    /// <summary>Keys the game itself reacts to, besides the ones every game shares.</summary>
    IEnumerable<(string Key, string Description)> ExtraKeys => [];

    IReadOnlyList<GameCell[]> Render();

    void MoveCursor(int dx, int dy);

    /// <summary>Enter or Space: play, select or remove whatever is under the cursor.</summary>
    void Activate();

    /// <summary>A typed character the game wants for itself (a pass, a promotion piece); false if it is not interested.</summary>
    bool TryChar(char c) => false;

    void Undo();

    void NewGame();

    /// <summary>
    /// Null unless it is the computer's turn. Otherwise a snapshot of the position, worked on a background task: the
    /// function chooses a move and returns what applies it, to be run on the UI thread (or null for no move). The
    /// action does nothing if the game moved on since the snapshot.
    /// </summary>
    Func<CancellationToken, Action?>? PlanComputerMove();

    /// <summary>
    /// Advances a game that moves on its own clock (Snake) by the time that has passed since the last call. Returns true
    /// when the position changed. The turn-based games never move on their own.
    /// </summary>
    bool Tick(TimeSpan elapsed) => false;

    /// <summary>Stops a game that moves on its own clock. The dialog calls it when the game is left and when the dialog closes.</summary>
    void Pause() { }

    /// <summary>False for a game with no computer opponent: the mode key does not apply and the mode label reads "one player".</summary>
    bool OffersComputerOpponent => true;

    /// <summary>False for a game that cannot take a move back: the undo key is not offered.</summary>
    bool OffersUndo => true;

    /// <summary>The short footer word for Enter.</summary>
    string ActivateLabel => "play";

    /// <summary>The help text for Enter.</summary>
    string ActivateHint => UiText.Get("DialogHint.PlaySelectTake", "Play, select or take a piece at the cursor");
}

/// <summary>What the games share: the mode flag and a random source the tests can fix.</summary>
public abstract class GameBase(Random? random) : IGame
{
    protected Random Random { get; } = random ?? new Random();

    /// <summary>Counts every change to the position, so a computer move chosen for an old one is dropped.</summary>
    protected int Version { get; set; }

    public abstract string Name { get; }
    public abstract string Rules { get; }
    public bool VsComputer { get; set; } = true;
    public abstract string Status { get; }
    public abstract bool IsOver { get; }
    public abstract bool CanUndo { get; }
    public abstract bool IsComputerTurn { get; }
    public virtual IEnumerable<(string Key, string Description)> ExtraKeys => [];
    public abstract IReadOnlyList<GameCell[]> Render();
    public abstract void MoveCursor(int dx, int dy);
    public abstract void Activate();
    public virtual bool TryChar(char c) => false;
    public abstract void Undo();
    public abstract void NewGame();
    public abstract Func<CancellationToken, Action?>? PlanComputerMove();

    // Virtual so a game can override them; the defaults are the interface's.
    public virtual bool Tick(TimeSpan elapsed) => false;
    public virtual void Pause()
    {
    }

    public virtual bool OffersComputerOpponent => true;
    public virtual bool OffersUndo => true;
    public virtual string ActivateLabel => "play";
    public virtual string ActivateHint => UiText.Get("DialogHint.PlaySelectTake", "Play, select or take a piece at the cursor");

    /// <summary>The cursor stays on the board.</summary>
    protected static int Clamp(int value, int max) => Math.Clamp(value, 0, max);
}
