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

  Scenario: The taskbar shows the progress of a running copy
    Given the app runs on Windows
    When a copy of 100 files is running
    Then the taskbar button shows the progress of the copy

  Scenario: The taskbar indicator clears when the work is done
    Given the app runs on Windows
    And a copy has shown progress on the taskbar
    When the copy finishes
    Then the taskbar indicator is cleared
