@ignore
Feature: Command palette and help
  Shift+F1 lists every command that can run now. F1 shows help. About is under the Help menu.

  Background:
    Given iTerminal has just started

  Scenario: The command palette lists the commands with their keys
    When I press "Shift+F1"
    Then the palette lists commands such as "Copy" with the key "F5"

  Scenario: Typing narrows the palette
    When I press "Shift+F1" and type "theme"
    Then only commands matching "theme" are listed

  Scenario: Every word of the query must match
    When I press "Shift+F1" and type "copy folder"
    Then only commands whose text contains both words are listed

  Scenario: Enter runs the chosen command against the panel that was focused
    Given the left panel is focused and "alpha" is under the cursor
    When I press "Shift+F1" and choose "Rename" and press "Enter"
    Then the rename dialog is shown for "alpha"

  Scenario: Recently run commands come first with an empty query
    Given the command "Theme Manager" was run last
    When I press "Shift+F1"
    Then "Theme Manager" is listed first

  Scenario: F1 shows help for the current control
    When I press "F1"
    Then the help lists the keys that work here

  Scenario: The About dialog shows the version
    When I press "Ctrl+Shift+Alt+F1"
    Then the About dialog shows the application version

  Scenario: The Help menu lists the same commands
    When I open the Help menu
    Then the menu lists "Help", "Command Palette" and "About"
