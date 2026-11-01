@ignore
Feature: Docker containers as folders
  Ctrl+F9 browses container file systems as folders. Start, stop and restart act on the container under the cursor.

  Background:
    Given iTerminal has just started
    And Docker is running with a container "web" that is stopped

  Scenario: Ctrl+F9 lists the running and stopped containers
    When I press "Ctrl+F9"
    Then the focused panel lists "web"

  Scenario: Enter opens a container's file system
    Given the focused panel lists "web"
    When I move the cursor to "web" and press "Enter"
    Then the panel shows the files inside the container

  Scenario: Shift+F9 starts a stopped container
    When I move the cursor to "web" and press "Shift+F9"
    Then the container "web" is started

  Scenario: Shift+F9 stops a running container
    Given the container "web" is running
    When I move the cursor to "web" and press "Shift+F9"
    Then the container "web" is stopped

  Scenario: Ctrl+Shift+F9 restarts a container
    Given the container "web" is running
    When I move the cursor to "web" and press "Ctrl+Shift+F9"
    Then the container "web" is restarted

  Scenario: Without Docker the drive says so in words
    Given Docker is not installed
    When I press "Ctrl+F9"
    Then the panel says that Docker is not available

  Scenario: Copying a file out of a container
    Given the left panel shows a folder inside container "web"
    When I copy "/etc/hostname" to the right panel
    Then the file is in the right panel's folder
