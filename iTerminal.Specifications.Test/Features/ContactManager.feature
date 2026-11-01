@ignore
Feature: Contacts
  Ctrl+Shift+K keeps names, email addresses, phone numbers and companies, and filters them.

  Background:
    Given iTerminal has just started

  Scenario: Ctrl+Shift+K lists the contacts
    Given a contact "Ada Lovelace" exists
    When I press "Ctrl+Shift+K"
    Then "Ada Lovelace" is listed

  Scenario: A new contact is added
    When I press "Ctrl+Shift+K" and add a contact "Grace Hopper" with an email address
    Then "Grace Hopper" is listed with the email address

  Scenario: Typing filters the contacts
    Given the contacts "Ada" and "Grace" exist
    When I type "gr" in the filter
    Then only "Grace" is listed

  Scenario: Editing a contact keeps the highlight on it
    Given "Ada" is highlighted
    When I change the company of "Ada"
    Then "Ada" is still highlighted

  Scenario: A contact saved outside the filter clears the filter
    Given the filter shows "Grace" only
    When I save a new contact "Alan" that the filter would hide
    Then the filter is cleared and "Alan" is shown

  Scenario: Contacts are kept after a restart
    Given the contact "Ada" exists
    When iTerminal is restarted
    Then "Ada" is listed
