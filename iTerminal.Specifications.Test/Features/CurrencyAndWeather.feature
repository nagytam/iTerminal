@ignore
Feature: Currency converter and weather
  Ctrl+Shift+F5 converts currencies with the European Central Bank's daily rates, kept for offline use. The menu bar shows the weather.

  Background:
    Given iTerminal has just started

  Scenario: The currency converter converts between two currencies
    When I press "Ctrl+Shift+F5"
    And I convert 100 "USD" to "EUR"
    Then the result shows an amount in euros computed from the daily rate

  Scenario: The last downloaded rates work offline
    Given the rates were downloaded yesterday
    And the network is unavailable
    When I press "Ctrl+Shift+F5"
    Then the converter shows the saved rates with their date

  Scenario: Rates are downloaded when the converter opens and the network is available
    Given the rates were not downloaded in this run
    When I press "Ctrl+Shift+F5"
    Then the rates are downloaded in the background
    And the screen keeps responding while they download

  Scenario: Only the newest download is applied
    Given two rate downloads were started one after the other
    When the first one finishes last
    Then the converter shows the rates of the second download

  Scenario: The menu bar shows the current weather
    Given the weather service is available
    Then the menu bar shows the current temperature and conditions

  Scenario: The weather indicator says when it cannot reach the service
    Given the weather service is not available
    Then the menu bar shows that the weather is unavailable
