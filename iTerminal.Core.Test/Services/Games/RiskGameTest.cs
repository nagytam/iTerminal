using iConsole.Display;
using iTerminal.Core.Services.Games;
using static iTerminal.Core.Services.Games.RiskGame;

namespace iTerminal.Core.Test.Services.Games;

[TestClass]
public class RiskGameTest
{
    private const int Territories = 24;

    /// <summary>Blue holds the top two rows (0-11), Red the bottom two (12-23).</summary>
    private static int[] SplitOwners() => Enumerable.Range(0, Territories).Select(i => i < 12 ? 0 : 1).ToArray();

    /// <summary>One army everywhere, then the given territories overridden.</summary>
    private static int[] Armies(params (int Index, int Count)[] overrides)
    {
        int[] armies = Enumerable.Repeat(1, Territories).ToArray();
        foreach ((int index, int count) in overrides)
        {
            armies[index] = count;
        }

        return armies;
    }

    private static RiskGame Load(int[] owner, int[] armies, int side, RiskPhase phase, int reinforcements, int seed = 1)
    {
        RiskGame game = new(new Random(seed));
        game.LoadForTest(owner, armies, side, phase, reinforcements);
        return game;
    }

    private static int[] OwnersOf(RiskGame game) => Enumerable.Range(0, Territories).Select(game.OwnerAt).ToArray();

    private static int[] ArmiesOf(RiskGame game) => Enumerable.Range(0, Territories).Select(game.ArmiesAt).ToArray();

    private static int TotalArmies(RiskGame game) => ArmiesOf(game).Sum();

    private static void Press(RiskGame game, int index)
    {
        game.MoveCursorTo(index);
        game.Activate();
    }

    /// <summary>The expected turn-start reinforcements, worked out from the four continents as the rules state them.</summary>
    private static int ExpectedReinforcements(RiskGame game, int side)
    {
        int owned = Enumerable.Range(0, Territories).Count(i => game.OwnerAt(i) == side);
        int total = Math.Max(3, owned / 3);
        (int Column, int Row, int Bonus)[] continents = [(0, 0, 2), (3, 0, 2), (0, 2, 3), (3, 2, 4)];
        foreach ((int column, int row, int bonus) in continents)
        {
            bool whole = true;
            for (int c = column; c < column + 3; c++)
            {
                for (int r = row; r < row + 2; r++)
                {
                    whole &= game.OwnerAt(Index(c, r)) == side;
                }
            }

            if (whole)
            {
                total += bonus;
            }
        }

        return total;
    }

    /// <summary>One step of play for whoever is to move: the computer's planned action, or the person's first obvious move.</summary>
    private static void PlayOneStep(RiskGame game)
    {
        if (game.IsComputerTurn)
        {
            Action? action = game.PlanComputerMove()!(CancellationToken.None);
            action?.Invoke();
            return;
        }

        switch (game.Phase)
        {
            case RiskPhase.Reinforce:
                Press(game, Enumerable.Range(0, Territories).First(i => game.OwnerAt(i) == game.Side));
                break;
            default:
                game.TryChar('e');
                break;
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheSetupGivesEachSideTwelveTerritoriesAndThirtyTwoArmies()
    {
        RiskGame game = new(new Random(7));

        Assert.AreEqual(12, Enumerable.Range(0, Territories).Count(i => game.OwnerAt(i) == 0));
        Assert.AreEqual(12, Enumerable.Range(0, Territories).Count(i => game.OwnerAt(i) == 1));
        Assert.IsTrue(Enumerable.Range(0, Territories).All(i => game.OwnerAt(i) is 0 or 1), "every territory is owned");
        Assert.IsTrue(Enumerable.Range(0, Territories).All(i => game.ArmiesAt(i) >= 1), "every territory holds an army");
        Assert.AreEqual(32, Enumerable.Range(0, Territories).Where(i => game.OwnerAt(i) == 0).Sum(game.ArmiesAt));
        Assert.AreEqual(32, Enumerable.Range(0, Territories).Where(i => game.OwnerAt(i) == 1).Sum(game.ArmiesAt));
        Assert.AreEqual(0, game.Side, "Blue moves first");
        Assert.AreEqual(RiskPhase.Reinforce, game.Phase);
        Assert.AreEqual(ExpectedReinforcements(game, 0), game.Reinforcements);
        Assert.IsFalse(game.IsOver);
        Assert.IsFalse(game.CanUndo);
        Assert.IsTrue(game.VsComputer, "the computer plays Red by default");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheSetupIsReproducibleForAFixedSeed()
    {
        RiskGame first = new(new Random(42));
        RiskGame second = new(new Random(42));

        CollectionAssert.AreEqual(OwnersOf(first), OwnersOf(second));
        CollectionAssert.AreEqual(ArmiesOf(first), ArmiesOf(second));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void EnterOnAnOwnTerritoryPlacesOneArmyAndTheLastOneStartsTheAttack()
    {
        RiskGame game = Load(SplitOwners(), Armies(), 0, RiskPhase.Reinforce, 2);

        Press(game, 0);
        Assert.AreEqual(2, game.ArmiesAt(0));
        Assert.AreEqual(1, game.Reinforcements);
        Assert.AreEqual(RiskPhase.Reinforce, game.Phase);

        Press(game, 1);
        Assert.AreEqual(0, game.Reinforcements);
        Assert.AreEqual(RiskPhase.Attack, game.Phase, "the phase becomes Attack when the reinforcements run out");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void EnterOnAnEnemyTerritoryPlacesNothing()
    {
        RiskGame game = Load(SplitOwners(), Armies(), 0, RiskPhase.Reinforce, 2);
        int before = TotalArmies(game);

        Press(game, 20);

        Assert.AreEqual(before, TotalArmies(game));
        Assert.AreEqual(2, game.Reinforcements);
        Assert.AreEqual(1, game.ArmiesAt(20));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void EndingTheReinforcePhaseDoesNothingUntilEveryArmyIsPlaced()
    {
        RiskGame game = Load(SplitOwners(), Armies(), 0, RiskPhase.Reinforce, 2);

        Assert.IsTrue(game.TryChar('E'), "E is consumed by the game");

        Assert.AreEqual(RiskPhase.Reinforce, game.Phase);
        Assert.AreEqual(2, game.Reinforcements);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AnAttackStartsOnlyFromAnOwnTerritoryWithTwoOrMoreArmies()
    {
        RiskGame game = Load(SplitOwners(), Armies((6, 5), (0, 1)), 0, RiskPhase.Attack, 0);

        Press(game, 0);
        Assert.AreEqual(-1, game.Selected, "one army cannot attack");

        Press(game, 6);
        Assert.AreEqual(6, game.Selected);

        Press(game, 6);
        Assert.AreEqual(-1, game.Selected, "Enter on the source deselects it");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AnAttackAgainstANonAdjacentTerritoryDoesNothing()
    {
        RiskGame game = Load(SplitOwners(), Armies((6, 5)), 0, RiskPhase.Attack, 0);
        int[] before = ArmiesOf(game);
        Press(game, 6);

        Press(game, 23);

        CollectionAssert.AreEqual(before, ArmiesOf(game));
        Assert.AreEqual(6, game.Selected, "the source stays selected");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AnAttackAgainstAnAdjacentEnemyFightsOnceAndConservesArmies()
    {
        RiskGame game = Load(SplitOwners(), Armies((6, 5), (12, 3)), 0, RiskPhase.Attack, 0);
        Press(game, 6);
        int before = TotalArmies(game);
        int attackerBefore = game.ArmiesAt(6);
        int defenderBefore = game.ArmiesAt(12);

        Press(game, 12);

        // Three attacking dice against two defending ones make two pairs, and each lost pair removes one army from its side.
        int lost = before - TotalArmies(game);
        Assert.AreEqual(2, lost, "two pairs are compared, so two armies are lost");
        int attackerLoss = attackerBefore - game.ArmiesAt(6);
        int defenderLoss = defenderBefore - game.ArmiesAt(12);
        Assert.AreEqual(lost, attackerLoss + defenderLoss);
        Assert.IsTrue(game.ArmiesAt(6) >= 1, "the source keeps at least one army");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AConquestChangesTheOwnerAndMovesTheAttackerDiceIntoTheTerritory()
    {
        // The attacker's three dice against the defender's one: when the defender loses its only army the attacker loses none,
        // so the territory is taken with three armies and the source keeps the rest.
        bool conquered = false;
        for (int seed = 1; seed <= 40 && !conquered; seed++)
        {
            RiskGame game = Load(SplitOwners(), Armies((6, 4), (12, 1)), 0, RiskPhase.Attack, 0, seed);
            Press(game, 6);

            Press(game, 12);

            if (game.OwnerAt(12) == 0)
            {
                conquered = true;
                Assert.AreEqual(3, game.ArmiesAt(12), "three dice move three armies in");
                Assert.AreEqual(1, game.ArmiesAt(6), "the source keeps one army");
            }
        }

        Assert.IsTrue(conquered, "the attacker wins at least one of forty fixed-seed battles");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheLastTerritoryOfASideEndsTheGame()
    {
        int[] owner = Enumerable.Repeat(0, Territories).ToArray();
        owner[1] = 1;
        bool won = false;
        for (int seed = 1; seed <= 40 && !won; seed++)
        {
            RiskGame game = Load(owner, Armies((0, 5)), 0, RiskPhase.Attack, 0, seed);
            Press(game, 0);

            Press(game, 1);

            if (game.OwnerAt(1) == 0)
            {
                won = true;
                Assert.IsTrue(game.IsOver);
                Assert.AreEqual("Blue wins!", game.Status);
            }
        }

        Assert.IsTrue(won, "Blue takes Red's only territory in at least one fixed-seed battle");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ASideWithNoTerritoriesAtTheStartIsBeaten()
    {
        RiskGame game = Load(Enumerable.Repeat(1, Territories).ToArray(), Armies(), 0, RiskPhase.Reinforce, 1);

        Assert.IsTrue(game.IsOver);
        Assert.AreEqual("Red wins!", game.Status);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void FortifyMovesOneArmyToAnAdjacentTerritoryAndTheTurnPasses()
    {
        RiskGame game = Load(SplitOwners(), Armies((0, 3), (1, 1)), 0, RiskPhase.Fortify, 0);
        Press(game, 0);
        Assert.AreEqual(0, game.Selected);

        Press(game, 1);

        Assert.AreEqual(2, game.ArmiesAt(0));
        Assert.AreEqual(2, game.ArmiesAt(1));
        Assert.AreEqual(1, game.Side, "the turn passes to Red");
        Assert.AreEqual(RiskPhase.Reinforce, game.Phase);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void EndingTheFortifyPhaseWithoutAMovePassesTheTurn()
    {
        RiskGame game = Load(SplitOwners(), Armies((0, 3)), 0, RiskPhase.Fortify, 0);

        Assert.IsTrue(game.TryChar('e'));

        Assert.AreEqual(3, game.ArmiesAt(0));
        Assert.AreEqual(1, game.Side);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AFortifyMoveTowardsAnEnemyDoesNothing()
    {
        RiskGame game = Load(SplitOwners(), Armies((6, 3)), 0, RiskPhase.Fortify, 0);
        Press(game, 6);

        Press(game, 12);

        Assert.AreEqual(3, game.ArmiesAt(6));
        Assert.AreEqual(0, game.Side, "the turn does not pass");
        Assert.AreEqual(6, game.Selected);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheCursorStaysOnTheBoard()
    {
        RiskGame game = Load(SplitOwners(), Armies(), 0, RiskPhase.Reinforce, 1);

        game.MoveCursor(-1, -1);
        Assert.AreEqual(0, game.Cursor);

        game.MoveCursor(9, 9);
        Assert.AreEqual(Index(Columns - 1, Rows - 1), game.Cursor);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheBoardDrawsFourRowsOfSixCellsFortyTwoColumnsWide()
    {
        RiskGame game = Load(SplitOwners(), Armies((0, 7)), 0, RiskPhase.Reinforce, 1);

        IReadOnlyList<GameCell[]> rows = game.Render();

        Assert.AreEqual(Rows, rows.Count);
        Assert.IsTrue(rows.All(r => r.Length == Columns));
        Assert.AreEqual(42, rows[0].Sum(c => c.Text.Length));
        Assert.AreEqual("  7    ", rows[0][0].Text);
        Assert.AreEqual(Color.Blue, rows[0][0].Background);
        Assert.AreEqual(Color.Red, rows[2][0].Background);
        Assert.IsTrue(rows[0][0].Cursor, "the cursor starts on the first own territory");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheSourceOfAnAttackIsDrawnInADifferentColour()
    {
        RiskGame game = Load(SplitOwners(), Armies((6, 5)), 0, RiskPhase.Attack, 0);
        Press(game, 6);

        GameCell source = game.Render()[1][0];

        Assert.AreEqual(Color.Yellow, source.Background);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheStatusNamesTheSideThePhaseAndTheArmiesLeft()
    {
        RiskGame game = Load(SplitOwners(), Armies(), 0, RiskPhase.Reinforce, 2);

        Assert.AreEqual("Blue: place 2 armies on your territories.", game.Status);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheHelpListsEndThisPhaseAndTheGameHasNoUndo()
    {
        RiskGame game = new(new Random(1));

        Assert.IsTrue(game.ExtraKeys.Any(k => k.Key == "E" && k.Description == "End this phase"));
        Assert.IsFalse(game.OffersUndo);
        Assert.AreEqual("Risk", game.Name);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void InputIsIgnoredOnTheComputersTurn()
    {
        RiskGame game = Load(SplitOwners(), Armies(), 1, RiskPhase.Reinforce, 3);
        int[] owners = OwnersOf(game);
        int[] armies = ArmiesOf(game);
        int cursor = game.Cursor;

        game.Activate();
        game.MoveCursor(1, 0);
        bool consumed = game.TryChar('e');

        Assert.IsFalse(consumed);
        CollectionAssert.AreEqual(owners, OwnersOf(game));
        CollectionAssert.AreEqual(armies, ArmiesOf(game));
        Assert.AreEqual(cursor, game.Cursor);
        Assert.AreEqual(3, game.Reinforcements);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void InputIsIgnoredOnceTheGameIsOver()
    {
        RiskGame game = Load(Enumerable.Repeat(0, Territories).ToArray(), Armies(), 0, RiskPhase.Attack, 0);
        int[] armies = ArmiesOf(game);
        int cursor = game.Cursor;

        game.Activate();
        game.MoveCursor(1, 0);
        bool consumed = game.TryChar('e');

        Assert.IsFalse(consumed);
        CollectionAssert.AreEqual(armies, ArmiesOf(game));
        Assert.AreEqual(cursor, game.Cursor);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void NoComputerMoveIsPlannedOnThePersonsTurnOrWithoutAnOpponent()
    {
        RiskGame person = Load(SplitOwners(), Armies(), 0, RiskPhase.Reinforce, 2);
        Assert.IsNull(person.PlanComputerMove());

        RiskGame twoPeople = Load(SplitOwners(), Armies(), 1, RiskPhase.Reinforce, 2);
        twoPeople.VsComputer = false;
        Assert.IsNull(twoPeople.PlanComputerMove());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AComputerActionThatIsStillCurrentPlacesAnArmy()
    {
        RiskGame game = Load(SplitOwners(), Armies(), 1, RiskPhase.Reinforce, 3);
        int before = TotalArmies(game);

        Action action = game.PlanComputerMove()!(CancellationToken.None)!;
        action();

        Assert.AreEqual(before + 1, TotalArmies(game));
        Assert.AreEqual(2, game.Reinforcements);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AStaleComputerActionDoesNothing()
    {
        RiskGame game = Load(SplitOwners(), Armies(), 1, RiskPhase.Reinforce, 3);
        Action action = game.PlanComputerMove()!(CancellationToken.None)!;

        // A new game changes the position, so the move planned for the old one must not apply.
        game.NewGame();
        int[] owners = OwnersOf(game);
        int[] armies = ArmiesOf(game);
        action();

        CollectionAssert.AreEqual(owners, OwnersOf(game));
        CollectionAssert.AreEqual(armies, ArmiesOf(game));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheComputerPlacesOnTheTerritoryWithTheMostEnemyArmiesNextToIt()
    {
        // Blue's army at 6 is ten strong; Red's territory at 12 borders it, so 12 is the threatened one.
        RiskGame game = Load(SplitOwners(), Armies((6, 10)), 1, RiskPhase.Reinforce, 3);

        game.PlanComputerMove()!(CancellationToken.None)!.Invoke();

        Assert.AreEqual(2, game.ArmiesAt(12));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheComputerEndsItsAttackWhenNoBattleIsFavourable()
    {
        RiskGame game = Load(SplitOwners(), Armies((12, 3), (6, 3)), 1, RiskPhase.Attack, 0);

        game.PlanComputerMove()!(CancellationToken.None)!.Invoke();

        Assert.AreEqual(RiskPhase.Fortify, game.Phase);
        Assert.AreEqual(3, game.ArmiesAt(12));
        Assert.AreEqual(3, game.ArmiesAt(6));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheComputersTurnEndsWithinBoundedSteps()
    {
        RiskGame game = Load(SplitOwners(), Armies((6, 10)), 1, RiskPhase.Reinforce, 3, seed: 5);

        int steps = 0;
        while (game.IsComputerTurn && steps < 500)
        {
            Action? action = game.PlanComputerMove()!(CancellationToken.None);
            action?.Invoke();
            steps++;
        }

        Assert.IsFalse(game.IsComputerTurn, $"the computer's turn ended after {steps} steps");
        Assert.AreEqual(0, game.Side, "the turn passes back to Blue");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void APassingTurnRefillsTheReinforcementsFromTheBoard()
    {
        RiskGame game = Load(SplitOwners(), Armies(), 0, RiskPhase.Fortify, 0);

        game.TryChar('e');

        Assert.AreEqual(ExpectedReinforcements(game, 1), game.Reinforcements);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AHotSeatGameWithNoEliminationEndsInADrawAtTheTurnLimit()
    {
        RiskGame game = new(new Random(3));
        game.VsComputer = false;

        int steps = 0;
        while (!game.IsOver && steps < 100_000)
        {
            PlayOneStep(game);
            steps++;
        }

        Assert.IsTrue(game.IsOver, "the game ends");
        Assert.AreEqual(TurnLimit, game.TurnsPlayed);
        Assert.AreEqual("A draw: the turn limit was reached.", game.Status);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ASelfPlayGameAgainstTheComputerAlwaysTerminates()
    {
        RiskGame game = new(new Random(11));

        int steps = 0;
        while (!game.IsOver && steps < 100_000)
        {
            PlayOneStep(game);
            steps++;
        }

        Assert.IsTrue(game.IsOver, $"the game ended within {steps} actions");
        Assert.IsTrue(game.Status is "Blue wins!" or "Red wins!" or "A draw: the turn limit was reached.", game.Status);
    }
}
