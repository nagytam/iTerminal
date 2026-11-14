@ignore
Feature: Renaming a file or folder
  F6 renames the entry under the cursor. Edit > Multi-rename renames many entries by a pattern.

  Background:
    Given iTerminal has just started

  Scenario: F6 renames the entry under the cursor
    When I move the cursor to "alpha" in the left panel
    And I press "F6"
    And I type "omega"
    And I press "Enter"
    Then the left panel lists "omega"
    And the left panel does not list "alpha"

  Scenario: A rename that clashes with an existing name is refused
    When I move the cursor to "alpha" in the left panel
    And I press "F6"
    And I type "beta"
    And I press "Enter"
    Then the rename dialog stays open
    And the dialog says that "beta" already exists

  Scenario: Esc cancels a rename
    When I move the cursor to "alpha" in the left panel
    And I press "F6"
    And I type "omega"
    And I press "Esc"
    Then the left panel lists "alpha"

  Scenario: Multi-rename adds a counter to the marked files
    Given "a.txt" and "b.txt" are marked in the left panel
    When I open the Edit menu
    And I choose "Multi-rename"
    And I enter the pattern "photo_[N:2]"
    Then the preview shows "a.txt" becoming "photo_01.txt"
    And the preview shows "b.txt" becoming "photo_02.txt"

  Scenario: Multi-rename keeps the extension when the pattern does not name it
    Given "a.txt" is marked in the left panel
    When I open the Edit menu
    And I choose "Multi-rename"
    And I enter the pattern "[C]_copy"
    Then the preview shows "a.txt" becoming "a_copy.txt"

  Scenario: Find and Replace applies to the expanded pattern
    Given "draft_a.txt" is marked in the left panel
    When I open the Edit menu
    And I choose "Multi-rename"
    And I enter the pattern "[C]" with Find "draft" and Replace "final"
    Then the preview shows "draft_a.txt" becoming "final_a.txt"

  Scenario: Multi-rename marks a name that would be taken as a conflict
    Given "a.txt" and "b.txt" are marked and "b.txt" already exists
    When I open the Edit menu
    And I choose "Multi-rename"
    And I enter the pattern "[C]"
    Then the preview marks the row for "a.txt" as a conflict and does not rename it

  Scenario: Multi-rename renames the cursor row when nothing is marked
    Given the cursor is on "alpha"
    When I open the Edit menu
    And I choose "Multi-rename"
    Then the preview lists only "alpha"

  Scenario: Multi-rename cannot rename inside an archive
    Given the left panel shows the inside of an archive
    When I open the Edit menu
    Then "Multi-rename" is not available for the archive panel

  Scenario: Multi-rename applies the new names and reports failures
    Given the preview lists five files ready to rename
    When I confirm the multi-rename
    Then the five files are renamed as the preview showed
    And a file that changed on disk in the meantime is listed as an error
