@ignore
Feature: Using the mouse
  The mouse works in the panels, the menus and the dialogs as an alternative to the keyboard.

  Background:
    Given iTerminal has just started

  Scenario: A click moves the cursor to a row in a panel
    When I click on "alpha" in the left panel
    Then the cursor is on "alpha"

  Scenario: A double click opens the folder
    When I double-click "alpha" in the left panel
    Then the left panel lists "inside_alpha.txt"

  Scenario: The wheel scrolls a panel
    Given the left panel lists more entries than fit on screen
    When I scroll the mouse wheel down in the left panel
    Then the list scrolls down

  Scenario: Clicking a menu title opens its menu
    When I click on "File" in the menu bar
    Then the File menu is open

  Scenario: Clicking a key bar button runs its command
    When I click on "[F5-Copy]" in the key bar
    Then the copy dialog is shown

  Scenario: A right click opens the context menu of a row
    When I right-click "alpha" in the left panel
    Then the context menu for "alpha" is shown

  Scenario: Clicking outside an open menu closes it
    Given the File menu is open
    When I click on the empty area of the screen
    Then the menu is closed
