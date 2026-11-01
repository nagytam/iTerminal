@ignore
Feature: Behaviour common to all dialogs
  Every dialog closes with Esc, moves focus with Tab and takes Enter for its main button. A dialog that is open takes the keys before the panels do.

  Background:
    Given iTerminal has just started

  Scenario: Esc closes any dialog without changing anything
    When I press "F5" and then "Esc"
    Then the copy dialog is closed
    And no file was copied

  Scenario: Tab moves focus between the controls of a dialog
    When I press "F5"
    And I press "Tab"
    Then the focus moves to the next control of the dialog

  Scenario: Enter runs the main button of the dialog
    When I press "F5" and then "Enter"
    Then the copy starts

  Scenario: Keys do not reach the panel behind an open dialog
    Given the copy dialog is open
    When I press "Down"
    Then the cursor of the panel behind the dialog does not move

  Scenario: F1 explains the keys of the dialog
    When I press "F5"
    And I press "F1"
    Then a help page lists the keys that work in the copy dialog

  Scenario: Text boxes support the usual editing keys
    Given the rename dialog is open with "alpha"
    When I press "Home" and type "x"
    Then the name is "xalpha"

  Scenario: Ctrl+C and Ctrl+V copy and paste text inside a text box
    Given the rename dialog is open with "alpha"
    When I select all and press "Ctrl+C"
    And I clear the text and press "Ctrl+V"
    Then the name is "alpha"

  Scenario: The footer shows the keys of the focused control
    When I press "F5"
    Then the footer shows the keys of the copy dialog
