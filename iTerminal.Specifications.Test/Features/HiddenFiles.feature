@ignore
Feature: Hidden files and folders in the panels
  Hidden entries are not listed unless the Panels settings say to show them.

  Background:
    Given iTerminal has just started

  Scenario: Hidden entries are not listed by default
    Given the left panel contains a hidden file ".config"
    Then the left panel does not list ".config"

  Scenario: Showing hidden files lists them in both panels
    When I open the settings dialog on the Panels page
    And I tick "Show hidden files and folders"
    Then the left panel lists ".config"
    And the right panel lists ".config"

  Scenario: The parent row is never hidden
    Given the left panel shows a hidden folder
    When I move the cursor into the hidden folder
    Then the cursor row is ".."
    And I can press Enter to go back to its parent

  Scenario: Hidden entries still take part in a copy
    Given a hidden file ".secret" is in the left panel
    When I select all entries of the left panel and press "F5"
    Then the copy includes ".secret"
