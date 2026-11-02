@ignore
Feature: Processes, tasks and system information
  Alt+F12 lists processes, F9 shows the task queue and Ctrl+F11 shows system information.

  Background:
    Given iTerminal has just started

  Scenario: Alt+F12 lists the running processes
    When I press "Alt+F12"
    Then the process list shows each process with its name and memory

  Scenario: Sorting the process list by memory
    Given the process list is open
    When I choose "Sort by memory"
    Then the processes are sorted by memory

  Scenario: Ending a process asks first
    Given the process list shows "notepad"
    When I choose "End process" on "notepad"
    Then the dialog asks whether to end "notepad"
    When I press "n"
    Then "notepad" keeps running

  Scenario: F9 shows the task queue of background work
    Given a copy is running
    When I press "F9"
    Then the task queue lists the copy with its progress

  Scenario: P pauses the selected task
    Given the task queue lists a queued copy
    When I press "P"
    Then the copy does not start until it is resumed

  Scenario: Del cancels a task after confirmation
    Given the task queue lists a running copy
    When I press "Del" and then "y"
    Then the copy is cancelled

  Scenario: Ctrl+Up moves a queued task earlier
    Given two queued tasks are listed
    When I press "Ctrl+Up" on the second task
    Then it is listed first

  Scenario: Ctrl+F11 shows the system information
    When I press "Ctrl+F11"
    Then the dialog shows the operating system, processor and memory

  Scenario: The process list does not block the screen while it loads
    When I open the process list on a busy machine
    Then the screen keeps responding while the list loads
