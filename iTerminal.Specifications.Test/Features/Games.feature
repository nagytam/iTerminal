@ignore
Feature: Games
  Tools > Games offers Tic-Tac-Toe, Chess, Go and Nine Men's Morris against the computer.

  Background:
    Given iTerminal has just started

  Scenario: The games menu lists the four games
    When I open the Tools menu and choose "Games"
    Then Tic-Tac-Toe, Chess, Go and Nine Men's Morris are listed

  Scenario: The computer answers a move in Tic-Tac-Toe
    Given Tic-Tac-Toe is open and the player has moved
    Then the computer places its mark without blocking the screen

  Scenario: A chess pawn is promoted by choice
    Given a pawn can reach the last rank
    When the pawn moves there
    Then a choice of piece is offered

  Scenario: Castling is not allowed through an attacked square
    Given the king would pass through an attacked square
    When the player tries to castle
    Then the castling move is refused

  Scenario: Threefold repetition is a draw
    Given the same position has occurred three times
    Then the game is drawn

  Scenario: Nine Men's Morris takes a man after a mill
    Given the player forms a mill
    When the player moves
    Then the player may take an opponent's man that is not in a mill

  Scenario: Go places stones on intersections
    When the player places a stone on a free intersection
    Then the stone is shown on the board
