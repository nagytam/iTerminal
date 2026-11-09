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

/// <summary>Four-player, partnership Schieber Jass, played hot-seat.</summary>
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

    public override bool OffersComputerOpponent => false;

    public override bool OffersUndo => false;

    public override string? ModeLabel => GameText.Get("Games.Jass.Mode", "four players (hot-seat)");

    public override bool IsComputerTurn => false;

    public override bool CanUndo => false;

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

            if (_awaitingReveal)
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
        if (_awaitingReveal || _trump is null || _handComplete || IsOver)
        {
            return;
        }

        int delta = dx != 0 ? dx : dy;
        _cursor = Clamp(_cursor + delta, Math.Max(0, _hands[_currentPlayer].Count - 1));
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
            DealHand();
            return;
        }

        if (_awaitingReveal)
        {
            _awaitingReveal = false;
            _cursor = 0;
            return;
        }

        if (_trump is not null && _hands[_currentPlayer].Count > 0)
        {
            PlayCard(_cursor);
        }
    }

    public override bool TryChar(char c)
    {
        _message = string.Empty;
        if (_awaitingReveal || _handComplete || IsOver)
        {
            return false;
        }

        if (_trump is null)
        {
            if (!_partnerChooses && char.ToUpperInvariant(c) == 'P')
            {
                _currentPlayer = (_forehand + 2) % 4;
                _partnerChooses = true;
                _awaitingReveal = true;
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

            _trump = suit;
            _currentPlayer = _forehand;
            _awaitingReveal = true;
            return true;
        }

        if (c is >= '1' and <= '9')
        {
            PlayCard(c - '1');
            return true;
        }

        return false;
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
        _cursor = Math.Min(_cursor, Math.Max(0, hand.Count - 1));
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

    private static int TrickStrength(JassCard card, JassSuit lead, JassSuit trump)
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

    private static int TrumpStrength(JassRank rank) => rank switch
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

    private static int NormalStrength(JassRank rank) => rank switch
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

    public override Func<CancellationToken, Action?>? PlanComputerMove() => null;

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
            [new GameCell(_awaitingReveal
                ? string.Format(GameText.Get("Games.Jass.PassToReveal", "Pass device to Player {0}; press Enter to reveal."), _currentPlayer + 1)
                : string.Format(GameText.Get("Games.Jass.PlayerHand", "Player {0}'s hand (press 1–9 to play a card):"), _currentPlayer + 1))],
        ];

        if (_awaitingReveal)
        {
            rows.Add([new GameCell(string.Format(
                GameText.Get("Games.Jass.HiddenHand", "Player {0}'s cards are hidden."),
                _currentPlayer + 1))]);
        }
        else
        {
            for (int group = 0; group < 3; group++)
            {
                string handRow = string.Join("  ", Enumerable.Range(group * 3, 3)
                    .Where(index => index < _hands[_currentPlayer].Count)
                    .Select(index =>
                    {
                        string card = $"{index + 1}:{_hands[_currentPlayer][index].ShortName}";
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
            "Teams: Players 1 + 3 vs. Players 2 + 4. P pushes the first trump choice to partner."))]);
        return rows;
    }

    public override IEnumerable<(string Key, string Description)> ExtraKeys
    {
        get
        {
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

    public override string ActivateLabel => GameText.Get("Games.Jass.ActivateLabel", "reveal / play");

    public override string ActivateHint => GameText.Get("Games.Jass.ActivateHint", "Reveal your hand or play the selected card");
}
