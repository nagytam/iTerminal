@ignore
Feature: The command line under the file panels
  Ctrl+B cycles the bottom bar to the command line. Panels feed it names and run what is typed in it.

  Background:
    Given iTerminal has just started

  Scenario: Ctrl+Enter copies the name under the cursor to the command line
    When I move the cursor to "alpha" in the left panel
    And I press "Ctrl+Enter"
    Then the command line holds "alpha"

  Scenario: Shift+Enter copies the marked names
    Given "alpha" and "beta" are marked
    When I press "Shift+Enter"
    Then the command line holds "alpha beta"

  Scenario: A name with a space is quoted on the command line
    Given a file named "my file.txt" is under the cursor
    When I press "Ctrl+Enter"
    Then the command line holds the name of the file in double quotes

  Scenario: Ctrl+Shift+Enter copies the folder's path
    When I press "Ctrl+Shift+Enter"
    Then the command line holds the path of the left panel's folder

  Scenario: Enter runs the command line when it holds text
    Given the command line holds "echo hello"
    When I press "Enter" in the left panel
    Then the command "echo hello" runs in the command log
    And the panel does not open "alpha"

  Scenario: Enter opens the entry when the command line is empty
    Given the command line is empty
    When I move the cursor to "alpha" and press "Enter"
    Then the left panel lists "inside_alpha.txt"

  Scenario: Alt+P and Alt+N walk the command history
    Given the commands "ls" and "pwd" were run
    When I press "Alt+P"
    Then the command line holds "pwd"
    When I press "Alt+P"
    Then the command line holds "ls"
    When I press "Alt+N"
    Then the command line holds "pwd"

  Scenario: The half-typed line returns after the newest command
    Given the command line holds "ech"
    And the command "pwd" was run
    When I press "Alt+P"
    And I press "Alt+N"
    Then the command line holds "ech"

  Scenario: cd changes the panel's folder
    Given the command line holds "cd alpha"
    When I press "Enter" in the left panel
    Then the left panel lists "inside_alpha.txt"

  Scenario: cd to a missing folder reports an error and changes nothing
    Given the command line holds "cd nowhere"
    When I press "Enter" in the left panel
    Then an error line says that "nowhere" does not exist
    And the left panel still lists "alpha"

  Scenario: cd with a shell operator goes to the shell
    Given the command line holds "cd alpha && ls"
    When I press "Enter" in the left panel
    Then the command runs in the shell and not as a panel change

  Scenario: Enter opens the entry while a command is running
    Given a command is already running
    When I move the cursor to "alpha" and press "Enter" in the left panel
    Then the new command is not started
    And the left panel lists "inside_alpha.txt"
