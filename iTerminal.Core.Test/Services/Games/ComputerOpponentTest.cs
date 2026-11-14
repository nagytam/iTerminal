using iTerminal.Core.Services.Games;

namespace iTerminal.Core.Test.Services.Games;

/// <summary>
/// Full games between a random-legal-move person and the computer. The computer's planned move must always exist while the
/// game is going and must be applied without error, and the games must end. Each game is played from several seeds.
/// </summary>
[TestClass]
public class ComputerOpponentTest
{
    private const int MaxPlies = 800;

    /// <summary>Chess and Morris search for up to two seconds a move, so their games are cut after a few computer moves.</summary>
    private const int SlowGameComputerMoves = 6;

    [TestMethod]
    public void TheComputerAlwaysHasAMoveInTicTacToe()
    {
        for (int seed = 0; seed < 50; seed++)
        {
            Random random = new(seed);
            TicTacToeGame game = new(new Random(seed)) { VsComputer = true };
            PlayOut(game, () =>
            {
                List<int> empty = [.. Enumerable.Range(0, 9).Where(i => game.CellAt(i) == ' ')];
                game.Place(empty[random.Next(empty.Count)]);
            }, seed);
        }
    }

    [TestMethod]
    public void TheComputerAlwaysHasAMoveInChess()
    {
        for (int seed = 0; seed < 2; seed++)
        {
            Random random = new(seed);
            ChessGame game = new(new Random(seed)) { VsComputer = true };
            PlayOut(game, () =>
            {
                IReadOnlyList<ChessMove> moves = game.LegalMoves;
                game.Play(moves[random.Next(moves.Count)]);
            }, seed, SlowGameComputerMoves);
        }
    }

    [TestMethod]
    public void TheComputerAlwaysHasAMoveInGo()
    {
        for (int seed = 0; seed < 10; seed++)
        {
            Random random = new(seed);
            GoGame game = new(new Random(seed)) { VsComputer = true };
            game.NewGame(9);
            PlayOut(game, () =>
            {
                if (random.Next(15) == 0)
                {
                    game.Pass();
                    return;
                }

                game.TryPlay(random.Next(81));
            }, seed);

            (int black, int white) = GoRules.AreaScore(game.Board, 9, game.Dead);
            Assert.IsTrue(black + white <= 81, "the area counts each point once");
        }
    }

    [TestMethod]
    public void TheComputerAlwaysHasAMoveInMorris()
    {
        for (int seed = 0; seed < 2; seed++)
        {
            Random random = new(seed);
            NineMensMorrisGame game = new(new Random(seed)) { VsComputer = true };
            PlayOut(game, () =>
            {
                List<MorrisMove> moves = MorrisState.Moves(game.State);
                MorrisMove move = moves[random.Next(moves.Count)];
                Assert.IsTrue(game.PlayMove(new MorrisMove(move.From, move.To, -1)));
                if (game.MustRemove)
                {
                    Assert.IsTrue(game.TryRemove(move.Remove));
                }
            }, seed, SlowGameComputerMoves);
        }
    }

    [TestMethod]
    public void TheComputerAlwaysHasACardInSwissJass()
    {
        for (int seed = 0; seed < 10; seed++)
        {
            Random random = new(seed);
            JassGame game = new(new Random(seed)) { VsComputer = true, ComputerPause = TimeSpan.Zero };
            PlayOut(game, () =>
            {
                if (game.TricksPlayed == 9)
                {
                    game.Activate();
                    return;
                }

                if (game.Trump is null)
                {
                    game.TryChar("CDHS"[random.Next(4)]);
                    return;
                }

                int index = JassComputer.ChooseCard(game.Hands[0], game.Trick, game.Trump.Value, 0);
                Assert.IsTrue(index >= 0 && game.TryChar((char)('1' + index)));
            }, seed);
        }
    }

    /// <summary>Plays the game until it is over: the person's side with <paramref name="personMove"/>, the computer's by its planned move.</summary>
    private static void PlayOut(IGame game, Action personMove, int seed, int maxComputerMoves = int.MaxValue)
    {
        int computerMoves = 0;
        for (int ply = 0; ply < MaxPlies && !game.IsOver && computerMoves < maxComputerMoves; ply++)
        {
            if (game.IsComputerTurn)
            {
                Func<CancellationToken, Action?>? plan = game.PlanComputerMove();
                Assert.IsNotNull(plan, $"seed {seed}: the computer has a turn to plan");
                Action? move = plan!(CancellationToken.None);
                Assert.IsNotNull(move, $"seed {seed}: the computer has a move in a game that is not over");
                move!();
                computerMoves++;
            }
            else
            {
                personMove();
            }
        }

        Assert.IsTrue(computerMoves > 0, $"seed {seed}: the computer played");
    }
}
