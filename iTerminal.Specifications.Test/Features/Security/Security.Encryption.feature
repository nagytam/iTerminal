@ignore
Feature: Encrypting and decrypting files with a passphrase
  Ctrl+Shift+E encrypts the marked files, or the file under the cursor, to separate .enc files with a passphrase.
  The files are AES-256-GCM encrypted, with the key derived from the passphrase by PBKDF2.

  Background:
    Given iTerminal has just started

  Scenario: Ctrl+Shift+E opens the encryption dialog for the file under the cursor
    When I move the cursor to "secret.txt" in the left panel
    And I press "Ctrl+Shift+E"
    Then the encryption dialog is open for "secret.txt"

  Scenario: Encrypting writes an .enc file beside the original
    Given the encryption dialog is open for "secret.txt"
    When I type the passphrase "correct horse" twice and press "Enter"
    Then "secret.txt.enc" is created
    And the dialog reports that the file is encrypted

  Scenario: All marked files are encrypted in one run
    Given "a.txt" and "b.txt" are marked in the left panel
    When I press "Ctrl+Shift+E" and type the passphrase "correct horse" twice
    Then "a.txt.enc" and "b.txt.enc" are created

  Scenario: The two passphrase fields must match before anything is written
    Given the encryption dialog is open for "secret.txt"
    When I type the passphrase "one" and then "two" in the two fields
    Then the dialog says the passphrases do not match
    And "secret.txt.enc" is not created

  Scenario: The originals are kept unless the user asks for them to be deleted
    Given the encryption dialog is open for "secret.txt"
    When I type the passphrase "correct horse" twice and choose to keep the originals
    Then "secret.txt" still exists

  Scenario: Choosing to delete the originals removes them once the encrypted copy is written
    Given the encryption dialog is open for "secret.txt"
    When I type the passphrase "correct horse" twice and choose to delete the originals
    Then "secret.txt.enc" is created
    And "secret.txt" is deleted

  Scenario: An .enc file is decrypted with the same passphrase
    Given "secret.txt.enc" was made from "secret.txt" with the passphrase "correct horse"
    When I move the cursor to "secret.txt.enc" and press "Ctrl+Shift+E"
    And I type the passphrase "correct horse" and press "Enter"
    Then "secret.txt" is restored with its original contents

  Scenario: A wrong passphrase does not decrypt the file
    Given "secret.txt.enc" was made from "secret.txt" with the passphrase "correct horse"
    When I move the cursor to "secret.txt.enc" and press "Ctrl+Shift+E"
    And I type the passphrase "wrong" and press "Enter"
    Then the dialog says the passphrase is wrong
    And "secret.txt" is not created

  Scenario: A changed .enc file is refused
    Given "secret.txt.enc" has been changed since it was encrypted
    When I move the cursor to "secret.txt.enc" and press "Ctrl+Shift+E"
    And I type the passphrase "correct horse" and press "Enter"
    Then the dialog says the file has been altered
    And "secret.txt" is not created

  Scenario: Esc stops an encryption that is running and removes the partial output
    Given a large file is being encrypted
    When I press "Esc"
    Then the encryption is stopped
    And the partial output is removed
