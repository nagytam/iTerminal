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
        game.VsComputer = false;
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
        game.VsComputer = false;
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
        game.VsComputer = false;
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

    [TestMethod]
    public void TheComputerSeatsPlayAHandToTheEndAndTheScoreAddsUpTo157()
    {
        JassGame game = new(new Random(11)) { ComputerPause = TimeSpan.Zero };

        int guard = 0;
        while (game.TricksPlayed < 9)
        {
            Assert.IsTrue(++guard < 2000, "the hand stalled");
            if (game.IsComputerTurn)
            {
                game.PlanComputerMove()!(CancellationToken.None)?.Invoke();
            }
            else if (game.Trump is null)
            {
                Assert.IsTrue(game.TryChar(SuitLetter(JassComputer.BestTrump(game.Hands[0]))));
            }
            else
            {
                int index = JassComputer.ChooseCard(game.Hands[0], game.Trick, game.Trump.Value, 0);
                Assert.IsTrue(index >= 0 && game.TryChar((char)('1' + index)));
            }
        }

        Assert.AreEqual(157, game.TeamOneScore + game.TeamTwoScore);
    }

    [TestMethod]
    public void WhenThePersonPushesTheComputerPartnerChoosesTheTrump()
    {
        JassGame game = new(new Random(7)) { ComputerPause = TimeSpan.Zero };

        Assert.IsTrue(game.TryChar('P'));
        Assert.IsTrue(game.IsComputerTurn, "Player 3, the partner, is the computer");

        game.PlanComputerMove()!(CancellationToken.None)?.Invoke();

        Assert.IsNotNull(game.Trump);
        Assert.AreEqual(0, game.CurrentPlayer, "the forehand leads after the choice");
        Assert.IsFalse(game.IsComputerTurn);
    }

    [TestMethod]
    public void WhileTheComputerPlaysTheHeaderSaysSoInsteadOfInvitingACard()
    {
        JassGame game = new(new Random(7));
        Assert.IsTrue(game.TryChar('P'));

        Assert.IsTrue(game.Render().Any(row => row.Any(cell => cell.Text.Contains("is the computer and is playing"))));
        Assert.IsFalse(game.Render().Any(row => row.Any(cell => cell.Text.Contains("press 1–9"))));
    }

    [TestMethod]
    public void ThePersonCannotPlayForTheComputerSeats()
    {
        JassGame game = new(new Random(7));
        Assert.IsTrue(game.TryChar('P'));
        Assert.IsTrue(game.IsComputerTurn);

        Assert.IsFalse(game.TryChar('C'), "the partner's trump choice is not the person's to make");
        Assert.IsNull(game.Trump);
    }

    private static char SuitLetter(JassSuit suit) => suit switch
    {
        JassSuit.Clubs => 'C',
        JassSuit.Diamonds => 'D',
        JassSuit.Hearts => 'H',
        _ => 'S',
    };

    [TestMethod]
    public void ANeedlessHandPromptIsNotShownOnceTheHandIsOver()
    {
        JassGame game = new(new Random(11)) { ComputerPause = TimeSpan.Zero };
        int guard = 0;
        while (game.TricksPlayed < 9 && guard++ < 2000)
        {
            if (game.IsComputerTurn)
            {
                game.PlanComputerMove()!(CancellationToken.None)?.Invoke();
            }
            else if (game.Trump is null)
            {
                game.TryChar('S');
            }
            else
            {
                int index = JassComputer.ChooseCard(game.Hands[0], game.Trick, game.Trump.Value, 0);
                game.TryChar((char)('1' + index));
            }
        }

        Assert.IsTrue(game.Render().All(row => row.All(cell => !cell.Text.Contains("press 1–9"))), "the hands are empty, so there is no card to play");
    }
}
