@ignore
Feature: Viewing files with F3
  F3 opens the focused file in the built-in viewer. Quick view (Ctrl+F3) previews the file under the cursor in the other panel.

  Background:
    Given iTerminal has just started

  Scenario: F3 shows the text of a file
    When I move the cursor to "readme.txt" in the left panel
    And I press "F3"
    Then the viewer shows the first lines of "readme.txt"

  Scenario: Esc closes the viewer
    Given the viewer shows "readme.txt"
    When I press "Esc"
    Then the viewer is closed
    And the left panel has the keyboard

  Scenario: F2 wraps long lines to the width of the view
    Given the viewer shows a file with a line longer than the screen
    When I press "F2"
    Then the long line is broken across rows

  Scenario: F5 goes to a line number
    Given the viewer shows a file of 500 lines
    When I press "F5"
    And I type "250" and press "Enter"
    Then line 250 is shown a third of the way down the view

  Scenario: Searching in the viewer finds the text and steps through the hits
    Given the viewer shows a file with the word "error" on three lines
    When I search for "error"
    Then the first hit is shown
    When I press "F3"
    Then the next hit is shown

  Scenario: Home and End move to the start and end of the file
    Given the viewer shows a long file
    When I press "End"
    Then the last line is at the bottom of the view
    When I press "Home"
    Then the first line is at the top of the view

  Scenario: The wheel scrolls the view three rows at a time
    Given the viewer shows a long file
    When I scroll the mouse wheel down once
    Then the view moves down three rows

  Scenario: F8 and Shift+F8 switch the code page
    Given the viewer shows a file with accented text in Windows-1250
    When I press "F8"
    Then the text is shown under the next code page
    When I press "Shift+F8"
    Then the text is shown under the previous code page

  Scenario: F4 in the viewer shows the hex dump
    Given the viewer shows "data.bin"
    When I press "F4"
    Then the file is shown as bytes with offsets
    When I press "F5"
    And I type "0x10"
    Then the view goes to byte offset 16

  Scenario: Syntax colouring follows the file name
    Given the viewer shows "Program.cs"
    Then the keywords of the C# language are coloured

  Scenario: Quick view previews the cursor file in the other panel
    When I move the cursor to "readme.txt" in the left panel
    And I press "Ctrl+F3"
    Then the right panel shows the first lines of "readme.txt"
    And pressing "Ctrl+F3" again returns the right panel to its folder

  Scenario: A binary file opens in the viewer as bytes
    Given the viewer opens "image.png" as a picture or as bytes
    Then the file is not shown as garbled text

  Scenario: Quick view and the viewer leave a file unchanged
    When I view "readme.txt" with F3
    Then the modification date of "readme.txt" is unchanged
