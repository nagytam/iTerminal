@ignore
Feature: Copy, cut and paste of files with the clipboard
  Ctrl+C, Ctrl+X and Ctrl+V copy, cut and paste files through the system clipboard, so they work with other programs.

  Background:
    Given iTerminal has just started

  Scenario: Ctrl+C copies the marked files and Ctrl+V pastes them in another folder
    Given "alpha" is marked in the left panel
    When I press "Ctrl+C"
    And I move the left panel into "target"
    And I press "Ctrl+V"
    Then "alpha" is in "target"
    And "alpha" is still in the original folder

  Scenario: Ctrl+X cuts and moves the files only when they are pasted
    Given "alpha" is under the cursor in the left panel
    When I press "Ctrl+X"
    Then "alpha" is still in the left panel
    When I move the right panel into "target" and press "Ctrl+V"
    Then "alpha" is in "target"

  Scenario: Pasting into the same folder names the copy like Explorer
    Given "readme.txt" is copied with Ctrl+C
    When I press "Ctrl+V" in the same folder
    Then the new file is named "readme - Copy.txt"

  Scenario: A cut pasted into its own folder does nothing
    Given "readme.txt" is cut with Ctrl+X
    When I press "Ctrl+V" in the same folder
    Then "readme.txt" is not changed

  Scenario: Paste never overwrites a file already there
    Given "readme.txt" is copied and a file of that name exists in the target
    When I press "Ctrl+V" in the target
    Then the paste reports the name is taken and the existing file is kept

  Scenario: Files from another program can be pasted into iTerminal
    Given files were copied in another program
    When I press "Ctrl+V" in a panel
    Then the files are copied into the panel's folder

  Scenario: Ctrl+C on a file copies it and does not quit
    Given the cursor is on "alpha" in the left panel
    When I press "Ctrl+C"
    Then "alpha" is on the clipboard
    And iTerminal keeps running

  Scenario: F10 quits, Ctrl+C does not
    Given the cursor is on "alpha" in the left panel
    When I press "Ctrl+C"
    Then iTerminal keeps running
    When I press "F10"
    Then iTerminal closes
