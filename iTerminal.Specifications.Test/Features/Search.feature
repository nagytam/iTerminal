@ignore
Feature: Finding files
  F7 searches by name, size, age and content. Results appear as a panel the user can act on.

  Background:
    Given iTerminal has just started

  Scenario: F7 finds files by name
    When I press "F7"
    And I type "*.txt" in the name box and press "Enter"
    Then the results list every ".txt" file under the folder

  Scenario: Several patterns separated by semicolons
    When I press "F7"
    And I type "*.jpg;*.png" and press "Enter"
    Then the results list the ".jpg" and ".png" files

  Scenario: A content search finds the files that contain the text
    When I press "F7"
    And I type "TODO" in the contains box and press "Enter"
    Then the results list only the files that contain "TODO"

  Scenario: The results are a panel the user can act on
    Given a search has listed "alpha.txt" and "beta.txt"
    When I select "alpha.txt" in the results and press "F5"
    Then "alpha.txt" is copied

  Scenario: Find Large Files opens the search with a size already set
    When I open the File menu
    And I choose "Find Large Files"
    Then the search has "100 MB" as the size limit

  Scenario: Find Old Files opens the search with an age already set
    When I open the File menu
    And I choose "Find Old Files"
    Then the search has "365 days" as the age limit

  Scenario: A search can be cancelled
    Given a search of a large folder is running
    When I press "Esc"
    Then the search stops and keeps the results found so far

  Scenario: Search results are a search:// panel
    Given a search has run
    Then the panel's address starts with "search://"

  Scenario: Alt with a letter types to search the panel
    Given the left panel lists "alpha", "beta" and "readme.txt"
    When I press "Alt+R"
    Then the cursor moves to "readme.txt"
