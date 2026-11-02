@ignore
Feature: File and folder properties
  Ctrl+F12 shows what the disk records about the entry under the cursor and lets the changeable part be set.

  Background:
    Given iTerminal has just started

  Scenario: Ctrl+F12 shows the size, dates and attributes
    When I move the cursor to "readme.txt" in the left panel
    And I press "Ctrl+F12"
    Then the properties dialog shows the size, created, modified and accessed dates

  Scenario: Changing the modified date is written to the disk
    Given the properties dialog is open for "readme.txt"
    When I set the modified date to "2021-03-04 05:06:07" and save
    Then "readme.txt" was last modified on 2021-03-04 05:06:07

  Scenario: A date in the wrong form is refused before anything is written
    Given the properties dialog is open for "readme.txt"
    When I set the modified date to "not a date" and save
    Then the dialog says the date is not valid
    And no date of "readme.txt" is changed

  Scenario: A folder's size is added up behind the dialog
    When I move the cursor to "alpha" and press "Ctrl+F12"
    Then the dialog shows the folder's size as a running total

  Scenario: The properties of a link describe the link
    Given a symbolic link "shortcut" to "alpha" is under the cursor
    When I press "Ctrl+F12"
    Then the dialog describes "shortcut" as a link to "alpha"

  Scenario: Colour labels and tags are shown in properties
    Given "readme.txt" has the colour "red" and the tag "draft"
    When I press "Ctrl+F12"
    Then the dialog shows the colour "red" and the tag "draft"

  Scenario: Ctrl+P in properties opens the permissions dialog
    Given the properties dialog is open for "readme.txt"
    When I press "Ctrl+P"
    Then the permissions dialog for "readme.txt" is shown
