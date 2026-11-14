@ignore
Feature: Network manager and network browser
  Ctrl+F5 shows network connections and their state. Ctrl+N browses the machines on the local network.

  Background:
    Given iTerminal has just started

  Scenario: Ctrl+F5 lists the network connections and their state
    When I press "Ctrl+F5"
    Then the network manager lists each interface with its address and state

  Scenario: The network manager refreshes its list
    Given the network manager is open
    When I press "F5"
    Then the list is read again

  Scenario: Ctrl+N browses the machines on the local network
    When I press "Ctrl+N"
    Then the network browser lists the computers it finds

  Scenario: Enter on a computer in the network browser opens its shares
    Given the network browser lists "server"
    When I press "Enter" on "server"
    Then the focused panel lists the shares of "server"

  Scenario: A computer that does not answer is listed as unreachable
    Given the network browser lists a computer that does not respond
    Then that computer is shown as unreachable
