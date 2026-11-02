@ignore
Feature: Comparing two files
  Ctrl+F10 shows two files side by side with their differences highlighted.

  Background:
    Given iTerminal has just started

  Scenario: Two marked files are compared
    Given exactly two files are marked in the left panel
    When I press "Ctrl+F10"
    Then the diff view shows both files side by side

  Scenario: Without marks the cursor file is compared with the other panel's cursor file
    Given the cursor is on "alpha.txt" in the left panel
    And the cursor is on "beta.txt" in the right panel
    When I press "Ctrl+F10"
    Then "alpha.txt" and "beta.txt" are compared

  Scenario: N and P jump between differences
    Given the diff view shows several differences
    When I press "N"
    Then the next difference is at the cursor
    When I press "P"
    Then the previous difference is at the cursor

  Scenario: W ignores whitespace and C ignores case
    Given two files differ only in spaces
    When I press "W"
    Then the files are shown as the same

  Scenario: F4 switches between lines and bytes
    Given the diff view shows text
    When I press "F4"
    Then the files are shown as bytes side by side
    And the differing bytes are highlighted

  Scenario: A file over the size limit is refused
    Given a file larger than 8 MB is marked
    When I press "Ctrl+F10"
    Then the diff view says the file is too large to compare

  Scenario: A right-click context menu offers the compare command on a file
    When I right-click "alpha.txt"
    Then the context menu lists the file's programs and the panel's commands
