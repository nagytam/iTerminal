@ignore
Feature: Checking for a newer iTerminal release
  Shortly after start-up iTerminal looks for a newer release and offers it in the update dialog. Later checks only
  post a notification. On Windows the dialog can download and start the installer; elsewhere it says where the
  release is.

  Background:
    Given iTerminal has just started

  Scenario: A newer release is offered shortly after start-up
    Given release 2.0.0 is newer than this build
    When the update check has run
    Then the update dialog is shown with "2.0.0"

  Scenario: No dialog is shown when this build is the newest
    Given no release newer than this build exists
    When the update check has run
    Then no update dialog is shown

  Scenario: Later checks post a notification instead of opening the dialog
    Given release 2.0.0 was already offered in this session
    When the update check runs again
    Then a notification says that release 2.0.0 is available
    And no update dialog is opened

  Scenario: S skips the release, and Esc asks again at the next start
    Given the update dialog is open for "2.0.0"
    When I press "S"
    Then the update dialog is closed
    And release 2.0.0 is not offered again in this session

  Scenario: On Windows the installer is downloaded and started
    Given the app runs on Windows
    And the update dialog is open for "2.0.0"
    When I press "Enter" on "Install updates"
    Then the installer for "2.0.0" is downloaded
    And iTerminal is closed so the installer can replace its files

  Scenario: On Linux the dialog only says where the release is
    Given the app runs on Linux
    And the update dialog is open for "2.0.0"
    Then the dialog shows the address of release "2.0.0"
    And the dialog does not offer to install it
