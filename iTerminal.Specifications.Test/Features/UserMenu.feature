@ignore
Feature: The user menu
  Shift+F10 opens a user menu of commands the user writes in a text file. Each line is a menu entry.

  Background:
    Given iTerminal has just started
    And the user menu file holds the entry "Compile" with the command "make"

  Scenario: Shift+F10 shows the entries of the user menu
    When I press "Shift+F10"
    Then "Compile" is listed

  Scenario: Choosing an entry runs its command in the command log
    When I press "Shift+F10" and choose "Compile"
    Then "make" runs in the command log

  Scenario: The user menu file is kept in the data folder
    When I press "Shift+F10"
    Then the menu is read from "usermenu.txt" in the data folder

  Scenario: An entry is given the name of the file under the cursor
    Given the user menu holds an entry "Open in editor" with the command "code"
    And "notes.md" is under the cursor
    When I choose "Open in editor"
    Then the command runs with "notes.md"
