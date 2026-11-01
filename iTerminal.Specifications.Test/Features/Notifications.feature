@ignore
Feature: Notifications and toasts
  Background work reports its outcome as a toast that goes away on its own.

  Background:
    Given iTerminal has just started

  Scenario: A finished copy shows a toast
    When a copy finishes
    Then a toast says the copy is complete

  Scenario: A toast disappears after a few seconds
    Given a toast is shown
    When a few seconds pass
    Then the toast is gone

  Scenario: A failure is shown as an error notification
    When a file operation fails
    Then an error notification names the file that failed

  Scenario: Several notifications stack
    When two notifications arrive together
    Then both are shown one above the other
