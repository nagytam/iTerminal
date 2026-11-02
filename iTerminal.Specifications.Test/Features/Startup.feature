Feature: Starting iTerminal
  A user who starts the app for the first time sees the menus, the key bar and two file panels.

  Background:
    Given iTerminal has just started

  Scenario: The menu bar is on the top row
    Then the menu bar is on the top row

  Scenario: The key bar shows the function keys
    Then the key bar starts with "[F1-Help][F2-Terminal][F3-View][F4-Edit][F5-Copy][F6-Rename][F7-Find][F8-Delete]"
    And the key bar contains "[F10-Quit]"

  Scenario: The left panel lists its folder
    Then the left panel lists "alpha"
    And the left panel lists "beta"
    And the left panel lists "readme.txt"

  Scenario: The right panel lists its folder
    Then the right panel lists "target"
    And the right panel lists "right_only.txt"

  Scenario: The left panel has the keyboard at first
    Then the left panel has the keyboard
    And the cursor is on ".."

  Scenario: Each panel is titled with its folder, the left one on the left
    Then each panel is titled with its folder

  Scenario: Folders are listed before files
    Then "beta" is listed before "notes.md" in the left panel

  Scenario: Each panel footer counts its entries
    Then the panel footers show 5 and 3 entries

  Scenario: The splash screen is gone once the app is up
    Then the splash screen is gone

  Scenario: Starting the app makes no sound
    Then nothing beeped

  Scenario: The database is kept in the configured data folder
    Then the database is in the data folder
