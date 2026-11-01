@ignore
Feature: Comparing and synchronising folders
  Shift+F2 compares the folders the two panels show, marks the differences and can synchronise them.

  Background:
    Given iTerminal has just started

  Scenario: Comparing lists every difference between the two folders
    Given the left panel and right panel show folders that differ
    When I press "Shift+F2"
    Then the comparison lists each file that is only on one side
    And each file that is newer on one side is listed with its direction

  Scenario: The comparison marks the newer copies for copying
    Given "alpha" is newer in the left folder
    When I press "Shift+F2" and "Enter"
    Then "alpha" is marked in the left panel

  Scenario: Differences with no newer side are listed but not marked
    Given "readme.txt" has the same date but a different size on both sides
    When I press "Shift+F2" and "Enter"
    Then "readme.txt" is listed as different
    And "readme.txt" is not marked on either side

  Scenario: Comparing by content finds files with the same size and date that differ
    Given two files have the same name, size and date but different bytes
    When I choose "name + content" and compare
    Then the files are listed as different

  Scenario: Include subfolders looks into folders on both sides
    When I tick "Include subfolders" and compare
    Then the differences inside the subfolders are listed

  Scenario: F5 in the comparison asks before it synchronises
    Given the comparison lists differences
    When I press "F5"
    Then a summary of the actions is shown
    And nothing is copied yet
    When I press "F5" again
    Then the actions run in a progress window

  Scenario: One-way sync can delete what only the target has, after a second confirmation
    Given "old.txt" exists only in the right folder
    When I choose "Left to right" with deletions allowed
    Then the plan lists the deletion of "old.txt"
    And the deletion needs a second confirmation

  Scenario: Deleted files in a sync go to the recycle bin
    Given a sync deletes "old.txt"
    When the sync runs
    Then "old.txt" is in the recycle bin

  Scenario: A two-way sync leaves conflicts alone
    Given "readme.txt" differs on both sides with no newer side
    When I synchronise both ways
    Then "readme.txt" is counted as a conflict and left unchanged

  Scenario: Esc closes the comparison and cancels it if it is still running
    Given a comparison of large folders is running
    When I press "Esc"
    Then the comparison stops

  Scenario: Synchronise Folders from the Edit menu plans without marks
    When I open the Edit menu
    And I choose "Synchronise Folders"
    And I press "p"
    Then the planned actions are listed
    And no marks are set in either panel

  Scenario: Synchronise Folders options change the direction and subfolders
    Given the Synchronise Folders dialog is open
    When I press "b"
    Then the direction changes to "Left to right"
    When I press "s"
    Then subfolders are included

  Scenario: Changing an option clears the plan already listed
    Given a plan is listed
    When I press "s"
    Then the listed plan is cleared
