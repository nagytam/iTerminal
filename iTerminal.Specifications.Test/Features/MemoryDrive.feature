@ignore
Feature: The in-memory drive
  The memdrive:// drive holds files and folders in memory for scratch work. Nothing is written to disk.

  Background:
    Given iTerminal has just started

  Scenario: The memory drive is listed in the Places page
    When I press "Ctrl+F1"
    And I choose the Places page
    Then "memdrive://" is listed

  Scenario: A file copied into the memory drive is kept only in memory
    Given the right panel shows "memdrive://"
    When I copy "alpha" from the left panel to the right panel
    Then the right panel lists "alpha"
    And no new file appears on the disk

  Scenario: Moving a folder into itself is refused
    Given "memdrive://work" contains a folder "sub"
    When I move "work" into "work/sub"
    Then the move is refused before anything is removed

  Scenario: Creating a folder in the memory drive root
    Given the right panel shows "memdrive://"
    When I press "Shift+F7" and type "scratch"
    Then "memdrive://scratch" is listed
