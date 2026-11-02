@ignore
Feature: Translator and encyclopedia
  Ctrl+Shift+F4 translates text and detects its language. Ctrl+Shift+W looks a term up in Wikipedia, Simple English Wikipedia and Wiktionary.

  Background:
    Given iTerminal has just started

  Scenario: The translator translates a typed text
    When I press "Ctrl+Shift+F4"
    And I type "Good morning" and choose "German"
    Then the translation "Guten Morgen" is shown

  Scenario: The translator detects the language of the text
    When I press "Ctrl+Shift+F4"
    And I type "Bonjour tout le monde"
    Then the source language is detected as "French"

  Scenario: Long text is split and joined back in order
    When I type a text of several paragraphs and translate it
    Then the translation keeps the paragraphs in their order

  Scenario: Translation runs in the background
    When I translate a text
    Then the screen keeps responding until the translation arrives

  Scenario: A missing translation service is reported
    Given no translation service is configured
    When I translate "Hello"
    Then the translator says that no translation service is available

  Scenario: The encyclopedia looks a term up in the chosen source
    When I press "Ctrl+Shift+W"
    And I type "Hungary" and choose "Wikipedia (English)"
    Then the matching articles are listed
    And the article under the cursor is shown on the right

  Scenario: Wiktionary gives the meaning of a word
    When I press "Ctrl+Shift+W"
    And I type "serendipity" and choose "Wiktionary"
    Then a dictionary entry for "serendipity" is shown

  Scenario: Britannica opens in the browser
    When I press "Ctrl+Shift+W"
    And I choose "Britannica"
    Then the term opens in the system's browser and no page is fetched by iTerminal
