@ignore
Feature: Quitting iTerminal
  F10 quits. The terminal is restored to how it was before iTerminal started.

  Background:
    Given iTerminal has just started

  Scenario: F10 quits the application
    When I press "F10"
    Then iTerminal closes

  Scenario: Quitting restores the terminal
    When I press "F10"
    Then the terminal shows the cursor and the normal screen again

