@ignore
Feature: Package manager
  Alt+F8 lists and manages system packages: WinGet on Windows, apt on Linux.

  Background:
    Given iTerminal has just started

  Scenario: Alt+F8 lists installed packages
    When I press "Alt+F8"
    Then the installed packages are listed with their versions

  Scenario: Searching for a package
    When I press "Alt+F8" and search for "htop"
    Then "htop" is listed with its description

  Scenario: Installing a package asks first
    When I choose to install "htop"
    Then the dialog asks for confirmation before anything is installed

  Scenario: Installing runs in the background with the output kept
    Given the user confirmed the install of "htop"
    Then the install runs in the background and its output is kept

  Scenario: A package manager that is missing is reported
    Given apt is not installed
    When I press "Alt+F8"
    Then the dialog says the package manager is not available
