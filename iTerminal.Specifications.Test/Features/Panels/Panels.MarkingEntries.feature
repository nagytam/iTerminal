@ignore
Feature: Marking entries to act on several at once
  Marked rows are what F5, Shift+F6 and F8 act on. Marks are cleared when the user leaves the folder.

  Background:
    Given iTerminal has just started

  Scenario: Insert marks the cursor row and moves down
    When I move the cursor to "alpha" in the left panel
    And I press "Insert"
    Then "alpha" is marked
    And the cursor is on "beta"

  Scenario: Shift+Down marks while moving
    When I press "Home"
    And I press "Shift+Down"
    And I press "Shift+Down"
    Then ".." is not marked
    And "alpha" and "beta" are marked

  Scenario: Num plus marks files matching a pattern
    When I press "NumPlus"
    And I type "*.txt"
    And I press "Enter"
    Then every ".txt" file in the left panel is marked

  Scenario: Num minus unmarks files matching a pattern
    Given every file in the left panel is marked
    When I press "NumMinus"
    And I type "*.txt"
    And I press "Enter"
    Then no ".txt" file in the left panel is marked

  Scenario: A pattern that matches nothing keeps the dialog open
    When I press "NumPlus"
    And I type "*.nothing"
    And I press "Enter"
    Then the dialog is still open
    And the dialog says that no entry matches the pattern

  Scenario: Num asterisk inverts the marks
    Given "alpha" is marked in the left panel
    When I press "NumStar"
    Then "alpha" is not marked
    And every other file is marked

  Scenario: Mark All and Unmark All from the Edit menu
    When I open the Edit menu
    And I choose "Mark All"
    Then every entry of the left panel is marked
    When I open the Edit menu
    And I choose "Unmark All"
    Then no entry of the left panel is marked

  Scenario: Marks are cleared when the user leaves the folder
    Given "alpha" is marked in the left panel
    When I move the cursor to "alpha" in the left panel
    And I press "Enter"
    And I press ".."
    Then no entry of the left panel is marked

  Scenario: F5 acts on the marked rows and not the cursor row
    Given "alpha" and "beta" are marked in the left panel
    And the cursor is on "readme.txt"
    When I press "F5"
    Then the copy dialog lists "alpha" and "beta"
    And the copy dialog does not list "readme.txt"

  Scenario: Marks are acted on even when the cursor is on the parent row
    Given the cursor is on ".."
    And "alpha" is marked
    When I press "F8"
    Then the delete dialog lists "alpha"

  Scenario: The footer counts the marked entries and their size
    Given "alpha" and "beta" are marked in the left panel
    Then the footer shows that 2 entries are marked
