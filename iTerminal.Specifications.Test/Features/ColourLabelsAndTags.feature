@ignore
Feature: Colour labels and tags
  The Edit menu gives files and folders a colour and tags. The panel shows the colour and can be filtered by it.

  Background:
    Given iTerminal has just started

  Scenario: A colour label is set on the marked files
    Given "alpha" and "beta" are marked
    When I open the Edit menu and choose "Colour Labels and Tags"
    And I choose the colour "blue"
    And I press "Enter"
    Then "alpha" and "beta" are shown in blue

  Scenario: Tags are added to a file
    When I open the Edit menu and choose "Colour Labels and Tags"
    And I type the tags "invoice, 2026"
    And I press "Enter"
    Then the file has the tags "invoice" and "2026"

  Scenario: Several items keep what they already have
    Given "alpha" has the colour "red" and "beta" has no colour
    When I mark both and open "Colour Labels and Tags"
    Then the colour is shown as "(keep)"

  Scenario: Filter by Label narrows the panel to one colour
    Given "alpha" is red and "readme.txt" has no label
    When I open the Edit menu and choose "Filter by Label"
    And I choose "red"
    Then the left panel lists "alpha" and its folders
    And the left panel does not list "readme.txt"

  Scenario: A renamed file loses its label
    Given "alpha" is red
    When I rename "alpha" to "omega"
    Then "omega" has no colour

  Scenario: Labels are not applied to archive entries
    Given the left panel shows the inside of a ZIP archive
    Then the Colour Labels and Tags command is not available
