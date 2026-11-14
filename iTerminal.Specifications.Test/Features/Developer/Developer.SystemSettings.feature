@ignore
Feature: System settings of the machine
  Ctrl+Shift+F10 shows the settings of the machine itself: its name, workgroup and description, the environment
  variables of the user and of the machine, and the time zone. Left and Right (or Tab) change section.

  Background:
    Given iTerminal has just started

  Scenario: Ctrl+Shift+F10 opens the system settings on the first section
    When I press "Ctrl+Shift+F10"
    Then the system settings dialog is open
    And the dialog shows the machine's name

  Scenario: Left and Right move between sections
    Given the system settings dialog is open
    When I press "Right"
    Then the dialog shows the environment variables section
    When I press "Left"
    Then the dialog shows the machine's name section

  Scenario: Enter edits the highlighted row
    Given the system settings dialog is open on the machine's name
    When I press "Enter" and type "lab-pc"
    And I press "Enter"
    Then the machine's name is "lab-pc"

  Scenario: A change that needs administrator rights and has none is refused with the reason
    Given the system settings dialog is open on the machine's name
    And iTerminal is not running with administrator rights
    When I press "Enter" and type "lab-pc"
    And I press "Enter"
    Then the bottom row says that administrator rights are needed
    And the dialog shows at the top that it has no administrator rights

  Scenario: The time zone section shows the zone in use
    Given the system settings dialog is open
    When I press "Right" until the time zone section is shown
    Then the dialog shows the time zone in use
