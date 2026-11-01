Feature: Moving around the file panels
  The cursor keys move the cursor in the focused panel, and Enter opens the folder under it.

  Background:
    Given iTerminal has just started

  Scenario: Down and Up move the cursor
    When I press "Home"
    And I press "Down"
    Then the cursor is on "alpha"
    When I press "Down"
    Then the cursor is on "beta"
    When I press "Up"
    Then the cursor is on "alpha"

  Scenario: End and Home jump to the ends of the list
    When I press "End"
    Then the cursor is on "readme.txt"
    When I press "Home"
    Then the cursor is on ".."

  Scenario: Enter opens the folder under the cursor
    When I move the cursor to "alpha" in the left panel
    And I press "Enter"
    Then the left panel lists "inside_alpha.txt"

  Scenario: Ctrl+PgUp goes back to the folder that was left
    When I move the cursor to "beta" in the left panel
    And I press "Enter"
    And I press "Ctrl+PgUp"
    Then the cursor is on "beta"

  Scenario: Tab gives the keyboard to the other panel
    When I press "Tab"
    Then the right panel has the keyboard
    When I move the cursor to "right_only.txt" in the right panel
    Then the cursor is on "right_only.txt"

  Scenario: Shift+Tab gives the keyboard back to the panel before
    When I press "Tab"
    And I press "Shift+Tab"
    Then the left panel has the keyboard

  Scenario: Alt with a letter starts typing to search
    When I press "Alt+r"
    Then the screen shows "Filter: r"
    And the cursor is on "readme.txt"

  Scenario: Typing more narrows the search and Esc clears it
    When I press "Alt+n"
    And I type "ot"
    Then the screen shows "Filter: not"
    And the cursor is on "notes.md"
    When I press "Esc"
    Then the screen does not show "Filter:"

  Scenario: A new file appears in the panel without a refresh
    Given a file "dropped_in.txt" appears in the left folder
    Then the left panel lists "dropped_in.txt"
