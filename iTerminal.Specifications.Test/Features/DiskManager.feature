@ignore
Feature: Disks, mounted images and drive actions
  Alt+F9 lists the drives. E ejects, F formats, M mounts or unmounts a disk image and B reads BitLocker. Ctrl+Shift+F11 mounts the image under the cursor.

  Background:
    Given iTerminal has just started

  Scenario: Alt+F9 lists the drives with their space and file system
    When I press "Alt+F9"
    Then the drives are listed with their label, type, file system and free space

  Scenario: E ejects a removable drive safely
    Given a USB drive "E:" is under the cursor
    When I press "E"
    Then the drive is ejected
    And a notification says it can be removed safely

  Scenario: Ejecting a drive in use says so
    Given the drive "E:" has an open file
    When I press "E"
    Then the message says the drive is in use

  Scenario: F formats only after the drive letter is typed back
    Given a USB drive "F:" is under the cursor
    When I press "F"
    Then the format dialog asks for the drive letter to be typed
    When I type "F" and press "Enter"
    Then the drive is formatted

  Scenario: The system drive cannot be formatted
    Given the system drive is under the cursor
    When I press "F"
    Then the format is refused before the dialog opens

  Scenario: A drive that a panel is on cannot be formatted
    Given the left panel is on "F:"
    When I press "F" on "F:"
    Then the format is refused because a panel is on that drive

  Scenario: Ctrl+Shift+F11 mounts an ISO image
    Given "disc.iso" is under the cursor in the left panel
    When I press "Ctrl+Shift+F11"
    Then the mount dialog is filled with "disc.iso"
    When I confirm the mount
    Then the image appears as a drive

  Scenario: Enter on an ISO opens it as an archive instead
    Given "disc.iso" is under the cursor
    When I press "Enter"
    Then the left panel lists the contents of "disc.iso"

  Scenario: BitLocker status is read again with B
    Given the drive "C:" is under the cursor
    When I press "B"
    Then the BitLocker status of "C:" is shown

  Scenario: Eject, format and mount run in the background
    When I eject a drive
    Then the dialog says "Ejecting" until it finishes
    And the outcome is also shown as a notification
