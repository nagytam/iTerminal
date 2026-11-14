@ignore
Feature: Screen manager lists the desktop's screens
  F12 opens the screen manager, which lists the desktop's screens and marks the one on show. The desktop runs one
  screen at a time and cannot switch, so the list is for looking at only.

  Background:
    Given iTerminal has just started

  Scenario: F12 opens the screen manager
    When I press "F12"
    Then the screen shows "Screen manager"

  Scenario: The screen on show is marked
    Given the screen manager is open
    Then the screen on show is marked in the list

  Scenario: A screen is named after its type, without the namespace
    Given the screen manager is open
    Then no row shows "iTerminal.Core.Screens"

  Scenario: Enter does nothing, and the hint row says so
    Given the screen manager is open
    When I press "Enter"
    Then the screen manager is still open
    And the hint row says that screens cannot be switched
