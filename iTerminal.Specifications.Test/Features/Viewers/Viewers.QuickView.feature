@ignore
Feature: Quick view of the file under the cursor in the other panel
  Ctrl+F3 turns the panel the user is not in into a preview of the file under the cursor. The preview follows the cursor
  and is closed with the same key.

  Background:
    Given iTerminal has just started

  Scenario: Ctrl+F3 previews the file under the cursor in the other panel
    When I move the cursor to "notes.md" in the left panel
    And I press "Ctrl+F3"
    Then the right panel shows a preview titled "notes.md"

  Scenario: The preview follows the cursor
    Given the right panel is a quick view of the left panel
    When I move the cursor to "readme.txt" in the left panel
    Then the right panel shows a preview titled "readme.txt"

  Scenario: Ctrl+F3 again closes the quick view
    Given the right panel is a quick view of the left panel
    When I press "Ctrl+F3"
    Then the right panel lists "right_only.txt"

  Scenario: A folder under the cursor shows how many entries it holds
    When I move the cursor to "alpha" in the left panel
    And I press "Ctrl+F3"
    Then the right panel shows the number of entries in "alpha"

  Scenario: A binary file shows its size and kind instead of its bytes
    Given "image.bin" is a binary file of 2048 bytes
    When I move the cursor to "image.bin" in the left panel
    And I press "Ctrl+F3"
    Then the right panel shows the size and kind of "image.bin"

  Scenario: A large text file shows only its beginning
    Given "huge.log" is a text file of 500 MB
    When I move the cursor to "huge.log" in the left panel
    And I press "Ctrl+F3"
    Then the right panel shows the first lines of "huge.log"
    And the right panel does not read the whole file
