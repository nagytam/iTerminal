using iConsole.Display;
using iConsole.Localization;

namespace iTerminal.Core.Services.Games;

internal enum JassSuit
{
    Clubs,
    Diamonds,
    Hearts,
    Spades,
}

internal enum JassRank
{
    Six,
    Seven,
    Eight,
    Nine,
    Ten,
    Jack,
    Queen,
    King,
    Ace,
}

internal readonly record struct JassCard(JassSuit Suit, JassRank Rank)
{
    public override string ToString()
    {
        string suit = GameText.Get($"Games.Jass.Suit.{Suit}", Suit.ToString());
        string rank = Rank switch
        {
            JassRank.Six => "6",
            JassRank.Seven => "7",
            JassRank.Eight => "8",
            JassRank.Nine => "9",
            JassRank.Ten => "10",
            JassRank.Jack => "J",
            JassRank.Queen => "Q",
            JassRank.King => "K",
            _ => "A",
        };
        return string.Format(GameText.Get("Games.Jass.CardFormat", "{0} of {1}"), rank, suit);
    }

    public string ShortName
    {
        get
        {
            string rank = Rank switch
            {
                JassRank.Six => "6",
                JassRank.Seven => "7",
                JassRank.Eight => "8",
                JassRank.Nine => "9",
                JassRank.Ten => "10",
                JassRank.Jack => "J",
                JassRank.Queen => "Q",
                JassRank.King => "K",
                _ => "A",
            };
            string suit = Suit switch
            {
                JassSuit.Clubs => "C",
                JassSuit.Diamonds => "D",
                JassSuit.Hearts => "H",
                _ => "S",
            };
            return rank + suit;
        }
    }
}

internal readonly record struct JassPlay(int Player, JassCard Card);

/// <summary>
/// Four-player, partnership Schieber Jass. Against the computer the person is Player 1 and the other three seats are the
/// computer's (Player 3 is the partner); otherwise four people share the keyboard and pass the device. Player 1 and Player 3
/// are one partnership, Player 2 and Player 4 the other.
/// </summary>
public class JassGame : GameBase
{
    private const int TargetScore = 1000;
    private static readonly JassCard[] Deck =
    [
        .. from suit in Enum.GetValues<JassSuit>()
           from rank in Enum.GetValues<JassRank>()
           select new JassCard(suit, rank),
    ];

    private readonly List<JassCard>[] _hands = [[], [], [], []];
    private readonly int[] _scores = new int[2];
    private readonly int[] _handScores = new int[2];
    private readonly List<JassPlay> _trick = [];
    private readonly List<JassPlay> _lastTrick = [];
    private JassSuit? _trump;
    private int _dealer = 3;
    private int _forehand;
    private int _currentPlayer;
    private int _cursor;
    private int _tricksPlayed;
    private int _trickPoints;
    private bool _hasDealt;
    private bool _awaitingReveal = true;
    private bool _partnerChooses;
    private bool _handComplete;
    private string _message = string.Empty;

    /// <summary>How long the computer's seats take before they play; a pause lets the person follow the trick.</summary>
    internal TimeSpan ComputerPause { get; set; } = TimeSpan.FromMilliseconds(600);

    public JassGame() : this(null)
    {
    }

    internal JassGame(Random? random) : base(random)
    {
        NewGame();
    }

    public override string Name => "Swiss Jass";

    public override string Rules => GameText.Get(
        "Games.Jass.Rules",
        "Four players in two partnerships. Follow suit; if void, trump, and overtrump whenever possible. A hand is 157 points.");

    public override bool OffersUndo => false;

    public override string? ModeLabel => VsComputer
        ? GameText.Get("Games.Jass.VsComputer", "you and the computer players")
        : GameText.Get("Games.Jass.Mode", "four players (hot-seat)");

    /// <summary>A computer seat plays on its own as soon as it is its turn to play, choose trump or push.</summary>
    public override bool IsComputerTurn => VsComputer && !IsOver && !_handComplete && IsComputerSeat(_currentPlayer);

    public override bool CanUndo => false;

    /// <summary>Against the computer only Player 1 is the person; the rest of the table is the computer's.</summary>
    private bool IsComputerSeat(int seat) => VsComputer && seat != 0;

    /// <summary>The device is passed around only at a table of people; against the computer every hand is shown at once.</summary>
    private bool NeedsReveal => _awaitingReveal && !VsComputer;

    /// <summary>The seat whose hand is shown: the person's own, against the computer, or the player on turn at the shared keyboard.</summary>
    private int ShownSeat => VsComputer ? 0 : _currentPlayer;

    private bool IsHumanTurn => !IsComputerSeat(_currentPlayer);

    public override bool IsOver => _scores.Max() >= TargetScore;

    public override string Status
    {
        get
        {
            if (_message.Length > 0)
            {
                return _message;
            }

            if (IsOver)
            {
                return string.Format(
                    GameText.Get("Games.Jass.Winner", "Partnership {0} wins with {1} points. N starts a new match."),
                    WinningTeam + 1,
                    _scores[WinningTeam]);
            }

            if (_handComplete)
            {
                return string.Format(
                    GameText.Get("Games.Jass.HandComplete", "Hand complete. {0} – {1}. Enter deals the next hand."),
                    _scores[0],
                    _scores[1]);
            }

            if (NeedsReveal)
            {
                return string.Format(
                    GameText.Get("Games.Jass.Reveal", "Pass the device to Player {0} and press Enter to reveal."),
                    _currentPlayer + 1);
            }

            if (_trump is null)
            {
                return string.Format(
                    GameText.Get(
                        _partnerChooses ? "Games.Jass.ChooseTrump" : "Games.Jass.ChooseTrumpOrPush",
                        _partnerChooses
                            ? "Player {0}: choose trump: C clubs, D diamonds, H hearts or S spades."
                            : "Player {0}: choose trump (C/D/H/S) or push to your partner (P)."),
                    _currentPlayer + 1);
            }

            return string.Format(
                GameText.Get("Games.Jass.ToPlay", "Player {0} to play. Trump: {1}. Partnership: {2} – {3}."),
                _currentPlayer + 1,
                SuitName(_trump!.Value),
                _scores[0],
                _scores[1]);
        }
    }

    private int WinningTeam => _scores[0] >= TargetScore && _scores[1] >= TargetScore
        ? _scores[0] >= _scores[1] ? 0 : 1
        : _scores[0] >= TargetScore ? 0 : 1;

    internal IReadOnlyList<JassCard>[] Hands => _hands;
    internal IReadOnlyList<JassPlay> Trick => _trick;
    internal IReadOnlyList<JassPlay> LastTrick => _lastTrick;
    internal int CurrentPlayer => _currentPlayer;
    internal int Forehand => _forehand;
    internal int Dealer => _dealer;
    internal int TeamOneScore => _scores[0];
    internal int TeamTwoScore => _scores[1];
    internal JassSuit? Trump => _trump;
    internal int TricksPlayed => _tricksPlayed;

    public override void NewGame()
    {
        Version++;
        Array.Clear(_scores);
        _dealer = 3;
        _hasDealt = false;
        DealHand();
    }

    private void DealHand()
    {
        if (_hasDealt)
        {
            _dealer = (_dealer + 1) % 4;
        }

        _hasDealt = true;
        _forehand = (_dealer + 1) % 4;
        _currentPlayer = _forehand;
        _trump = null;
        _partnerChooses = false;
        _tricksPlayed = 0;
        _trickPoints = 0;
        Array.Clear(_handScores);
        _trick.Clear();
        _lastTrick.Clear();
        _handComplete = false;
        _awaitingReveal = true;
        _cursor = 0;
        _message = string.Empty;

        List<JassCard> shuffled = [.. Deck];
        for (int i = shuffled.Count - 1; i > 0; i--)
        {
            int j = Random.Next(i + 1);
            (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
        }

        foreach (List<JassCard> hand in _hands)
        {
            hand.Clear();
        }

        for (int card = 0; card < shuffled.Count; card++)
        {
            _hands[(_forehand + card / 9) % 4].Add(shuffled[card]);
        }

        foreach (List<JassCard> hand in _hands)
        {
            hand.Sort((left, right) =>
            {
                int suit = left.Suit.CompareTo(right.Suit);
                return suit != 0 ? suit : left.Rank.CompareTo(right.Rank);
            });
        }
    }

    public override void MoveCursor(int dx, int dy)
    {
        if (NeedsReveal || _trump is null || _handComplete || IsOver || !IsHumanTurn)
        {
            return;
        }

        int delta = dx != 0 ? dx : dy;
        _cursor = Clamp(_cursor + delta, Math.Max(0, _hands[ShownSeat].Count - 1));
    }

    public override void Activate()
    {
        _message = string.Empty;
        if (IsOver)
        {
            return;
        }

        if (_handComplete)
        {
            Version++;
            DealHand();
            return;
        }

        if (NeedsReveal)
        {
            _awaitingReveal = false;
            _cursor = 0;
            return;
        }

        if (_trump is not null && IsHumanTurn && _hands[_currentPlayer].Count > 0)
        {
            PlayCard(_cursor);
        }
    }

    public override bool TryChar(char c)
    {
        _message = string.Empty;
        if (NeedsReveal || _handComplete || IsOver || !IsHumanTurn)
        {
            return false;
        }

        if (_trump is null)
        {
            if (!_partnerChooses && char.ToUpperInvariant(c) == 'P')
            {
                PushTrump();
                return true;
            }

            JassSuit? suit = char.ToUpperInvariant(c) switch
            {
                'C' => JassSuit.Clubs,
                'D' => JassSuit.Diamonds,
                'H' => JassSuit.Hearts,
                'S' => JassSuit.Spades,
                _ => null,
            };
            if (suit is null)
            {
                return false;
            }

            ChooseTrump(suit.Value);
            return true;
        }

        if (c is >= '1' and <= '9')
        {
            PlayCard(c - '1');
            return true;
        }

        return false;
    }

    /// <summary>The forehand gives the trump choice to the partner, who must choose.</summary>
    private void PushTrump()
    {
        Version++;
        _currentPlayer = (_forehand + 2) % 4;
        _partnerChooses = true;
        _awaitingReveal = true;
    }

    /// <summary>The trump is set and the forehand leads the first trick.</summary>
    private void ChooseTrump(JassSuit suit)
    {
        Version++;
        _trump = suit;
        _currentPlayer = _forehand;
        _awaitingReveal = true;
    }

    private void PlayCard(int index)
    {
        List<JassCard> hand = _hands[_currentPlayer];
        if (index < 0 || index >= hand.Count)
        {
            return;
        }

        JassCard card = hand[index];
        if (!IsLegalPlay(hand, card, _trick, _trump!.Value))
        {
            _message = GameText.Get("Games.Jass.IllegalPlay", "Follow suit, trump if void, and overtrump when possible.");
            return;
        }

        Version++;
        if (_trick.Count == 0)
        {
            _lastTrick.Clear();
        }

        hand.RemoveAt(index);
        _trick.Add(new JassPlay(_currentPlayer, card));
        _cursor = Math.Min(_cursor, Math.Max(0, _hands[ShownSeat].Count - 1));
        _message = string.Empty;

        if (_trick.Count == 4)
        {
            int winner = TrickWinner(_trick, _trump.Value);
            _lastTrick.Clear();
            _lastTrick.AddRange(_trick);
            _tricksPlayed++;
            int points = _trick.Sum(play => CardPoints(play.Card, _trump.Value));
            _trickPoints += points;
            _handScores[winner % 2] += points;
            if (_tricksPlayed == 9)
            {
                _trickPoints += 5;
                _handScores[winner % 2] += 5;
                _scores[0] += _handScores[0];
                _scores[1] += _handScores[1];
                _handComplete = true;
                if (!IsOver)
                {
                    _message = string.Format(
                        GameText.Get("Games.Jass.HandPoints", "Hand complete: {0} points. Enter deals the next hand."),
                        _trickPoints);
                }
            }

            _trick.Clear();
            _currentPlayer = winner;
        }
        else
        {
            _currentPlayer = (_currentPlayer + 1) % 4;
        }

        if (!_handComplete)
        {
            _awaitingReveal = true;
        }
    }

    internal static bool IsLegalPlay(
        IReadOnlyCollection<JassCard> hand,
        JassCard candidate,
        IReadOnlyList<JassPlay> trick,
        JassSuit trump)
    {
        if (!hand.Contains(candidate))
        {
            return false;
        }

        if (trick.Count == 0)
        {
            return true;
        }

        JassSuit lead = trick[0].Card.Suit;
        bool hasLead = hand.Any(card => card.Suit == lead);
        if (hasLead)
        {
            if (candidate.Suit != lead)
            {
                return false;
            }

            return lead != trump || MustOvertrump(hand, candidate, trick, trump);
        }

        bool hasTrump = hand.Any(card => card.Suit == trump);
        if (!hasTrump)
        {
            return true;
        }

        if (candidate.Suit != trump)
        {
            return false;
        }

        return MustOvertrump(hand, candidate, trick, trump);
    }

    private static bool MustOvertrump(
        IReadOnlyCollection<JassCard> hand,
        JassCard candidate,
        IReadOnlyList<JassPlay> trick,
        JassSuit trump)
    {
        JassCard? highest = trick
            .Where(play => play.Card.Suit == trump)
            .Select(play => (JassCard?)play.Card)
            .OrderByDescending(card => TrumpStrength(card!.Value.Rank))
            .FirstOrDefault();
        if (highest is null || TrumpStrength(candidate.Rank) > TrumpStrength(highest.Value.Rank))
        {
            return true;
        }

        return !hand.Any(card => card.Suit == trump && TrumpStrength(card.Rank) > TrumpStrength(highest.Value.Rank));
    }

    internal static int TrickWinner(IReadOnlyList<JassPlay> trick, JassSuit trump)
    {
        if (trick.Count != 4)
        {
            throw new ArgumentException("A Jass trick must contain four cards.", nameof(trick));
        }

        JassSuit lead = trick[0].Card.Suit;
        return trick
            .OrderByDescending(play => TrickStrength(play.Card, lead, trump))
            .First()
            .Player;
    }

    internal static int TrickStrength(JassCard card, JassSuit lead, JassSuit trump)
    {
        int suitStrength = card.Suit == trump ? 2 : card.Suit == lead ? 1 : 0;
        int rankStrength = card.Suit == trump ? TrumpStrength(card.Rank) : NormalStrength(card.Rank);
        return suitStrength * 100 + rankStrength;
    }

    internal static int CardPoints(JassCard card, JassSuit trump)
    {
        if (card.Suit == trump)
        {
            return card.Rank switch
            {
                JassRank.Jack => 20,
                JassRank.Nine => 14,
                JassRank.Ace => 11,
                JassRank.King => 4,
                JassRank.Queen => 3,
                JassRank.Ten => 10,
                _ => 0,
            };
        }

        return card.Rank switch
        {
            JassRank.Ace => 11,
            JassRank.Ten => 10,
            JassRank.King => 4,
            JassRank.Queen => 3,
            JassRank.Jack => 2,
            _ => 0,
        };
    }

    internal static int TrumpStrength(JassRank rank) => rank switch
    {
        JassRank.Jack => 9,
        JassRank.Nine => 8,
        JassRank.Ace => 7,
        JassRank.King => 6,
        JassRank.Queen => 5,
        JassRank.Ten => 4,
        JassRank.Eight => 3,
        JassRank.Seven => 2,
        _ => 1,
    };

    internal static int NormalStrength(JassRank rank) => rank switch
    {
        JassRank.Ace => 9,
        JassRank.King => 8,
        JassRank.Queen => 7,
        JassRank.Jack => 6,
        JassRank.Ten => 5,
        JassRank.Nine => 4,
        JassRank.Eight => 3,
        JassRank.Seven => 2,
        _ => 1,
    };

    private static string SuitName(JassSuit suit) => GameText.Get($"Games.Jass.Suit.{suit}", suit.ToString());

    public override void Undo()
    {
    }

    public override void Pause()
    {
        if (!_handComplete && !IsOver && !_awaitingReveal)
        {
            _awaitingReveal = true;
            _message = string.Empty;
        }
    }

    /// <summary>
    /// The computer's move for its seat, worked out on a background task from a copy of the table: the trump choice or the
    /// push, or the card to play. The action applies it on the UI thread if the table has not changed since.
    /// </summary>
    public override Func<CancellationToken, Action?>? PlanComputerMove()
    {
        if (!IsComputerTurn)
        {
            return null;
        }

        List<JassCard>[] hands = [.. _hands.Select(hand => new List<JassCard>(hand))];
        List<JassPlay> trick = [.. _trick];
        JassSuit? trump = _trump;
        int seat = _currentPlayer;
        int forehand = _forehand;
        bool partnerChooses = _partnerChooses;
        int version = Version;
        TimeSpan pause = ComputerPause;
        return token =>
        {
            if (pause > TimeSpan.Zero)
            {
                token.WaitHandle.WaitOne(pause);
            }

            token.ThrowIfCancellationRequested();
            if (trump is null)
            {
                if (seat == forehand && !partnerChooses && !JassComputer.WantsTrump(hands[seat]))
                {
                    return () =>
                    {
                        if (version == Version)
                        {
                            PushTrump();
                        }
                    };
                }

                JassSuit chosen = JassComputer.BestTrump(hands[seat]);
                return () =>
                {
                    if (version == Version)
                    {
                        ChooseTrump(chosen);
                    }
                };
            }

            int index = JassComputer.ChooseCard(hands[seat], trick, trump.Value, seat);
            return index < 0 ? null : () =>
            {
                if (version == Version)
                {
                    PlayCard(index);
                }
            };
        };
    }

    public override IReadOnlyList<GameCell[]> Render()
    {
        List<GameCell[]> rows =
        [
            [new GameCell(string.Format(
                GameText.Get("Games.Jass.DealerForehandScore", "Dealer: Player {0}   Forehand: Player {1}   Score: {2} – {3}"),
                _dealer + 1,
                _forehand + 1,
                _scores[0],
                _scores[1]))],
            [new GameCell(_trump is null
                ? GameText.Get("Games.Jass.NoTrump", "Trump not chosen")
                : string.Format(
                    GameText.Get("Games.Jass.TrumpTricks", "Trump: {0}   Tricks: {1}/9   Hand points: {2}"),
                    SuitName(_trump.Value),
                    _tricksPlayed,
                    _trickPoints))],
            [new GameCell(NeedsReveal
                ? string.Format(GameText.Get("Games.Jass.PassToReveal", "Pass device to Player {0}; press Enter to reveal."), _currentPlayer + 1)
                : _handComplete
                    ? string.Empty
                : IsComputerTurn
                    ? string.Format(GameText.Get("Games.Jass.ComputerPlays", "Player {0} is the computer and is playing; your hand waits."), _currentPlayer + 1)
                    : string.Format(GameText.Get("Games.Jass.PlayerHand", "Player {0}'s hand (press 1–9 to play a card):"), ShownSeat + 1))],
        ];

        if (NeedsReveal)
        {
            rows.Add([new GameCell(string.Format(
                GameText.Get("Games.Jass.HiddenHand", "Player {0}'s cards are hidden."),
                _currentPlayer + 1))]);
        }
        else if (!_handComplete)
        {
            for (int group = 0; group < 3; group++)
            {
                string handRow = string.Join("  ", Enumerable.Range(group * 3, 3)
                    .Where(index => index < _hands[ShownSeat].Count)
                    .Select(index =>
                    {
                        string card = $"{index + 1}:{_hands[ShownSeat][index].ShortName}";
                        return index == _cursor ? $"[{card}]" : card;
                    }));
                rows.Add([new GameCell(handRow)]);
            }
        }

        IReadOnlyList<JassPlay> shownTrick = _trick.Count == 0 ? _lastTrick : _trick;
        string played = shownTrick.Count == 0
            ? GameText.Get("Games.Jass.NoCards", "No cards played in this trick.")
            : string.Join("   ", shownTrick.Select(play => string.Format(
                GameText.Get("Games.Jass.PlayedCard", "P{0}: {1}"),
                play.Player + 1,
                play.Card.ShortName)));
        rows.Add([new GameCell(played)]);
        rows.Add([new GameCell(GameText.Get(
            "Games.Jass.TeamInstruction",
            "Teams: Players 1 + 3 vs. Players 2 + 4."))]);
        return rows;
    }

    public override IEnumerable<(string Key, string Description)> ExtraKeys
    {
        get
        {
            if (!IsHumanTurn || NeedsReveal)
            {
                yield break;
            }

            if (_trump is null)
            {
                yield return ("C / D / H / S", GameText.Get("Games.Jass.ChooseTrumpHint", "Choose clubs, diamonds, hearts or spades as trump"));
                if (!_partnerChooses)
                {
                    yield return ("P", GameText.Get("Games.Jass.PushHint", "Push the trump choice to your partner"));
                }
            }
            else
            {
                yield return ("1-9", GameText.Get("Games.Jass.PlayHint", "Play the numbered card from your hand"));
            }
        }
    }

    /// <summary>Against the computer there is no device to pass, so Enter only plays.</summary>
    public override string ActivateLabel => VsComputer
        ? iConsole.Localization.UiText.Get("Games.HintPlay", "play")
        : GameText.Get("Games.Jass.ActivateLabel", "reveal / play");

    public override string ActivateHint => GameText.Get("Games.Jass.ActivateHint", "Reveal your hand or play the selected card");
}
