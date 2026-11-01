@ignore
Feature: Running with administrator rights
  When an action needs more rights, iTerminal can restart with them. Ctrl+Shift+R restarts it elevated.

  Background:
    Given iTerminal has just started

  Scenario: Ctrl+Shift+R restarts iTerminal with administrator rights
    Given iTerminal runs without administrator rights
    When I press "Ctrl+Shift+R" and confirm
    Then iTerminal restarts elevated

  Scenario: A refused elevation keeps the current session
    Given iTerminal runs without administrator rights
    When I press "Ctrl+Shift+R" and cancel the elevation prompt
    Then the current session is still running

  Scenario: Formatting a drive without rights says to restart as administrator
    Given iTerminal runs without administrator rights
    When a format fails with "Access is denied"
    Then the message says to start iTerminal as administrator
