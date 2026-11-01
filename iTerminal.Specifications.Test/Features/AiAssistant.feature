@ignore
Feature: The local AI assistant bar
  The AI bar sends a question to a local Ollama model and shows the answer. The model is downloaded on demand.

  Background:
    Given iTerminal has just started

  Scenario: Ctrl+F2 chooses the AI model
    When I press "Ctrl+F2"
    Then the AI model dialog lists the installed models

  Scenario: A question is answered by the local model
    Given the AI bar is shown with the model "llama" selected
    When I type "What is in this folder?" and press "Enter"
    Then the answer appears in the AI bar
    And the screen keeps responding while the answer is written

  Scenario: The model is downloaded when none is installed
    Given no Ollama model is installed
    When I choose to set up the AI model
    Then the model is downloaded with progress shown

  Scenario: A question about the folder includes the folder's listing
    Given the left panel shows "alpha", "beta" and "readme.txt"
    When I ask "How many files are here?"
    Then the question sent to the model names the folder's entries

  Scenario: Without a running model the bar says so
    Given Ollama is not running and cannot be started
    When I type a question and press "Enter"
    Then the AI bar says that no model is available

  Scenario: The command line replaces the AI bar when revealed
    When I press "Ctrl+B" until the command line is shown
    Then the AI bar is hidden and the command line is shown
