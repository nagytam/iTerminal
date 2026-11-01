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
