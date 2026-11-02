@ignore
Feature: Runtime dependencies downloaded on demand
  Tools > Runtime Dependencies shows the helpers iTerminal can install: Playwright, Ollama and git. They are not shipped with the application.

  Background:
    Given iTerminal has just started

  Scenario: The dependencies page lists each helper with its state
    When I open the Tools menu and choose "Runtime Dependencies"
    Then Playwright, Ollama and git are listed with "installed" or "not installed"

  Scenario: Installing a missing helper runs in the background
    Given git is not installed
    When I choose to install "git"
    Then the install runs in the background with its progress shown

  Scenario: A helper that is already installed is not downloaded again
    Given Ollama is already running at its configured address
    When I open the dependencies page
    Then Ollama is shown as available and nothing is downloaded
