using iConsole.Buffers;
using iConsole.Controls;
using iConsole.Data;
using iConsole.Test.Core;
using iTerminal.Core.Commands;
using iTerminal.Core.Dialogs.Games;
using iTerminal.Core.Services.Games;
using static iTerminal.Core.Test.Helper.UiTestHelper;
using StringAssert = Microsoft.VisualStudio.TestTools.UnitTesting.StringAssert;

namespace iTerminal.Core.Test.Dialogs.Games;

[TestClass]
public class GamesDialogTest
{
    private static GamesDialog Create() => new(
        [new TicTacToeGame(new Random(1)), new ChessGame(new Random(1)), new GoGame(new Random(1)), new NineMensMorrisGame(new Random(1))],
        TestTheme());

    private static void DrawChildren(IControl parent, IBuffer buffer)
    {
        foreach (IControl control in parent.Controls.AsSnapshot().Where(c => c.IsVisible))
        {
            control.Draw(buffer);
            DrawChildren(control, buffer);
        }
    }

    private static string Drawn(GamesDialog dialog, int width = 120, int height = 40)
    {
        ScreenBuffer buffer = new(width, height);
        // Twice, so text left over from the first frame would show.
        for (int pass = 0; pass < 2; pass++)
        {
            dialog.Draw(buffer);
            DrawChildren(dialog, buffer);
        }

        return string.Join("\n", Enumerable.Range(0, height).Select(buffer.Line));
    }

    private static Task<bool> Press(GamesDialog dialog, ConsoleKey key, char c = '\0') => dialog.HandleKeyEventAsync(Key(key, c));

    private static async Task Settle(GamesDialog dialog)
    {
        dialog.ApplyPendingForTest();
        await dialog.ThinkingTask;
        dialog.ApplyPendingForTest();
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ItListsTheFourGamesAndDrawsTheFirstOne()
    {
        GamesDialog dialog = Create();
        await ShowAsync(dialog);

        string screen = Drawn(dialog);

        StringAssert.Contains(screen, "Tic-Tac-Toe");
        StringAssert.Contains(screen, "Chess");
        StringAssert.Contains(screen, "Go");
        StringAssert.Contains(screen, "Nine Men's Morris");
        StringAssert.Contains(screen, "Tic-Tac-Toe — against the computer");
        StringAssert.Contains(screen, "X to move.");
        StringAssert.Contains(screen, "Esc:close");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task PageKeysAndDigitsChooseTheGame()
    {
        GamesDialog dialog = Create();
        await ShowAsync(dialog);

        await Press(dialog, ConsoleKey.PageDown);
        Assert.AreEqual("Chess", dialog.Current.Name);
        StringAssert.Contains(Drawn(dialog), "White to move.");

        await Press(dialog, ConsoleKey.D4, '4');
        Assert.AreEqual("Nine Men's Morris", dialog.Current.Name);

        await Press(dialog, ConsoleKey.PageDown);
        Assert.AreEqual("Tic-Tac-Toe", dialog.Current.Name);
        await Press(dialog, ConsoleKey.PageUp);
        Assert.AreEqual("Nine Men's Morris", dialog.Current.Name);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ArrowsAndEnterPlayAndTheComputerAnswersOnTheNextIdlePass()
    {
        GamesDialog dialog = Create();
        await ShowAsync(dialog);
        TicTacToeGame game = (TicTacToeGame)dialog.Current;

        await Press(dialog, ConsoleKey.Enter);
        Assert.AreEqual('X', game.CellAt(4));
        Assert.IsFalse(game.XToMove);

        dialog.ApplyPendingForTest();
        Assert.IsTrue(dialog.IsThinking);
        StringAssert.Contains(Drawn(dialog), "The computer is thinking");
        await Settle(dialog);

        Assert.IsFalse(dialog.IsThinking);
        Assert.AreEqual(1, Enumerable.Range(0, 9).Count(i => game.CellAt(i) == 'O'));
        Assert.IsTrue(game.XToMove);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheLettersStartAgainTakeBackAndSwitchTheMode()
    {
        GamesDialog dialog = Create();
        await ShowAsync(dialog);
        TicTacToeGame game = (TicTacToeGame)dialog.Current;
        await Press(dialog, ConsoleKey.M, 'm');
        Assert.IsFalse(game.VsComputer);
        StringAssert.Contains(Drawn(dialog), "two players");

        await Press(dialog, ConsoleKey.Enter);
        Assert.IsTrue(game.CanUndo);
        await Press(dialog, ConsoleKey.U, 'u');
        Assert.IsFalse(game.CanUndo);

        await Press(dialog, ConsoleKey.Enter);
        await Press(dialog, ConsoleKey.N, 'n');
        Assert.AreEqual(' ', game.CellAt(4));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AGoPassKeyIsLeftToTheGameAndNewStaysWithTheDialog()
    {
        GamesDialog dialog = Create();
        await ShowAsync(dialog);
        await Press(dialog, ConsoleKey.D3, '3');
        GoGame go = (GoGame)dialog.Current;

        await Press(dialog, ConsoleKey.P, 'p');

        Assert.IsFalse(go.BlackToMove);
        Assert.AreEqual(1, go.Passes);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheChessPromotionKeysReachTheGameBeforeTheDialogsOwnLetters()
    {
        GamesDialog dialog = Create();
        await ShowAsync(dialog);
        await Press(dialog, ConsoleKey.D2, '2');
        ChessGame chess = (ChessGame)dialog.Current;
        chess.VsComputer = false;
        chess.Load(ChessTest_Fen("4k3/P7/8/8/8/8/8/4K3 w - - 0 1"));
        chess.CursorSquare = 6 * 8;
        await Press(dialog, ConsoleKey.Enter);
        chess.CursorSquare = 7 * 8;
        await Press(dialog, ConsoleKey.Enter);
        Assert.IsTrue(chess.AwaitsPromotion);

        // N means a knight here, not "new game".
        await Press(dialog, ConsoleKey.N, 'n');

        Assert.IsFalse(chess.AwaitsPromotion);
        Assert.AreEqual(2, chess.State.Board[7 * 8]);
    }

    private static ChessState ChessTest_Fen(string fen) => iTerminal.Core.Test.Services.Games.ChessTest.FromFen(fen);

    [Timeout(60_000)]
    [TestMethod]
    public async Task ClosingDropsTheSearchInFlight()
    {
        GamesDialog dialog = Create();
        await ShowAsync(dialog);
        await Press(dialog, ConsoleKey.Enter);
        dialog.ApplyPendingForTest();
        Assert.IsTrue(dialog.IsThinking);

        await Press(dialog, ConsoleKey.Escape);
        await dialog.ThinkingTask;
        dialog.ApplyPendingForTest();

        Assert.AreEqual(0, Enumerable.Range(0, 9).Count(i => ((TicTacToeGame)dialog.Current).CellAt(i) == 'O'));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task EveryGameDrawsOnASmallAndABigScreenWithoutAFault()
    {
        foreach ((int width, int height) in new[] { (30, 8), (120, 40), (64, 24), (80, 24), (10, 4), (1, 1) })
        {
            GamesDialog dialog = Create();
            await ShowAsync(dialog, new Size((uint)width, (uint)height));
            for (int game = 0; game < 4; game++)
            {
                Drawn(dialog, width, height);
                await Press(dialog, ConsoleKey.PageDown);
            }
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheKeyHintsNameTheKeysOnceAndTheFooterSaysHowToLeave()
    {
        GamesDialog dialog = Create();

        List<(string Key, string Description)> hints = [.. dialog.GetKeyHints()];

        Assert.AreEqual(hints.Count, hints.Select(h => h.Key).Distinct().Count());
        Assert.IsTrue(hints.All(h => h.Key.Length > 0 && h.Description.Length > 0));
        StringAssert.Contains(GamesDialogKeyHints(), "Esc");
    }

    private static string GamesDialogKeyHints() =>
        (string)typeof(GamesDialog).GetField("KeyHints", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetRawConstantValue()!;

    [Timeout(60_000)]
    [TestMethod]
    public void TheWrapKeepsWordsWholeAndBreaksLongOnes()
    {
        CollectionAssert.AreEqual(new[] { "one two", "three" }, GamesDialog.Wrap("one two three", 8).ToArray());
        CollectionAssert.AreEqual(new[] { "abcd", "efgh" }, GamesDialog.Wrap("abcdefgh", 4).ToArray());
        Assert.AreEqual(0, GamesDialog.Wrap("text", 0).Count());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheCommandIsCalledGamesAndHasNoHotKey()
    {
        GamesDialogCommand command = new(Create());

        Assert.AreEqual("Games", command.Title);
        Assert.IsNull(command.Key);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AComputerSearchCancelledLateDoesNotLoseTheMoveOfTheNewerOne()
    {
        // The first search ignores its cancellation until the second has answered, so its (stale) move is parked last.
        TaskCompletionSource gate = new();
        ScriptedGame game = new(gate.Task);
        var dialog = new GamesDialog([game], TestTheme());
        await ShowAsync(dialog);

        dialog.OnIdle();
        Task first = dialog.ThinkingTask;
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.N, 'n'));
        dialog.OnIdle();
        await dialog.ThinkingTask;
        gate.SetResult();
        await first;

        dialog.ApplyPendingForTest();

        Assert.AreEqual(1, game.MovesPlayed, "the newer search's move is played");
        Assert.IsFalse(dialog.IsThinking, "the computer is not left waiting for a move that never comes");
    }

    /// <summary>A game whose computer plans its first move behind a gate that ignores cancellation, and plans every later one at once.</summary>
    private sealed class ScriptedGame(Task firstPlanGate) : IGame
    {
        private bool _firstPlanned;

        public string Name => "Scripted";
        public string Rules => string.Empty;
        public bool VsComputer { get; set; } = true;
        public string Status => string.Empty;
        public bool IsOver => false;
        public bool CanUndo => false;
        public bool IsComputerTurn { get; private set; } = true;
        public int MovesPlayed { get; private set; }

        public IReadOnlyList<GameCell[]> Render() => [];
        public void MoveCursor(int dx, int dy) { }
        public void Activate() { }
        public void Undo() { }

        public void NewGame() => IsComputerTurn = true;

        public Func<CancellationToken, Action?>? PlanComputerMove()
        {
            bool gated = !_firstPlanned;
            _firstPlanned = true;
            return token =>
            {
                if (gated)
                {
                    firstPlanGate.GetAwaiter().GetResult();
                }

                token.ThrowIfCancellationRequested();
                return () =>
                {
                    MovesPlayed++;
                    IsComputerTurn = false;
                };
            };
        }
    }
}
