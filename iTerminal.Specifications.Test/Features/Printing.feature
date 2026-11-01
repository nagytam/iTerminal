@ignore
Feature: Printing files
  Ctrl+P prints the file under the cursor and manages the printer queue. On Windows it uses PowerShell, elsewhere CUPS.

  Background:
    Given iTerminal has just started
    And a printer "Office" is available

  Scenario: Ctrl+P prints the file under the cursor
    Given "report.txt" is under the cursor
    When I press "Ctrl+P"
    And I choose "Office" and press "Enter"
    Then "report.txt" is sent to the printer "Office"

  Scenario: The print queue shows the jobs of the printer
    Given a job for "report.txt" is queued on "Office"
    When I press "Ctrl+P"
    Then the queue lists the job for "report.txt"

  Scenario: A job can be held and resumed
    Given a job is queued on "Office"
    When I choose "Hold" on the job
    Then the job is held
    When I choose "Resume" on the job
    Then the job resumes

  Scenario: A job can be cancelled
    Given a job is queued on "Office"
    When I choose "Cancel job" on the job and confirm
    Then the job is removed from the queue

  Scenario: A printer that does not answer is reported in words
    Given the printer service does not answer within 15 seconds
    When I press "Ctrl+P"
    Then the dialog says the printer did not respond

  Scenario: Printing is offered only for a local file
    Given "remote.txt" is on an ssh drive
    When I open the context menu on "remote.txt"
    Then "Print" is not offered
