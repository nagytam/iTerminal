@ignore
Feature: Sorting the file panels
  Shift+F11 orders the focused panel. The order belongs to the panel and survives a change of drive and a restart.

  Background:
    Given iTerminal has just started

  Scenario: Sort by size puts the largest file first and keeps folders ahead of files
    Given the left panel lists a folder and files of different sizes
    When I press "Shift+F11"
    And I choose "Size" in the sort dialog
    Then the left panel lists its folders before its files
    And the files are ordered from the largest to the smallest

  Scenario: Reversing a sort keeps folders ahead of files
    Given the left panel is sorted by size
    When I press "Shift+F11"
    And I press "r"
    Then the dialog is still open
    And the left panel lists its folders before its files

  Scenario: The parent row stays first in every order
    When I press "Shift+F11"
    And I choose "Name" in the sort dialog
    Then the cursor row of the left panel's first row is ".."

  Scenario: Rows that tie are listed by name
    Given two files of the same size are in the left panel
    When I press "Shift+F11"
    And I choose "Size" in the sort dialog
    Then the two files are listed by name

  Scenario: The footer shows the order when it is not the drive's own
    When I press "Shift+F11"
    And I choose "Size" in the sort dialog
    Then the footer shows "Sort: size"

  Scenario: The cursor stays on the same entry after a re-sort
    Given the cursor is on "beta" in the left panel
    When I press "Shift+F11"
    And I choose "Date" in the sort dialog
    Then the cursor is on "beta"

  Scenario: The sort order is kept after a restart
    Given the left panel is sorted by size
    When iTerminal is restarted
    Then the left panel is still sorted by size

  Scenario: Natural number order sorts numbers in names by value
    Given the left panel lists "file10.txt", "file2.txt" and "file1.txt"
    And "Sort numbers in names by value" is on in the Panels settings
    When I choose "Name" in the sort dialog
    Then the files are listed as "file1.txt", "file2.txt", "file10.txt"

  Scenario: The Panels settings page sets the order of both panels
    When I open the settings dialog on the Panels page
    And I choose "Size" for the left panel
    Then the left panel is sorted by size
