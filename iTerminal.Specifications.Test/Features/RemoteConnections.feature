@ignore
Feature: Saved remote connections
  Alt+F2 keeps saved SSH, PowerShell remoting, FTP and WebDAV connections. Enter opens the session on the lent terminal, or the drive in a panel.

  Background:
    Given iTerminal has just started

  Scenario: Alt+F2 lists the saved connections
    Given two connections are saved
    When I press "Alt+F2"
    Then both connections are listed

  Scenario: Adding an SSH connection
    When I press "Alt+F2" and add an SSH connection to "server.example.test" as "deploy"
    Then "deploy@server.example.test" is listed

  Scenario: Enter opens an SSH connection on the lent terminal
    Given an SSH connection "deploy@server.example.test" is saved
    When I press "Enter" on it
    Then the terminal is lent to an ssh session for that host

  Scenario: B opens the connection as a panel
    Given an SSH connection is saved
    When I press "B" on it
    Then the focused panel shows the ssh:// drive for that host

  Scenario: No password is ever stored with an SSH connection
    When I add an SSH connection with a password
    Then the connection is saved without the password
    And the session asks for the password on the terminal

  Scenario: A saved FTP connection with a stored password opens as a drive
    Given an FTP connection with a stored password is saved
    When I press "B" on it
    Then the focused panel lists the FTP folder

  Scenario: A PowerShell remoting connection opens a session
    Given a PowerShell remoting connection is saved
    When I press "Enter" on it
    Then a PowerShell remoting session is started on the lent terminal

  Scenario: A connection can be deleted after confirmation
    Given a connection is saved
    When I press "Del" on it
    Then the dialog asks whether to delete it
    When I confirm
    Then the connection is no longer listed
