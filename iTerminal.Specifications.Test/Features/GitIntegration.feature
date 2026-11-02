@ignore
Feature: Git inside the file manager
  Ctrl+F6 browses the git history of a repository. Ctrl+G opens the Git client. The panel shows the branch and its sync state.

  Background:
    Given iTerminal has just started
    And the left panel is inside a git repository on branch "main"

  Scenario: Ctrl+F6 opens the history of the repository
    When I press "Ctrl+F6"
    Then the focused panel lists the commits of the repository

  Scenario: A commit opens as a folder of the tree at that revision
    Given the history lists commit "a1b2c3d"
    When I press "Enter" on "a1b2c3d"
    Then the panel shows the files at that commit
    And nothing in the working tree is changed

  Scenario: The panel header shows the branch and its sync state
    Given the branch "main" is one commit behind its upstream and two commits ahead
    Then the panel header shows "{Git 1/2}"

  Scenario: Git pull --rebase runs from the context menu
    When I open the context menu on a file in the repository
    And I choose "Git pull --rebase"
    Then git pull --rebase runs in the repository root
    And a notification shows the outcome

  Scenario: A failed rebase says so and leaves the rebase open for the Git client
    Given a rebase would conflict
    When I choose "Git pull --rebase"
    Then a notification says the rebase stopped on a conflict

  Scenario: Git sync pulls and then pushes only when the pull succeeds
    When I choose "Git sync"
    Then the pull runs first
    And the push runs only if the pull succeeded

  Scenario: Git sync warns when the branch has no upstream
    Given the branch "feature" has no upstream
    When I choose "Git sync"
    Then a warning says there is no upstream and nothing runs

  Scenario: Ctrl+G opens the Git client
    When I press "Ctrl+G"
    Then the Git client is shown for the repository

  Scenario: The Git client stages a file and commits it
    Given the Git client shows a modified file "readme.txt"
    When I stage "readme.txt" and commit with the message "Update readme"
    Then the commit "Update readme" is on the branch

  Scenario: Git commands run in the background and do not freeze the screen
    Given the Git client is open
    When I start a fetch
    Then the screen keeps responding while the fetch runs

  Scenario: A failed git command shows its full output
    Given a git command fails
    When I open the Git client
    Then the detail pane shows the whole output of the failed command
