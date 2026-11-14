namespace iTerminal.Core.Services.Games;

/// <summary>
/// The computer's seats at the Jass table. It chooses trump from the points its hand would score in each suit, and plays
/// the way a partnership plays: win a trick that the opponents hold only when it is cheap to do, feed the partner's trick
/// with points when it is the last to play, and otherwise throw away the card that gives the least away. Pure functions of
/// the hand and the trick, so the search runs on a background task against a copy of the table.
/// </summary>
internal static class JassComputer
{
    /// <summary>A hand with less than this in its best suit is not worth choosing trump for; the forehand pushes it to the partner.</summary>
    private const int TrumpThreshold = 45;

    /// <summary>The suit the hand is strongest in: the points its trumps would score, plus a little for each trump held.</summary>
    public static JassSuit BestTrump(IReadOnlyList<JassCard> hand)
    {
        JassSuit best = JassSuit.Clubs;
        int bestScore = int.MinValue;
        foreach (JassSuit suit in Enum.GetValues<JassSuit>())
        {
            int score = TrumpScore(hand, suit);
            if (score > bestScore)
            {
                bestScore = score;
                best = suit;
            }
        }

        return best;
    }

    /// <summary>Whether the forehand should choose trump itself rather than push it to the partner.</summary>
    public static bool WantsTrump(IReadOnlyList<JassCard> hand) => TrumpScore(hand, BestTrump(hand)) >= TrumpThreshold;

    private static int TrumpScore(IReadOnlyList<JassCard> hand, JassSuit suit) =>
        hand.Where(card => card.Suit == suit).Sum(card => JassGame.CardPoints(card, suit) + 3);

    /// <summary>The index in the hand of the card to play, or -1 when there is none (an empty hand).</summary>
    public static int ChooseCard(IReadOnlyList<JassCard> hand, IReadOnlyList<JassPlay> trick, JassSuit trump, int seat)
    {
        List<int> legal = [.. Enumerable.Range(0, hand.Count).Where(i => JassGame.IsLegalPlay(hand, hand[i], trick, trump))];
        if (legal.Count == 0)
        {
            return -1;
        }

        if (legal.Count == 1)
        {
            return legal[0];
        }

        if (trick.Count == 0)
        {
            // Lead the strongest side-suit card, which tends to win; with only trumps left, lead the strongest of them.
            return legal.MaxBy(i => hand[i].Suit == trump
                ? JassGame.TrumpStrength(hand[i].Rank)
                : 100 + JassGame.NormalStrength(hand[i].Rank) + JassGame.CardPoints(hand[i], trump));
        }

        JassSuit lead = trick[0].Card.Suit;
        JassPlay winning = trick.MaxBy(play => JassGame.TrickStrength(play.Card, lead, trump));
        int winningStrength = JassGame.TrickStrength(winning.Card, lead, trump);
        bool partnerWinning = winning.Player == (seat + 2) % 4;

        if (partnerWinning)
        {
            // The partner holds the trick, so points put in now are points the team keeps. The last to play may give them all;
            // anyone earlier plays low, in case an opponent still overtakes.
            return trick.Count == 3
                ? legal.MaxBy(i => (JassGame.CardPoints(hand[i], trump), -JassGame.TrickStrength(hand[i], lead, trump)))
                : legal.MinBy(i => (JassGame.CardPoints(hand[i], trump), JassGame.TrickStrength(hand[i], lead, trump)));
        }

        List<int> winners = legal.Where(i => JassGame.TrickStrength(hand[i], lead, trump) > winningStrength).ToList();
        if (winners.Count > 0)
        {
            // The cheapest card that takes the trick from the opponents.
            return winners.MinBy(i => JassGame.TrickStrength(hand[i], lead, trump));
        }

        return legal.MinBy(i => (JassGame.CardPoints(hand[i], trump), JassGame.TrickStrength(hand[i], lead, trump)));
    }
}
