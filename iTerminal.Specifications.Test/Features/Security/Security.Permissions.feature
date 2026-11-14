@ignore
Feature: Permissions and access control
  The permissions dialog shows who may do what with an entry, and changes it on Windows (NTFS ACL) and Unix (POSIX ACL and mode bits).

  Background:
    Given iTerminal has just started

  Scenario: The permissions dialog lists the entries of the access list
    Given the permissions dialog is open for "readme.txt"
    Then the dialog lists each user or group with its rights

  Scenario: Set a principal's rights replaces its explicit entry
    Given "alice" has "Read" on "readme.txt"
    When I set "alice" to "Modify"
    Then "alice" has "Modify" on "readme.txt"
    And "alice" has no second entry

  Scenario: Removing an entry is offered only for explicit entries
    Given the permissions dialog shows an inherited entry
    Then the inherited entry cannot be removed

  Scenario: Turning inheritance off keeps the inherited rights as explicit copies
    Given "readme.txt" inherits its rights from its folder
    When I press "I" and turn inheritance off
    Then the inherited rights are kept as explicit entries

  Scenario: Changing the owner needs the right to take ownership
    When I press "O" to change the owner
    And I choose a new owner
    Then the owner changes or the dialog says the right is missing

  Scenario: On Unix the group, the owner and the mask can be changed but not removed
    Given the permissions dialog is open on a Unix system
    Then the owner, group, mask and other entries cannot be removed

  Scenario: A name that could become a second entry is refused
    Given the permissions dialog is open on a Unix system
    When I add the entry name "bob;other"
    Then the name is refused

  Scenario: Ctrl+Shift+A asks for elevation when the rights are missing
    Given the current account may not change "readme.txt"
    When I press "Ctrl+Shift+A"
    Then iTerminal offers to restart with administrator rights
