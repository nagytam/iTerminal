@ignore
Feature: Bluetooth devices
  Ctrl+Shift+J lists Bluetooth devices known to BlueZ and scans, connects and pairs them. Linux only.

  Background:
    Given iTerminal has just started
    And the machine runs Linux with BlueZ

  Scenario: The device manager lists paired and nearby devices with their state
    When I press "Ctrl+Shift+J"
    Then the paired and recently seen devices are listed with connected, paired or nearby state

  Scenario: S scans for devices for eight seconds
    When I press "S"
    Then the scan runs for eight seconds and then the list shows the devices found

  Scenario: C connects the device under the cursor
    Given a paired headset is under the cursor and not connected
    When I press "C"
    Then the headset is connected
    And the status line says it is connected

  Scenario: C disconnects a connected device
    Given a connected headset is under the cursor
    When I press "C"
    Then the headset is disconnected

  Scenario: A second change is refused while one is running
    Given a connection is being made
    When I press "C" on another device
    Then nothing starts and the status line says an operation is already running

  Scenario: On Windows the device manager is not offered
    Given the machine runs Windows
    When I open the Tools menu
    Then "Bluetooth devices" is not listed
