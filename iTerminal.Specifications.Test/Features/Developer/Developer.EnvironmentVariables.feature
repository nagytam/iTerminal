@ignore
Feature: Environment variables editor
  Ctrl+Shift+H edits the environment variables in three scopes, cycled with S. Session is the environment of
  this running iTerminal, which the programs it starts inherit and which is gone when it closes. User and Machine
  are saved; Machine needs administrator rights.

  Background:
    Given iTerminal has just started

  Scenario: Ctrl+Shift+H opens the editor on the Session scope
    When I press "Ctrl+Shift+H"
    Then the environment variables editor is open
    And its scope is "Session"

  Scenario: S cycles the scope through Session, User and Machine
    Given the environment variables editor is open
    When I press "S" three times
    Then its scope is "Session"

  Scenario: A variable added to the Session scope is inherited by a program started from iTerminal
    Given the environment variables editor is open
    When I add the variable "ITERM_TEST" with the value "42"
    And I start a program that prints "ITERM_TEST"
    Then the program prints "42"

  Scenario: A Session variable is gone when iTerminal closes
    Given I have added the Session variable "ITERM_TEST" with the value "42"
    When iTerminal is closed and started again
    Then "ITERM_TEST" is not set

  Scenario: Deleting a variable asks first
    Given the environment variables editor is open with "ITERM_TEST" in the list
    When I press "D" on "ITERM_TEST"
    Then the footer asks whether to delete "ITERM_TEST"
    And "ITERM_TEST" is still listed until I answer yes

  Scenario: A name containing an equals sign is refused and the form stays open
    Given the environment variables editor is open
    When I add a variable named "BAD=NAME" with the value "1"
    Then the form says the name is not valid
    And the form is still open

  Scenario: R reads the variables again from the system
    Given the environment variables editor is open
    And a User variable was changed outside iTerminal
    When I press "R"
    Then the list shows the changed value

  Scenario: Typing / filters the list by name or value
    Given the environment variables editor is open
    When I press "/" and type "PATH"
    Then only variables whose name or value contains "PATH" are listed
