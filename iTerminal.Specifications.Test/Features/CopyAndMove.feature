@ignore
Feature: Copying and moving files and folders
  F5 copies and Shift+F6 moves the marked entries (or the cursor row) to the folder shown in the other panel.

  Background:
    Given iTerminal has just started

  Scenario: F5 opens the copy dialog with the other panel's folder as the target
    Given the right panel shows "target"
    When I move the cursor to "alpha" in the left panel
    And I press "F5"
    Then the copy dialog is shown
    And its target folder is the folder of the right panel

  Scenario: Copying runs in the background with a progress window
    When I move the cursor to "alpha" in the left panel
    And I press "F5"
    And I press "Enter"
    Then a progress window for the copy is shown
    And the task appears in the task manager

  Scenario: A finished copy appears in the other panel
    When I copy "alpha" from the left panel to the right panel
    Then the right panel lists "alpha"
    And a completion notification is shown

  Scenario: A copy keeps the modification date of the source
    Given "readme.txt" was last modified on 2020-01-02 10:00
    When I copy "readme.txt" to the right panel
    Then the copy of "readme.txt" was last modified on 2020-01-02 10:00

  Scenario: A copy into a folder with the same name asks what to do
    Given "readme.txt" also exists in the target folder
    When I copy "readme.txt" to the right panel
    Then a conflict prompt offers Overwrite, Skip, Rename, Keep newer and Keep larger

  Scenario: Skip keeps both files and finishes the step
    Given a conflict prompt is shown for "readme.txt"
    When I choose "Skip"
    Then the file in the target folder is unchanged
    And the source file is kept

  Scenario: Rename keeps both copies under a new name
    Given a conflict prompt is shown for "readme.txt"
    When I choose "Rename"
    Then the copy is named "readme (2).txt" in the target folder

  Scenario: Apply to all answers the rest of the run
    Given a conflict prompt is shown for "readme.txt" in a run of five files
    When I choose "Overwrite" and tick "Apply to all"
    Then no further conflict prompt is shown in that run

  Scenario: Esc in the conflict prompt cancels the run
    Given a conflict prompt is shown
    When I press "Esc"
    Then the steps after the current one are not run

  Scenario: A failed step does not stop the others
    Given one of five files cannot be read
    When I copy the five files to the right panel
    Then the other four files are copied
    And the failure is listed when the run ends

  Scenario: Copying stops when the target has too little free space
    Given the target drive has less free space than the source needs
    When I copy "big.iso" to the right panel
    Then a prompt asks whether to pause or go on anyway

  Scenario: Shift+F6 moves the entry and removes it from the source
    When I move "alpha" from the left panel to the right panel with "Shift+F6"
    Then the right panel lists "alpha"
    And the left panel does not list "alpha"

  Scenario: Moving within one volume does not need free space
    Given the source and the target are on the same volume
    When I move "alpha" with "Shift+F6"
    Then no free space check is shown

  Scenario: Copying a folder into itself is refused
    Given the target folder is inside the folder being copied
    When I copy "alpha" to its own subfolder
    Then the copy is refused with a message

  Scenario: The parent row is never copied
    Given the cursor is on ".."
    When I press "F5"
    Then the copy dialog is not shown
