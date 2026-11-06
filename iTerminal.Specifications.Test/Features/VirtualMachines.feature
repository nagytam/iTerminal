@ignore
Feature: Virtual machines with libvirt
  Shift+Alt+F9 lists the virtual machines of libvirt (Linux) or Hyper-V (Windows) and starts, stops, saves, pauses and opens their consoles.

  Background:
    Given iTerminal has just started
    And libvirt has a machine "devbox" that is shut off

  Scenario: The manager lists the machines and their state
    When I press "Shift+Alt+F9"
    Then "devbox" is listed as "shut off"

  Scenario: S starts the selected machine
    When I press "S" on "devbox"
    Then "devbox" is started

  Scenario: Enter starts a machine that is shut off and does nothing else
    When I press "Enter" on a running machine
    Then nothing is changed

  Scenario: D shuts a running machine down after a y or n answer
    Given "devbox" is running
    When I press "D"
    Then the status line asks whether to shut down "devbox"
    When I press "y"
    Then "devbox" shuts down

  Scenario: Shift+X powers a machine off
    Given "devbox" is running
    When I press "Shift+X" and confirm
    Then "devbox" is powered off

  Scenario: C opens the console of a running machine
    Given "devbox" is running
    When I press "C"
    Then the console opens in virt-viewer

  Scenario: A missing libvirt group is explained
    Given the user is not in the libvirt group
    When I press "Shift+Alt+F9"
    Then the message names the libvirt group

  Scenario: macOS does not offer the manager
    Given the machine runs macOS
    Then the Tools menu does not list "Virtual Machine Manager"
