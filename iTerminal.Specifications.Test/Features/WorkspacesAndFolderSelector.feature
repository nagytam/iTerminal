@ignore
Feature: Workspaces and the folder selector
  Ctrl+Shift+F2 saves named screen layouts (workspaces). Ctrl+F1 chooses a folder or drive for the focused panel.

  Background:
    Given iTerminal has just started

  Scenario: Save the current panels as a named workspace
    Given the left panel shows "alpha" and the right panel shows "target"
    When I press "Ctrl+Shift+F2" and save a workspace named "Project"
    Then the workspace "Project" is listed

  Scenario: Switching to a workspace restores its folders
    Given the workspace "Project" shows "target" on the right
    When I choose "Project" in the workspace manager
    Then the right panel shows "target"

  Scenario: A workspace whose folder is gone still opens
    Given the workspace "Old" shows a folder that no longer exists
    When I switch to "Old"
    Then the panel keeps its place and a message says the folder is missing

  Scenario: The number keys switch workspaces
    Given two workspaces are saved
    When I press "2" in the workspace manager
    Then the second workspace is shown

  Scenario: Ctrl+F1 opens the folder selector
    When I press "Ctrl+F1"
    Then the folder selector is shown with its pages

  Scenario: Recent folders are listed most recently used first
    Given "alpha" was opened last
    When I press "Ctrl+F1"
    Then "alpha" is first in the Recent page

  Scenario: Enter on a folder in the selector opens it in the panel
    When I press "Ctrl+F1" and choose "beta"
    And I press "Enter"
    Then the focused panel shows "beta"

  Scenario: Esc closes the folder selector without changing the panel
    When I press "Ctrl+F1"
    And I press "Esc"
    Then the panel shows the same folder as before

  Scenario: A folder is added to Favourites
    Given the cursor is on "alpha"
    When I add "alpha" to the Favourites
    Then "alpha" is listed on the Favourites page

  Scenario: The Tree page browses folders as a tree
    When I press "Ctrl+F1" and go to the Tree page
    Then the folders are shown as an expandable tree
