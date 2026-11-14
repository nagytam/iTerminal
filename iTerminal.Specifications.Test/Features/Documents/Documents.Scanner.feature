@ignore
Feature: Scanning pages with a connected scanner
  Ctrl+Shift+I opens the scanner dialog. It scans one page or the whole document feeder, shows what was scanned,
  and saves it only when asked.

  Background:
    Given iTerminal has just started

  Scenario: Ctrl+Shift+I opens the scanner dialog
    When I press "Ctrl+Shift+I"
    Then the screen shows "Scan"

  Scenario: The dialog lists the scanners that answer
    Given two scanners are connected
    When I press "Ctrl+Shift+I"
    Then the dialog lists both scanners

  Scenario: With no scanner the dialog says so and offers to search again
    Given no scanner is connected
    When I press "Ctrl+Shift+I"
    Then the dialog says that no scanner was found
    And the dialog offers to search again

  Scenario: A scanned page is shown before anything is saved
    Given a scanner is connected
    When I scan one page in the scanner dialog
    Then the scanned page is shown in the dialog
    And no file has been saved

  Scenario: The document feeder scans every page it holds
    Given a scanner with a document feeder holds three pages
    When I scan the whole feeder in the scanner dialog
    Then three pages are shown in the dialog

  Scenario: A scan is saved under the name typed into the dialog
    Given a scanner is connected
    And I have scanned one page in the scanner dialog
    When I save the scan as "scan-001"
    Then the saved scan is created in the folder of the left panel
