@ignore
Feature: Settings, themes and the language
  Shift+F8 opens the settings. Alt+F6 opens the theme manager. Ctrl+Shift+F3 changes the language at run time.

  Background:
    Given iTerminal has just started

  Scenario: Shift+F8 opens the settings dialog with its pages
    When I press "Shift+F8"
    Then the settings dialog shows its pages in a vertical tab list

  Scenario: A change in the settings is kept without pressing save
    When I press "Shift+F8" and change a setting
    Then the setting is kept when the dialog is closed and reopened

  Scenario: The theme manager applies a theme at once
    When I press "Alt+F6"
    And I choose the theme "Dark"
    Then the screen is drawn in the "Dark" theme

  Scenario: Themes persist after a restart
    Given the theme "Dark" is applied
    When iTerminal is restarted
    Then the screen is drawn in the "Dark" theme

  Scenario: Changing the language redraws menus and the footer
    When I press "Ctrl+Shift+F3"
    And I choose "Deutsch"
    Then the menus and the footer are shown in German

  Scenario: The language choice is kept after a restart
    Given the language is "Magyar"
    When iTerminal is restarted
    Then the menus are shown in Hungarian

  Scenario: A string missing in the chosen language falls back to English
    Given the language is "Français" and a text has no French translation
    Then that text is shown in English

  Scenario: The language list shows each language in its own name
    When I press "Ctrl+Shift+F3"
    Then "Deutsch", "Français", "Español", "Italiano" and "Magyar" are listed by their own names
