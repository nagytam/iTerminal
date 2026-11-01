using iConsole.Display;
using iConsole.Localization;

namespace iTerminal.Core.Services.Games;

/// <summary>
/// A simplified Risk on a 6 x 4 map of territories with four continents, for one person against the computer (Red) or a
/// second person at the same keyboard (Blue moves first). Each turn has three phases: reinforce, attack and fortify. There
/// are no cards and no undo. A side that loses every territory loses; a game that reaches the turn limit is a draw.
/// </summary>
public class RiskGame : GameBase
{
    internal const int Columns = 6;
    internal const int Rows = 4;
    internal const int TerritoryCount = Columns * Rows;
    internal const int ComputerSide = 1;
    internal const int TurnLimit = 300;

    private const int ArmiesPerSide = 32;
    private const int TerritoriesPerSide = TerritoryCount / 2;
    private const int MinReinforcements = 3;
    private const int MaxAttackDice = 3;
    private const int MaxDefendDice = 2;

    private static readonly int[] ContinentBonus = [2, 2, 3, 4];

    private int[] _owner = new int[TerritoryCount];
    private int[] _armies = new int[TerritoryCount];
    private int _side;
    private RiskPhase _phase;
    private int _reinforcements;
    private int _selected = -1;
    private int _cursor;
    private int _turnsPlayed;
    private int _winner = -1;
    private bool _drawn;
    private string _message = string.Empty;

    public RiskGame() : this(null)
    {
    }

    internal RiskGame(Random? random) : base(random)
    {
        NewGame();
    }

    /// <summary>The phases of a turn, in order.</summary>
    internal enum RiskPhase
    {
        Reinforce,
        Attack,
        Fortify,
    }

    public override string Name => "Risk";

    public override string Rules => "Take territories by attacking neighbours with dice. Hold a whole continent for bonus armies. Eliminate the other side to win.";

    public override bool IsOver => _winner >= 0 || _drawn;

    public override bool CanUndo => false;

    public override bool IsComputerTurn => VsComputer && !IsOver && _side == ComputerSide;

    public override bool OffersUndo => false;

    public override string ActivateLabel => UiText.Get("Games.Risk.Select", "select");

    public override string ActivateHint => UiText.Get("Games.Risk.Hint", "Select, attack, move or place");

    public override IEnumerable<(string Key, string Description)> ExtraKeys
    {
        get
        {
            yield return ("E", "End this phase");
        }
    }

    public override string Status
    {
        get
        {
            if (_drawn)
            {
                return UiText.Get("Games.Risk.Draw", "A draw: the turn limit was reached.");
            }

            if (_winner >= 0)
            {
                return string.Format(UiText.Get("Games.Risk.Wins", "{0} wins!"), SideName(_winner));
            }

            string prefix = _message.Length > 0 ? _message + " " : string.Empty;
            if (IsComputerTurn)
            {
                return prefix + string.Format(UiText.Get("Games.Risk.ToMove", "{0} to move."), SideName(_side));
            }

            return prefix + Hint();
        }
    }

    /// <summary>The owner of a territory: 0 for Blue, 1 for Red.</summary>
    internal int OwnerAt(int index) => _owner[index];

    internal int ArmiesAt(int index) => _armies[index];

    internal int Side => _side;

    internal RiskPhase Phase => _phase;

    internal int Reinforcements => _reinforcements;

    internal int Selected => _selected;

    internal int Cursor => _cursor;

    internal int TurnsPlayed => _turnsPlayed;

    internal static int Index(int column, int row) => row * Columns + column;

    internal static int ContinentOf(int index) => (index / Columns >= Rows / 2 ? 2 : 0) + (index % Columns >= Columns / 2 ? 1 : 0);

    internal static bool Adjacent(int a, int b)
    {
        int dx = Math.Abs(a % Columns - b % Columns);
        int dy = Math.Abs(a / Columns - b / Columns);
        return dx + dy == 1;
    }

    /// <summary>Moves the cursor to a territory; a test helper, the person moves it with the arrows.</summary>
    internal void MoveCursorTo(int index) => _cursor = index;

    /// <summary>Builds a position for a test: the owner and armies of every territory, and whose turn and phase it is.</summary>
    internal void LoadForTest(int[] owner, int[] armies, int side, RiskPhase phase, int reinforcements)
    {
        Version++;
        _owner = [.. owner];
        _armies = [.. armies];
        _side = side;
        _phase = phase;
        _reinforcements = reinforcements;
        _selected = -1;
        _turnsPlayed = 0;
        _winner = -1;
        _drawn = false;
        _message = string.Empty;
        _cursor = Math.Max(0, FirstOwned(side));
        CheckElimination();
    }

    public override void NewGame()
    {
        Version++;
        int[] order = Enumerable.Range(0, TerritoryCount).ToArray();
        Random.Shuffle(order);

        _owner = new int[TerritoryCount];
        _armies = new int[TerritoryCount];
        for (int k = 0; k < TerritoryCount; k++)
        {
            _owner[order[k]] = k < TerritoriesPerSide ? 0 : 1;
            _armies[order[k]] = 1;
        }

        for (int side = 0; side < 2; side++)
        {
            int[] own = Enumerable.Range(0, TerritoryCount).Where(i => _owner[i] == side).ToArray();
            for (int placed = TerritoriesPerSide; placed < ArmiesPerSide; placed++)
            {
                _armies[own[Random.Next(own.Length)]]++;
            }
        }

        _side = 0;
        _phase = RiskPhase.Reinforce;
        _reinforcements = ReinforcementsFor(0);
        _selected = -1;
        _turnsPlayed = 0;
        _winner = -1;
        _drawn = false;
        _message = string.Empty;
        _cursor = FirstOwned(0);
    }

    public override void MoveCursor(int dx, int dy)
    {
        if (IsComputerTurn || IsOver)
        {
            return;
        }

        int column = Math.Clamp(_cursor % Columns + dx, 0, Columns - 1);
        int row = Math.Clamp(_cursor / Columns + dy, 0, Rows - 1);
        _cursor = Index(column, row);
    }

    public override void Activate()
    {
        if (IsComputerTurn || IsOver)
        {
            return;
        }

        _message = string.Empty;
        switch (_phase)
        {
            case RiskPhase.Reinforce:
                Place(_cursor);
                break;
            case RiskPhase.Attack:
                AttackAtCursor();
                break;
            case RiskPhase.Fortify:
                FortifyAtCursor();
                break;
        }
    }

    public override bool TryChar(char c)
    {
        if (IsComputerTurn || IsOver || char.ToLowerInvariant(c) != 'e')
        {
            return false;
        }

        _message = string.Empty;
        EndPhase();
        return true;
    }

    public override void Undo()
    {
    }

    public override IReadOnlyList<GameCell[]> Render()
    {
        List<GameCell[]> rows = [];
        for (int row = 0; row < Rows; row++)
        {
            GameCell[] cells = new GameCell[Columns];
            for (int column = 0; column < Columns; column++)
            {
                int index = Index(column, row);
                bool source = index == _selected;
                Color background = source ? Color.Yellow : _owner[index] == 0 ? Color.Blue : Color.Red;
                Color foreground = source ? Color.Black : Color.White;
                string text = _armies[index].ToString().PadLeft(3) + "    ";
                cells[column] = new GameCell(text, foreground, background, index == _cursor);
            }

            rows.Add(cells);
        }

        return rows;
    }

    public override Func<CancellationToken, Action?>? PlanComputerMove()
    {
        if (!IsComputerTurn)
        {
            return null;
        }

        // The worker gets copies of the position and its own random source: the game's Random is not thread-safe.
        int[] owner = (int[])_owner.Clone();
        int[] armies = (int[])_armies.Clone();
        RiskPhase phase = _phase;
        int reinforcements = _reinforcements;
        int version = Version;
        Random random = new(Random.Next());

        return token =>
        {
            if (token.IsCancellationRequested)
            {
                return null;
            }

            Action? action = ChooseAction(owner, armies, phase, reinforcements, random);
            if (action is null)
            {
                return null;
            }

            return () =>
            {
                // A position that changed since the snapshot (undo, a new game) makes the move stale: it does nothing.
                if (Version == version)
                {
                    action();
                }
            };
        };
    }

    private Action? ChooseAction(int[] owner, int[] armies, RiskPhase phase, int reinforcements, Random random)
    {
        const int computer = ComputerSide;
        switch (phase)
        {
            case RiskPhase.Reinforce:
            {
                // The own territory with the most enemy armies next to it; then the one with the most armies of its own.
                var own = Enumerable.Range(0, TerritoryCount)
                    .Where(i => owner[i] == computer)
                    .Select(i => (Index: i, Threat: Neighbours(i).Where(n => owner[n] != computer).Sum(n => armies[n]), Armies: armies[i]))
                    .ToList();
                if (own.Count == 0)
                {
                    return null;
                }

                int maxThreat = own.Max(t => t.Threat);
                var threatened = own.Where(t => t.Threat == maxThreat).ToList();
                int maxArmies = threatened.Max(t => t.Armies);
                var ties = threatened.Where(t => t.Armies == maxArmies).ToList();
                int target = ties[random.Next(ties.Count)].Index;
                return () => Place(target);
            }

            case RiskPhase.Attack:
            {
                List<(int From, int To)> pairs = [];
                int bestGain = int.MinValue;
                for (int from = 0; from < TerritoryCount; from++)
                {
                    if (owner[from] != computer || armies[from] < 3)
                    {
                        continue;
                    }

                    foreach (int to in Neighbours(from).Where(n => owner[n] != computer))
                    {
                        int gain = armies[from] - armies[to];
                        if (gain > bestGain)
                        {
                            bestGain = gain;
                            pairs.Clear();
                        }

                        if (gain == bestGain)
                        {
                            pairs.Add((from, to));
                        }
                    }
                }

                if (pairs.Count == 0 || bestGain <= 0)
                {
                    return FinishAttack;
                }

                (int attackFrom, int attackTo) = pairs[random.Next(pairs.Count)];
                return () => Fight(attackFrom, attackTo);
            }

            default:
            {
                List<(int From, int To)> moves = [];
                for (int from = 0; from < TerritoryCount; from++)
                {
                    if (owner[from] != computer || armies[from] < 2 || HasEnemyNeighbour(owner, from))
                    {
                        continue;
                    }

                    foreach (int to in Neighbours(from).Where(n => owner[n] == computer && HasEnemyNeighbour(owner, n)))
                    {
                        moves.Add((from, to));
                    }
                }

                if (moves.Count == 0)
                {
                    return FinishTurn;
                }

                (int moveFrom, int moveTo) = moves[random.Next(moves.Count)];
                return () => MoveArmy(moveFrom, moveTo);
            }
        }
    }

    private static bool HasEnemyNeighbour(int[] owner, int index) =>
        Neighbours(index).Any(n => owner[n] != owner[index]);

    private static IEnumerable<int> Neighbours(int index)
    {
        int column = index % Columns;
        int row = index / Columns;
        if (column > 0)
        {
            yield return index - 1;
        }

        if (column < Columns - 1)
        {
            yield return index + 1;
        }

        if (row > 0)
        {
            yield return index - Columns;
        }

        if (row < Rows - 1)
        {
            yield return index + Columns;
        }
    }

    private int FirstOwned(int side)
    {
        for (int i = 0; i < TerritoryCount; i++)
        {
            if (_owner[i] == side)
            {
                return i;
            }
        }

        return -1;
    }

    private int ReinforcementsFor(int side)
    {
        int owned = _owner.Count(o => o == side);
        int total = Math.Max(MinReinforcements, owned / 3);
        for (int continent = 0; continent < ContinentBonus.Length; continent++)
        {
            if (Enumerable.Range(0, TerritoryCount).Where(i => ContinentOf(i) == continent).All(i => _owner[i] == side))
            {
                total += ContinentBonus[continent];
            }
        }

        return total;
    }

    private bool IsOwn(int index) => _owner[index] == _side;

    private void Place(int index)
    {
        if (!IsOwn(index))
        {
            _message = UiText.Get("Games.Risk.PlaceOwn", "Place armies on your own territory.");
            return;
        }

        Version++;
        _armies[index]++;
        _reinforcements--;
        _message = string.Empty;
        if (_reinforcements == 0)
        {
            _phase = RiskPhase.Attack;
            _selected = -1;
        }
    }

    private void AttackAtCursor()
    {
        if (IsOwn(_cursor))
        {
            if (_selected == _cursor)
            {
                _selected = -1;
            }
            else if (_armies[_cursor] >= 2)
            {
                _selected = _cursor;
            }
            else
            {
                _message = UiText.Get("Games.Risk.AttackFromTwo", "Attack from a territory with 2 or more armies.");
            }

            return;
        }

        if (_selected < 0)
        {
            _message = UiText.Get("Games.Risk.SelectFirst", "Select one of your territories to attack from.");
        }
        else if (!Adjacent(_selected, _cursor))
        {
            _message = UiText.Get("Games.Risk.NotAdjacent", "That territory is not next to the one you attack from.");
        }
        else
        {
            Fight(_selected, _cursor);
        }
    }

    private void FortifyAtCursor()
    {
        if (_selected >= 0 && _selected != _cursor && IsOwn(_cursor) && Adjacent(_selected, _cursor))
        {
            MoveArmy(_selected, _cursor);
            return;
        }

        if (IsOwn(_cursor))
        {
            if (_selected == _cursor)
            {
                _selected = -1;
            }
            else if (_armies[_cursor] >= 2)
            {
                _selected = _cursor;
            }
            else
            {
                _message = UiText.Get("Games.Risk.MoveFromTwo", "Move from a territory with 2 or more armies.");
            }

            return;
        }

        _message = UiText.Get("Games.Risk.MoveToOwn", "Move to one of your own territories.");
    }

    private void EndPhase()
    {
        if (_phase == RiskPhase.Reinforce)
        {
            _message = string.Format(UiText.Get("Games.Risk.PlaceFirst", "Place the {0} remaining armies first."), _reinforcements);
        }
        else if (_phase == RiskPhase.Attack)
        {
            FinishAttack();
        }
        else
        {
            FinishTurn();
        }
    }

    private void FinishAttack()
    {
        Version++;
        _phase = RiskPhase.Fortify;
        _selected = -1;
    }

    /// <summary>One battle: the dice, the losses on both sides, and a conquest if the defender has no armies left.</summary>
    private void Fight(int from, int to)
    {
        if (!IsOwn(from) || IsOwn(to) || _armies[from] < 2 || !Adjacent(from, to))
        {
            return;
        }

        int attackDice = Math.Min(MaxAttackDice, _armies[from] - 1);
        int defendDice = Math.Min(MaxDefendDice, _armies[to]);
        int[] attack = Roll(attackDice);
        int[] defend = Roll(defendDice);

        int attackerLost = 0;
        int defenderLost = 0;
        for (int i = 0; i < Math.Min(attack.Length, defend.Length); i++)
        {
            // A tie goes to the defender.
            if (attack[i] > defend[i])
            {
                defenderLost++;
            }
            else
            {
                attackerLost++;
            }
        }

        _armies[from] -= attackerLost;
        _armies[to] -= defenderLost;
        Version++;

        string who = SideName(_side);
        string other = SideName(1 - _side);
        string report = string.Format(
            UiText.Get("Games.Risk.Battle", "{0} rolled {1} and {2} {3}: {0} loses {4}, {2} loses {5}."),
            who, string.Join(", ", attack), other, string.Join(", ", defend), attackerLost, defenderLost);
        if (_armies[to] == 0)
        {
            // The attacker moves the dice count in. The source keeps at least one army.
            int moved = Math.Min(attackDice, _armies[from] - 1);
            _owner[to] = _side;
            _armies[to] = moved;
            _armies[from] -= moved;
            report += " " + string.Format(UiText.Get("Games.Risk.Conquers", "{0} takes the territory."), who);
        }

        _message = report;
        _selected = _armies[from] >= 2 ? from : -1;
        CheckElimination();
    }

    private void MoveArmy(int from, int to)
    {
        if (!IsOwn(from) || !IsOwn(to) || _armies[from] < 2 || !Adjacent(from, to))
        {
            return;
        }

        _armies[from]--;
        _armies[to]++;
        FinishTurn();
    }

    private void FinishTurn()
    {
        Version++;
        _selected = -1;
        _turnsPlayed++;
        if (_turnsPlayed >= TurnLimit)
        {
            _drawn = true;
            return;
        }

        _side = 1 - _side;
        _phase = RiskPhase.Reinforce;
        _reinforcements = ReinforcementsFor(_side);
        _cursor = Math.Max(0, FirstOwned(_side));
    }

    private void CheckElimination()
    {
        bool blue = _owner.Contains(0);
        bool red = _owner.Contains(1);
        if (!blue)
        {
            _winner = 1;
        }
        else if (!red)
        {
            _winner = 0;
        }
    }

    private string Hint()
    {
        string who = SideName(_side);
        return _phase switch
        {
            RiskPhase.Reinforce => string.Format(UiText.Get("Games.Risk.PlaceHint", "{0}: place {1} armies on your territories."), who, _reinforcements),
            RiskPhase.Attack => string.Format(UiText.Get("Games.Risk.AttackHint", "{0}: attack from a territory with 2 or more armies, or E to end the attack."), who),
            _ => string.Format(UiText.Get("Games.Risk.FortifyHint", "{0}: move one army to an adjacent territory of yours, or E to end the turn."), who),
        };
    }

    private int[] Roll(int dice)
    {
        int[] rolls = new int[dice];
        for (int i = 0; i < dice; i++)
        {
            rolls[i] = Random.Next(1, 7);
        }

        return rolls.OrderByDescending(r => r).ToArray();
    }

    private static string SideName(int side) => side == 0
        ? UiText.Get("Games.Risk.SideBlue", "Blue")
        : UiText.Get("Games.Risk.SideRed", "Red");
}
