@ignore
Feature: Disk usage, duplicate files and secure wipe
  Ctrl+Shift+F12 shows where the space goes, Ctrl+Shift+F6 finds duplicate files and Ctrl+Shift+F8 overwrites files so they cannot be recovered.

  Background:
    Given iTerminal has just started

  Scenario: Disk usage shows the largest folders and files
    When I press "Ctrl+Shift+F12"
    Then the disk usage tree shows the folders with their sizes

  Scenario: Disk usage does not follow folder links
    Given "alpha" contains a link to a large folder elsewhere
    When the disk usage of "alpha" is measured
    Then the linked folder's size is not counted

  Scenario: g in disk usage goes to the folder in the panel
    Given the disk usage tree shows "alpha"
    When I press "g"
    Then the panel shows "alpha"

  Scenario: The duplicate finder lists files with identical content
    Given "a.bin" and "copy_of_a.bin" have identical content
    When I press "Ctrl+Shift+F6" in the folder
    Then the duplicate groups list "a.bin" and "copy_of_a.bin"

  Scenario: Files of the same size but different content are not duplicates
    Given "a.bin" and "b.bin" have the same size but different content
    When I press "Ctrl+Shift+F6"
    Then "a.bin" and "b.bin" are not in the same group

  Scenario: Secure wipe asks twice before it destroys anything
    Given "secret.txt" is under the cursor
    When I press "Ctrl+Shift+F8"
    And I press "Enter"
    Then the dialog says "secret.txt" will be destroyed
    And "secret.txt" still exists
    When I press "Enter"
    Then "secret.txt" is overwritten, renamed and deleted

  Scenario: Any other key withdraws the secure wipe
    Given the secure wipe has armed "secret.txt"
    When I press "x"
    Then "secret.txt" still exists

  Scenario: Secure wipe offers one pass, three passes or seven passes
    When I press "Ctrl+Shift+F8"
    And I press "7"
    Then the wipe is set to seven passes

  Scenario: Secure wipe deletes a link and not what it points to
    Given "link_to_data" is a link to "data.bin"
    When I wipe "link_to_data"
    Then "data.bin" is not changed

  Scenario: The dialog says what an overwrite cannot reach
    When I press "Ctrl+Shift+F8"
    Then the dialog says that SSDs, snapshots and backups may keep copies
