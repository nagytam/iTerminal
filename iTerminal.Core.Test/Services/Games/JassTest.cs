using iTerminal.Core.Services.Games;

namespace iTerminal.Core.Test.Services.Games;

[TestClass]
public class JassTest
{
    [TestMethod]
    public void ItDealsACompleteThirtySixCardDeckAndUsesTheForehandAsTheFirstChooser()
    {
        JassGame game = new(new Random(7));

        Assert.AreEqual(3, game.Dealer);
        Assert.AreEqual(0, game.Forehand);
        Assert.AreEqual(0, game.CurrentPlayer);
        Assert.AreEqual(4, game.Hands.Length);
        Assert.IsTrue(game.Hands.All(hand => hand.Count == 9));
        Assert.AreEqual(36, game.Hands.SelectMany(hand => hand).Distinct().Count());
    }

    [TestMethod]
    public void TheFirstPlayerCanPushTrumpChoiceToTheirPartner()
    {
        JassGame game = new(new Random(3));
        game.Activate();

        Assert.IsTrue(game.TryChar('p'));
        Assert.AreEqual(2, game.CurrentPlayer);
        Assert.IsNull(game.Trump);

        game.Activate();
        Assert.IsTrue(game.TryChar('s'));

        Assert.AreEqual(JassSuit.Spades, game.Trump);
        Assert.AreEqual(game.Forehand, game.CurrentPlayer);
    }

    [TestMethod]
    public void PausingHidesTheRevealedHandUntilThePlayerRevealsItAgain()
    {
        JassGame game = new(new Random(5));
        game.Activate();
        Assert.IsTrue(game.Status.Contains("choose trump", StringComparison.OrdinalIgnoreCase));

        game.Pause();

        Assert.IsTrue(game.Status.StartsWith("Pass the device", StringComparison.Ordinal));
        Assert.IsTrue(string.Join(" ", game.Render().SelectMany(row => row).Select(cell => cell.Text)).Contains("cards are hidden", StringComparison.Ordinal));
        Assert.AreEqual(9, game.Hands[game.CurrentPlayer].Count, "hiding the hand does not discard it");
    }

    [TestMethod]
    public void ItRequiresFollowingSuitAndTrumpingWhenVoid()
    {
        JassCard clubSeven = new(JassSuit.Clubs, JassRank.Seven);
        JassCard clubEight = new(JassSuit.Clubs, JassRank.Eight);
        JassCard heartAce = new(JassSuit.Hearts, JassRank.Ace);
        JassCard spadeSeven = new(JassSuit.Spades, JassRank.Seven);
        JassPlay[] clubLead = [new JassPlay(0, clubEight)];

        Assert.IsTrue(JassGame.IsLegalPlay([clubSeven, heartAce], clubSeven, clubLead, JassSuit.Spades));
        Assert.IsFalse(JassGame.IsLegalPlay([clubSeven, heartAce], heartAce, clubLead, JassSuit.Spades));
        Assert.IsFalse(JassGame.IsLegalPlay([spadeSeven, heartAce], heartAce, clubLead, JassSuit.Spades));
        Assert.IsTrue(JassGame.IsLegalPlay([spadeSeven, heartAce], spadeSeven, clubLead, JassSuit.Spades));
        Assert.IsTrue(JassGame.IsLegalPlay([heartAce], heartAce, clubLead, JassSuit.Spades), "when void in both lead suit and trump, any card is legal");
    }

    [TestMethod]
    public void ItRequiresOvertrumpingWhenAPlayerCanDoSo()
    {
        JassCard clubEight = new(JassSuit.Clubs, JassRank.Eight);
        JassCard trumpEight = new(JassSuit.Spades, JassRank.Eight);
        JassCard trumpSeven = new(JassSuit.Spades, JassRank.Seven);
        JassCard trumpNine = new(JassSuit.Spades, JassRank.Nine);
        JassCard heartAce = new(JassSuit.Hearts, JassRank.Ace);
        JassPlay[] trick = [new JassPlay(0, clubEight), new JassPlay(1, trumpEight)];

        Assert.IsFalse(JassGame.IsLegalPlay([trumpSeven, trumpNine, heartAce], trumpSeven, trick, JassSuit.Spades));
        Assert.IsTrue(JassGame.IsLegalPlay([trumpSeven, trumpNine, heartAce], trumpNine, trick, JassSuit.Spades));
        Assert.IsTrue(JassGame.IsLegalPlay([trumpSeven, heartAce], trumpSeven, trick, JassSuit.Spades), "a lower trump is legal when no higher trump is held");
    }

    [TestMethod]
    public void TrumpWinsTheTrickAndJassCardValuesTotalOneHundredAndFiftyTwo()
    {
        JassCard clubAce = new(JassSuit.Clubs, JassRank.Ace);
        JassCard clubSeven = new(JassSuit.Clubs, JassRank.Seven);
        JassCard spadeSeven = new(JassSuit.Spades, JassRank.Seven);
        JassCard heartAce = new(JassSuit.Hearts, JassRank.Ace);

        Assert.AreEqual(2, JassGame.TrickWinner(
            [new JassPlay(0, clubAce), new JassPlay(1, clubSeven), new JassPlay(2, spadeSeven), new JassPlay(3, heartAce)],
            JassSuit.Spades));

        int points = Enum.GetValues<JassSuit>()
            .SelectMany(suit => Enum.GetValues<JassRank>().Select(rank => JassGame.CardPoints(new JassCard(suit, rank), JassSuit.Spades)))
            .Sum();
        Assert.AreEqual(152, points);
        Assert.AreEqual(20, JassGame.CardPoints(new JassCard(JassSuit.Spades, JassRank.Jack), JassSuit.Spades));
        Assert.AreEqual(14, JassGame.CardPoints(new JassCard(JassSuit.Spades, JassRank.Nine), JassSuit.Spades));
    }

    [TestMethod]
    public void ACompletedHandScoresOneHundredAndFiftySevenAndTheDealerRotates()
    {
        JassGame game = new(new Random(11));
        bool sawCompletedTrick = false;
        while (game.TricksPlayed < 9)
        {
            if (game.Status.StartsWith("Pass the device", StringComparison.Ordinal))
            {
                game.Activate();
            }

            if (game.Trump is null)
            {
                Assert.IsTrue(game.TryChar('c'));
                continue;
            }

            IReadOnlyList<JassCard> hand = game.Hands[game.CurrentPlayer];
            int legalCard = Enumerable.Range(0, hand.Count)
                .First(index => JassGame.IsLegalPlay(hand, hand[index], game.Trick, game.Trump.Value));
            Assert.IsTrue(game.TryChar((char)('1' + legalCard)));
            if (game.LastTrick.Count == 4)
            {
                sawCompletedTrick = true;
            }
        }

        Assert.IsTrue(sawCompletedTrick, "the completed trick remains available for display");
        Assert.AreEqual(157, game.TeamOneScore + game.TeamTwoScore);
        Assert.IsFalse(game.IsOver);

        game.Activate();
        Assert.AreEqual(0, game.Dealer, "the dealer advances after a hand");

        game.NewGame();
        Assert.AreEqual(0, game.TeamOneScore);
        Assert.AreEqual(0, game.TeamTwoScore);
        Assert.AreEqual(3, game.Dealer);
    }
}
