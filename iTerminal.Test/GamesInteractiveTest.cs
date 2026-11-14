using iTerminal.Core.Dialogs.Games;

namespace iTerminal.Test;

/// <summary>The games run in the real app on an 80 x 24 terminal: their boards fit, the keys reach them and the computer's side is labelled.</summary>
[TestClass]
public class GamesInteractiveTest
{
    [Timeout(120_000)]
    [TestMethod]
    public async Task TetrisAgainstTheComputerFitsAnEightyByTwentyFourScreenAndTakesKeys()
    {
        await using TerminalApp app = await TerminalApp.StartAsync(width: 80, height: 24);
        await app.PressAsync("Shift+F1");
        await app.TypeAsync("Tetris");
        await app.PressAsync("Enter");
        await app.Driver.WaitForModalAsync<TetrisDialog>();

        await app.WaitForTextAsync("Tetris — against the computer");
        await app.WaitUntilAsync(s => s.Lines.Any(l => l.Contains("You") && l.Contains("Computer")), "the labels over the two boards");
        await app.WaitUntilAsync(s => s.Lines.Any(l => l.Contains("└") && l.Contains("┘")), "the floor of the well, so the bottom row is on screen");

        await app.PressAsync("Enter");
        await app.PressAsync("d");
        await app.WaitUntilAsync(s => s.Lines.Any(l => l.Contains("Score ") && !l.Contains("Score 0 ")), "the dropped piece to score");

        await app.PressAsync("Esc");
        await app.WaitForNoDialogAsync();
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task SnakeAgainstTheComputerShowsItsVersusScoreAndTakesKeys()
    {
        await using TerminalApp app = await TerminalApp.StartAsync(width: 80, height: 24);
        await app.PressAsync("Shift+F1");
        await app.TypeAsync("Snake");
        await app.PressAsync("Enter");
        await app.Driver.WaitForModalAsync<SnakeDialog>();

        await app.WaitForTextAsync("Snake — against the computer");
        await app.WaitForTextAsync("Press an arrow key to start.");
        await app.PressAsync("Right");
        await app.WaitForTextAsync("Computer 0.");
        await app.WaitForTextAsync("Enter:pause");

        await app.PressAsync("Esc");
        await app.WaitForNoDialogAsync();
    }
}
