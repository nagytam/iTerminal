@ignore
Feature: Web browser, mail and map
  Ctrl+S opens a text-mode web browser, Ctrl+W a mail client and Alt+F10 a map.

  Background:
    Given iTerminal has just started

  Scenario: The web browser opens a typed address
    When I press "Ctrl+S"
    And I type "https://example.test" in the address bar and press "Enter"
    Then the page is shown as text with its links

  Scenario: Links are followed with Enter
    Given the browser shows a page with a link "More"
    When I move to "More" and press "Enter"
    Then the linked page is shown

  Scenario: Back and forward move through the history
    Given the browser has visited two pages
    When I choose "Back"
    Then the first page is shown
    When I choose "Forward"
    Then the second page is shown

  Scenario: Several tabs can be open at once
    When I open a new tab in the browser
    Then two tabs are listed

  Scenario: A bookmark is added and listed
    Given the browser shows "https://example.test"
    When I add a bookmark
    Then the bookmark is listed in the bookmarks

  Scenario: Find in page highlights the matches
    Given the browser shows a long page
    When I search the page for "license"
    Then the matches are highlighted

  Scenario: Forms are filled and submitted
    Given the browser shows a form with a "name" field
    When I type "Ada" in "name" and submit the form
    Then the form is sent with "name" set to "Ada"

  Scenario: Page source is shown with F9
    Given the browser shows a page
    When I press "F9"
    Then the HTML source of the page is shown

  Scenario: Saving a page with F7
    Given the browser shows a page
    When I press "F7"
    Then the page is saved to a file

  Scenario: Scripts are not run
    Given the browser shows a page with a script that writes text
    Then the text written by the script does not appear

  Scenario: Ctrl+W opens the mail client
    When I press "Ctrl+W"
    Then the mail client is shown

  Scenario: Alt+F10 opens the map
    When I press "Alt+F10"
    Then the map is shown with a cursor position
