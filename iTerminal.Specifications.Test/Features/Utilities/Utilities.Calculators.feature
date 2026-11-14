@ignore
Feature: Calculator, unit converter, calendar and to-do list
  Ctrl+A opens the calculator, Ctrl+E the unit converter, Ctrl+D the calendar and Ctrl+Y the to-do list.

  Background:
    Given iTerminal has just started

  Scenario: The calculator evaluates an expression
    When I press "Ctrl+A"
    And I type "2 + 3 * 4"
    And I press "Enter"
    Then the result shows 14

  Scenario: The calculator reports a division by zero in words
    When I press "Ctrl+A"
    And I type "1 / 0" and press "Enter"
    Then the result says the division is not defined

  Scenario: The unit converter changes kilometres to miles
    When I press "Ctrl+E"
    And I convert 10 kilometres to miles
    Then the result shows about 6.21 miles

  Scenario: The unit converter refuses units of different kinds
    When I press "Ctrl+E"
    And I convert 10 kilograms to metres
    Then the converter says the units are of different kinds

  Scenario: The calendar opens on the current month
    When I press "Ctrl+D"
    Then the calendar shows the current month with today marked

  Scenario: A to-do item can be added and completed
    When I press "Ctrl+Y"
    And I add the item "Buy milk"
    And I complete "Buy milk"
    Then "Buy milk" is shown as done

  Scenario: The to-do list keeps its items after a restart
    Given the to-do item "Buy milk" exists
    When iTerminal is restarted and I press "Ctrl+Y"
    Then "Buy milk" is listed

  Scenario: Stock browser shows quotes for watched symbols
    When I press "Alt+F7"
    Then the watched symbols are listed with their latest price

  Scenario: Adding a stock symbol by company name
    When I press "Alt+F7"
    And I press "a" and search for "Apple"
    Then the search lists each exchange where the company trades
    When I choose a result
    Then the symbol is added to the watch list
