@ignore
Feature: Editing files with F4
  F4 opens the built-in editor. Ctrl+S saves, F5 goes to a line and closing with changes asks first.

  Background:
    Given iTerminal has just started

  Scenario: F4 opens the focused text file for editing
    When I move the cursor to "readme.txt" in the left panel
    And I press "F4"
    Then the editor shows "readme.txt"

  Scenario: Typing changes the file and Ctrl+S saves it
    Given the editor shows "readme.txt"
    When I type "new line" at the start of the file
    And I press "Ctrl+S"
    Then "readme.txt" starts with "new line"
    And the editor shows no unsaved changes

  Scenario: Closing a changed file asks what to do
    Given the editor has unsaved changes to "readme.txt"
    When I press "Esc"
    Then a prompt offers Save, Discard and Cancel

  Scenario: Discard closes the editor and keeps the file as it was
    Given the editor has unsaved changes to "readme.txt"
    When I press "Esc" and choose "Discard"
    Then the file "readme.txt" is unchanged

  Scenario: Cancel keeps the editor open
    Given the editor has unsaved changes to "readme.txt"
    When I press "Esc" and choose "Cancel"
    Then the editor is still open with its changes

  Scenario: Tab inserts spaces up to the next multiple of four
    Given the editor is open on an empty line
    When I press "Tab"
    Then four spaces are inserted

  Scenario: Shift+Tab removes one indent step
    Given the editor has an indented line
    When I press "Shift+Tab"
    Then the line loses one indent step

  Scenario: Enter keeps the indent of the line it splits
    Given the cursor is after "    return" in the editor
    When I press "Enter"
    Then the new line starts with four spaces

  Scenario: Undo and redo
    Given the editor has typed "abc"
    When I press "Ctrl+Z"
    Then "abc" is removed
    When I press "Ctrl+Y"
    Then "abc" is typed again

  Scenario: Find and replace in the editor
    Given the editor shows "cat cat dog"
    When I replace "cat" with "bird"
    Then the editor shows "bird bird dog"

  Scenario: Bookmarks let the cursor return to a line
    Given the cursor is on line 10 of the editor
    When I set a bookmark on line 10
    And I move the cursor to line 200
    And I jump to the bookmark
    Then the cursor is on line 10

  Scenario: Saving keeps the file's line endings
    Given "windows.txt" uses Windows line endings
    When I edit and save "windows.txt"
    Then "windows.txt" still uses Windows line endings

  Scenario: F5 in the editor goes to a line
    Given the editor shows a file of 300 lines
    When I press "F5" and type "120"
    Then line 120 is shown in the editor

  Scenario: F8 re-reads the file under another code page
    Given the editor shows a file with accented text
    When I press "F8"
    Then the text is shown under the next code page

  Scenario: Editing a read-only file is refused
    Given "locked.txt" is read-only
    When I press "F4" on "locked.txt"
    And I type "x" and press "Ctrl+S"
    Then the save is refused with a message

  Scenario: Syntax colouring while editing
    Given the editor shows "script.py"
    Then the Python keywords are coloured
