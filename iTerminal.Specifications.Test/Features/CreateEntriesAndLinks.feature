@ignore
Feature: Creating folders, files and links
  Shift+F7 creates a folder, Shift+F4 a file and Ctrl+L a link to the entry under the cursor.

  Background:
    Given iTerminal has just started

  Scenario: Shift+F7 creates a folder in the panel's folder
    When I press "Shift+F7"
    And I type "newfolder"
    And I press "Enter"
    Then the left panel lists "newfolder"

  Scenario: Shift+F4 creates an empty file
    When I press "Shift+F4"
    And I type "notes.txt"
    And I press "Enter"
    Then the left panel lists "notes.txt"
    And "notes.txt" is empty

  Scenario: A name that already exists keeps the dialog open
    When I press "Shift+F7"
    And I type "alpha"
    And I press "Enter"
    Then the dialog is still open
    And the dialog says that "alpha" already exists

  Scenario: Ctrl+L creates a symbolic link by default
    When I move the cursor to "alpha" in the left panel
    And I press "Ctrl+L"
    And I press "Enter"
    Then a symbolic link named "alpha" is offered in the right panel's folder

  Scenario: A hard link to a folder is refused
    When I move the cursor to "alpha" in the left panel
    And I press "Ctrl+L"
    And I choose "Hard link"
    Then the dialog says that hard links are for files only

  Scenario: A link is shown as name to target
    Given a symbolic link "shortcut" to "alpha" is in the left panel
    Then the left panel lists "shortcut → alpha"

  Scenario: Ctrl+Shift+L edits the target of a symbolic link
    Given a symbolic link "shortcut" to "alpha" is under the cursor
    When I press "Ctrl+Shift+L"
    And I type "beta" as the new target
    And I press "Enter"
    Then "shortcut" points to "beta"

  Scenario: A file with only one name cannot be edited as a hard link
    Given "readme.txt" has one name
    When I move the cursor to "readme.txt" and press "Ctrl+Shift+L"
    Then the dialog says the file has only one name
