@ignore
Feature: Panel views and navigation keys
  Ctrl+T shows the names in columns, Ctrl+U swaps the panels, Ctrl+R re-reads a folder and Ctrl+Space shows a folder's size.

  Background:
    Given iTerminal has just started

  Scenario: Ctrl+T shows the names in several columns
    Given the left panel lists many files
    When I press "Ctrl+T"
    Then the names are shown in several columns, filled row by row

  Scenario: Arrow keys move between columns in brief view
    Given the left panel is in brief view
    When I press "Right"
    Then the cursor moves to the next column

  Scenario: Ctrl+U swaps the two panels' folders
    When I press "Ctrl+U"
    Then the left panel shows the folder the right panel showed
    And the right panel shows the folder the left panel showed

  Scenario: Ctrl+R re-reads the folder
    Given "new.txt" was created in the left folder by another program
    When I press "Ctrl+R"
    Then the left panel lists "new.txt"

  Scenario: Ctrl+Backslash goes to the root folder of the drive
    When I press "Ctrl+Backslash"
    Then the left panel shows the root folder

  Scenario: Ctrl+Space shows the size of the folder under the cursor
    When I move the cursor to "alpha" and press "Ctrl+Space"
    Then the footer shows the size of "alpha"

  Scenario: Ctrl+Space works on a folder that holds a link back to its parent
    Given "alpha" contains a link back to its parent folder
    When I press "Ctrl+Space" on "alpha"
    Then the size is counted without following the link forever

  Scenario: The footer shows the total size of the marked entries
    Given "alpha" and "beta" are marked
    Then the footer shows the number of marks and their total size

  Scenario: Alt with a letter types to search the panel
    When I press "Alt+r" in the left panel
    Then the cursor moves to the first entry whose name starts with "r"

  Scenario: The Preview panel offers no brief view
    Given the right panel is the preview of "readme.txt"
    When I press "Ctrl+T" in the right panel
    Then the right panel stays in its normal view
