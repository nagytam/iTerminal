// Step bindings for Features/Games.feature. Each body is pending until the step is implemented;
// the scenarios of that feature are tagged @ignore until the bindings are implemented and tested.
using Reqnroll;

namespace iTerminal.Specifications.Test.Steps;

[Binding]
public sealed class GamesSteps
{
    [When(@"I open the Tools menu and choose {string}")]
    public void WhenIOpenTheToolsMenuAndChooseValue(string value1) => throw new PendingStepException();

    [Then(@"Tic-Tac-Toe, Chess, Go and Nine Men's Morris are listed")]
    public void ThenTicTacToeChessGoAndNineMenSMorrisAreListed() => throw new PendingStepException();

    [Given(@"Tic-Tac-Toe is open and the player has moved")]
    public void GivenTicTacToeIsOpenAndThePlayerHasMoved() => throw new PendingStepException();

    [Then(@"the computer places its mark without blocking the screen")]
    public void ThenTheComputerPlacesItsMarkWithoutBlockingTheScreen() => throw new PendingStepException();

    [Given(@"a pawn can reach the last rank")]
    public void GivenAPawnCanReachTheLastRank() => throw new PendingStepException();

    [When(@"the pawn moves there")]
    public void WhenThePawnMovesThere() => throw new PendingStepException();

    [Then(@"a choice of piece is offered")]
    public void ThenAChoiceOfPieceIsOffered() => throw new PendingStepException();

    [Given(@"the king would pass through an attacked square")]
    public void GivenTheKingWouldPassThroughAnAttackedSquare() => throw new PendingStepException();

    [When(@"the player tries to castle")]
    public void WhenThePlayerTriesToCastle() => throw new PendingStepException();

    [Then(@"the castling move is refused")]
    public void ThenTheCastlingMoveIsRefused() => throw new PendingStepException();

    [Given(@"the same position has occurred three times")]
    public void GivenTheSamePositionHasOccurredThreeTimes() => throw new PendingStepException();

    [Then(@"the game is drawn")]
    public void ThenTheGameIsDrawn() => throw new PendingStepException();

    [Given(@"the player forms a mill")]
    public void GivenThePlayerFormsAMill() => throw new PendingStepException();

    [When(@"the player moves")]
    public void WhenThePlayerMoves() => throw new PendingStepException();

    [Then(@"the player may take an opponent's man that is not in a mill")]
    public void ThenThePlayerMayTakeAnOpponentSManThatIsNotInAMill() => throw new PendingStepException();

    [When(@"the player places a stone on a free intersection")]
    public void WhenThePlayerPlacesAStoneOnAFreeIntersection() => throw new PendingStepException();

    [Then(@"the stone is shown on the board")]
    public void ThenTheStoneIsShownOnTheBoard() => throw new PendingStepException();
}
