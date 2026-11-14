@ignore
Feature: Opening files with other programs
  Shift+F3 (Open with) and Ctrl+F4 (Edit with) choose a program for the file under the cursor. Enter and F4 then use the default.

  Background:
    Given iTerminal has just started

  Scenario: Shift+F3 lists the programs for the file under the cursor
    Given "photo.jpg" is under the cursor
    When I press "Shift+F3"
    Then the Open With dialog lists the programs for ".jpg" files

  Scenario: Adding a program makes it the first choice for matching files
    Given the Open With dialog is shown for "photo.jpg"
    When I press "Insert" and add the program "gimp" for "*.jpg"
    Then "gimp" is the first program for ".jpg" files

  Scenario: Space makes a program the default
    Given the Open With dialog lists "viewer" and "gimp"
    When I move to "gimp" and press "Space"
    Then Enter on a ".jpg" file runs "gimp"

  Scenario: Del removes a program after a y or n answer
    Given the Open With dialog lists "gimp"
    When I move to "gimp" and press "Del"
    Then the footer asks whether to remove "gimp"
    When I press "y"
    Then "gimp" is no longer listed

  Scenario: Enter opens a file with the system default program when no program is chosen
    Given no program is chosen for ".pdf" files
    When I move the cursor to "manual.pdf" and press "Enter"
    Then the system default program opens "manual.pdf"

  Scenario: Enter runs an executable file in the command log
    Given "tool.sh" is executable
    When I move the cursor to "tool.sh" and press "Enter"
    Then "tool.sh" runs in the command log from its own folder

  Scenario: F4 uses the default edit program for files of that kind
    Given "notes.md" has a default edit program "code"
    When I move the cursor to "notes.md" and press "F4"
    Then "code" opens "notes.md"

  Scenario: Edit with keeps the built-in editor one row away
    When I press "Ctrl+F4"
    Then the built-in editor is listed among the programs

  Scenario: Programs are not offered for a file inside an archive
    Given the left panel shows the inside of a ZIP archive
    When I press "Shift+F3" on an entry
    Then the dialog says a program can only open a file on this machine

  Scenario: A program that is not installed is reported in words
    Given "missingtool" is the default program for ".xyz" files
    When I move the cursor to "data.xyz" and press "Enter"
    Then a notification says that "missingtool" is not installed
