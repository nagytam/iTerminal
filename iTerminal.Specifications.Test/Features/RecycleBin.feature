@ignore
Feature: The recycle bin
  Ctrl+F8 browses deleted files, restores them, or deletes them for good.

  Background:
    Given iTerminal has just started
    And "old.txt" was deleted to the recycle bin

  Scenario: Ctrl+F8 lists the deleted files
    When I press "Ctrl+F8"
    Then the focused panel lists "old.txt"

  Scenario: Restore puts a file back where it was deleted from
    Given the focused panel shows the recycle bin
    When I move the cursor to "old.txt" and choose "Restore"
    Then "old.txt" is back in its original folder

  Scenario: Delete for good removes a file from the recycle bin
    Given the focused panel shows the recycle bin
    When I move the cursor to "old.txt" and delete it for good
    Then "old.txt" is no longer in the recycle bin

  Scenario: Pressing the recycle bin key again goes back
    Given the focused panel shows the recycle bin
    When I press "Ctrl+F8"
    Then the panel shows the folder it showed before

  Scenario: The recycle bin lists the newest deletions first
    Given "newer.txt" was deleted after "old.txt"
    When I press "Ctrl+F8"
    Then "newer.txt" is listed before "old.txt"
