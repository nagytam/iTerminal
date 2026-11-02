@ignore
Feature: Deleting files and folders
  F8 deletes the marked entries (or the cursor row). On a local disk the recycle bin is the default.

  Background:
    Given iTerminal has just started

  Scenario: F8 asks before deleting
    When I move the cursor to "alpha" in the left panel
    And I press "F8"
    Then the delete dialog names "alpha"
    And nothing has been deleted yet

  Scenario: Local deletes go to the recycle bin by default
    When I move the cursor to "readme.txt" in the left panel
    And I press "F8"
    And I press "Enter"
    Then "readme.txt" is in the recycle bin
    And the left panel does not list "readme.txt"

  Scenario: Deleting a folder is recursive by default
    When I move the cursor to "alpha" in the left panel
    And I press "F8"
    Then the "Recursively delete folder" box is ticked

  Scenario: A recursive delete does not follow a folder link
    Given "alpha" contains a folder link to "outside" with files
    When I delete "alpha" recursively
    Then the files of "outside" still exist
    And the link itself is deleted

  Scenario: Delete for good skips the recycle bin
    When I move the cursor to "readme.txt" in the left panel
    And I press "F8"
    And I choose "Delete for good"
    And I press "Enter"
    Then "readme.txt" is not in the recycle bin

  Scenario: Esc cancels the delete dialog and deletes nothing
    When I move the cursor to "alpha" in the left panel
    And I press "F8"
    And I press "Esc"
    Then "alpha" still exists

  Scenario: The parent row is never deleted
    Given the cursor is on ".."
    When I press "F8"
    Then the delete dialog is not shown

  Scenario: Deleting from a read-only archive is refused
    Given the left panel shows the inside of a ZIP archive
    When I press "F8"
    Then the archive is not changed
