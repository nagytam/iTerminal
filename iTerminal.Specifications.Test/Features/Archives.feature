@ignore
Feature: Compressing and extracting archives
  Alt+F5 packs the marked entries into an archive. Shift+F5 extracts archives. Archives can also be browsed as folders.

  Background:
    Given iTerminal has just started

  Scenario: Alt+F5 creates a ZIP archive from the marked files
    Given "alpha" and "beta" are marked in the left panel
    When I press "Alt+F5"
    And I type "bundle.zip" and press "Enter"
    Then "bundle.zip" holds "alpha" and "beta"

  Scenario: Choosing TAR.GZ renames the archive to match
    When I press "Alt+F5"
    And I choose "TAR.GZ" as the format
    Then the archive name changes from "bundle.zip" to "bundle.tar.gz"

  Scenario: Typing .7z selects the 7z format
    When I press "Alt+F5"
    And I type "bundle.7z"
    Then the format is set to 7z

  Scenario: A format that cannot take a setting disables it
    When I press "Alt+F5"
    And I choose "TAR"
    Then the compression level control is disabled

  Scenario: A ZIP can be protected with a password
    When I press "Alt+F5"
    And I choose "ZIP" and enter a password with AES-256
    Then the archive opens only with that password

  Scenario: Adding to an existing archive keeps the old one if it fails
    Given "bundle.zip" holds "alpha"
    When I add "beta" to "bundle.zip" and the operation fails
    Then "bundle.zip" is unchanged

  Scenario: Shift+F5 extracts an archive into the other panel's folder
    Given "bundle.zip" is under the cursor in the left panel
    When I press "Shift+F5"
    Then the target folder is the right panel's folder
    When I confirm the extraction
    Then the archive's entries are in a folder named "bundle" in the right panel's folder

  Scenario: An entry that would leave the target folder is skipped
    Given an archive holds an entry named "../outside.txt"
    When I extract the archive
    Then "outside.txt" is not written outside the target folder
    And the extraction reports the skipped entry

  Scenario: Extracting an encrypted archive asks for its password
    Given an encrypted archive "secret.zip" is under the cursor
    When I extract "secret.zip"
    Then a password is asked for

  Scenario: A wrong password is reported as a wrong password
    Given the password typed for "secret.zip" is wrong
    When I extract "secret.zip"
    Then the message says the password is wrong

  Scenario: Enter opens an archive as a folder
    Given "bundle.zip" is under the cursor
    When I press "Enter"
    Then the left panel lists the entries of "bundle.zip"

  Scenario: A 7z archive without 7-Zip installed says why it cannot be made
    Given 7-Zip is not installed
    When I press "Alt+F5"
    Then the 7z format is not in the list and a hint says 7-Zip is needed
