@ignore
Feature: Scanning files with Microsoft Defender
  On Windows the context menu hands the marked files and folders, or the file under the cursor, to Defender's
  command-line scanner. Only files on a local disk are scanned; the scanner is given a path.

  Background:
    Given iTerminal has just started

  Scenario: The scan is offered on Windows for a local file
    Given the app runs on Windows
    When I move the cursor to "setup.exe" in the left panel
    And I open the context menu
    Then the context menu offers "Scan with Microsoft Defender"

  Scenario: The scan is not offered on Linux
    Given the app runs on Linux
    When I move the cursor to "readme.txt" in the left panel
    And I open the context menu
    Then the context menu does not offer "Scan with Microsoft Defender"

  Scenario: The scan is not offered for a file inside an archive
    Given the app runs on Windows
    And the left panel shows the inside of a ZIP archive
    When I move the cursor to a file in the archive
    And I open the context menu
    Then the context menu does not offer "Scan with Microsoft Defender"

  Scenario: A clean result is reported
    Given the app runs on Windows
    And Defender finds no threat in "setup.exe"
    When I scan "setup.exe" with Microsoft Defender
    Then the notification says "setup.exe" is clean

  Scenario: Threats found are named, with what Defender did about them
    Given the app runs on Windows
    And Defender finds a threat in "eicar.com"
    When I scan "eicar.com" with Microsoft Defender
    Then the notification names the threat found in "eicar.com"
    And the notification says what Defender did with it

  Scenario: A scan that cannot start says why
    Given the app runs on Windows
    And Defender's command-line scanner is not installed
    When I scan "setup.exe" with Microsoft Defender
    Then the notification says the Defender scanner could not be started
