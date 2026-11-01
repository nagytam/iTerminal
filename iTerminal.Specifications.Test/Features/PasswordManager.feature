@ignore
Feature: Password manager
  Ctrl+X opens the password manager. Ctrl+Shift+X opens it from anywhere. Credentials are kept in the vault, never in a URL.

  Background:
    Given iTerminal has just started

  Scenario: Ctrl+X opens the password manager
    When I press "Ctrl+X"
    Then the password manager lists the saved entries

  Scenario: Adding an entry with a name, a user and a password
    When I press "Ctrl+X" and add an entry "Mail" with user "ada" and a password
    Then "Mail" is listed
    And the password is not shown in the list

  Scenario: Ctrl+Shift+X opens the password manager from a dialog
    Given a dialog is open
    When I press "Ctrl+Shift+X"
    Then the password manager is shown

  Scenario: A password is copied to the clipboard on request
    Given "Mail" is listed
    When I choose to copy the password of "Mail"
    Then the password is on the clipboard

  Scenario: A password typed into a URL is not stored
    When I type "ftp://ada:secret@host/" as an address
    Then the password is not saved in the address history
