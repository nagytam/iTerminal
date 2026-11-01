@ignore
Feature: The terminal panel and the interactive shell
  F2 opens a terminal, Ctrl+O lends the whole terminal to a shell, and the command log keeps a record of commands.

  Background:
    Given iTerminal has just started

  Scenario: F2 opens the terminal over the panels
    When I press "F2"
    Then a terminal is shown
    And the terminal is ready for input

  Scenario: A command typed in the terminal runs and its output is shown
    Given the terminal is open
    When I type "echo hello" and press "Enter"
    Then the output "hello" is shown in the terminal

  Scenario: Ctrl+O hands the whole terminal to the shell
    When I press "Ctrl+O"
    Then the shell prompt is shown on the whole screen
    And the file panels are hidden

  Scenario: Returning from the shell restores the panels
    Given the shell was opened with Ctrl+O
    When I type "exit" and press "Enter"
    Then the file panels are shown again

  Scenario: Ctrl+C goes to the shell while it has the terminal
    Given the shell was opened with Ctrl+O
    When I press "Ctrl+C"
    Then the interrupt goes to the shell and iTerminal keeps running

  Scenario: Ctrl+Shift+O opens the command log
    When I press "Ctrl+Shift+O"
    Then the command log is shown
    And it lists the commands run in this session

  Scenario: A program that needs the full terminal is lent it
    Given a file "notes.txt" has the terminal editor "nano" as its edit program
    When I press "F4" on "notes.txt"
    Then the terminal editor takes the whole terminal
    And the panels are re-read when it exits
