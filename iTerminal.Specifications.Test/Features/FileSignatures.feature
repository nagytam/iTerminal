@ignore
Feature: Signing files and verifying signatures
  Ctrl+Shift+F7 lists the signatures of the file under the cursor, verifies them and signs the file with gpg, Authenticode or a key file.

  Background:
    Given iTerminal has just started

  Scenario: Opening the dialog verifies the existing signatures
    Given "release.tar.gz" has an OpenPGP signature "release.tar.gz.asc"
    When I move the cursor to "release.tar.gz" and press "Ctrl+Shift+F7"
    Then the dialog shows a verdict for the OpenPGP signature

  Scenario: A file with no signature says so
    When I move the cursor to "readme.txt" and press "Ctrl+Shift+F7"
    Then the dialog says the file is not signed

  Scenario: F2 signs with gpg using the default key
    Given gpg is installed and has a default key
    When I move the cursor to "release.tar.gz" and press "Ctrl+Shift+F7"
    And I press "F2"
    Then "release.tar.gz.asc" is created

  Scenario: Signing never overwrites an existing signature file
    Given "release.tar.gz.asc" already exists
    When I press "F2" in the signature dialog
    Then "release.tar.gz.asc" is not overwritten
    And the dialog says a signature file already exists

  Scenario: A key id that starts with a dash is refused
    When I type "-oops" as the key and press "F2"
    Then the signing is refused and nothing runs

  Scenario: F5 checks the signature again
    Given the signature dialog is open for a signed file
    When I press "F5"
    Then the verdict is shown again

  Scenario: Closing the dialog cancels work still running
    Given the signature dialog is verifying a file
    When I press "Esc"
    Then the verification stops
