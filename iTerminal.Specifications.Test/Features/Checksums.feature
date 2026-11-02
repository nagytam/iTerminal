@ignore
Feature: Checksums of files
  Shift+F12 shows the MD5, SHA-1, SHA-256 and SHA-512 of the file under the cursor and compares them with a value typed in.

  Background:
    Given iTerminal has just started

  Scenario: Shift+F12 shows all four checksums of the file
    When I move the cursor to "readme.txt" in the left panel
    And I press "Shift+F12"
    Then the dialog shows the MD5, SHA-1, SHA-256 and SHA-512 of "readme.txt"

  Scenario: A typed checksum is compared as it is typed
    Given the checksum dialog is open for "readme.txt"
    When I type the SHA-256 of "readme.txt"
    Then the dialog says the checksums match

  Scenario: A checksum that does not match is reported
    Given the checksum dialog is open for "readme.txt"
    When I type a SHA-256 that differs from the file
    Then the dialog says the checksums do not match

  Scenario: Spaces and colons in a typed checksum are ignored
    Given the checksum dialog is open for "readme.txt"
    When I type the SHA-256 with spaces between each pair of digits
    Then the dialog says the checksums match

  Scenario: A checksum file beside the file is checked without being asked
    Given "setup.exe.sha256" holds the checksum of "setup.exe"
    When I move the cursor to "setup.exe" and press "Shift+F12"
    Then the dialog says that the file matches "setup.exe.sha256"

  Scenario: A SHA256SUMS list in the folder is used for the file
    Given a "SHA256SUMS" list in the folder holds the checksum of "setup.exe"
    When I move the cursor to "setup.exe" and press "Shift+F12"
    Then the dialog says the file matches the list

  Scenario: F2 writes a checksum file for the file
    When I move the cursor to "readme.txt" and press "Shift+F12"
    And I press "F2"
    Then "readme.txt.sha256" is created

  Scenario: F2 never overwrites a checksum file that already says something else
    Given "readme.txt.sha256" holds a different checksum
    When I press "F2" in the checksum dialog for "readme.txt"
    Then "readme.txt.sha256" is not changed
    And the dialog says the existing file holds a different checksum

  Scenario: Checksums work on a file inside an archive
    Given the left panel shows the inside of a ZIP archive
    When I move the cursor to a file in the archive and press "Shift+F12"
    Then the four checksums of that file are shown

  Scenario: Closing the dialog stops a checksum that is still being computed
    Given a large file's checksum is being computed
    When I press "Esc"
    Then the computation is cancelled
