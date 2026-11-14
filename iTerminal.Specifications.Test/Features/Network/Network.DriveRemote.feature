@ignore
Feature: Browsing remote drives
  Ctrl+F1 and the Path page open remote drives: ssh://, smb://, ftp://, dav://, mtp:// phones and cloud drives. Each is a panel the same way a local folder is.

  Background:
    Given iTerminal has just started

  Scenario: The Path page opens an ssh address in the focused panel
    When I press "Ctrl+F1"
    And I type "ssh://user@host/home/user" on the Path page
    And I press "Enter"
    Then the focused panel shows "ssh://user@host/home/user"

  Scenario: An SMB share opens as a panel
    When I press "Ctrl+F1" and type "smb://server/share"
    Then the focused panel lists the share's folders

  Scenario: A password typed into an address is not stored
    When I type "ssh://user:secret@host/" on the Path page
    Then the password is not saved in the address history

  Scenario: Copying from a remote drive to a local folder
    Given the left panel shows "ssh://user@host/home/user"
    And the right panel shows a local folder
    When I copy "notes.txt" from the left panel to the right panel
    Then "notes.txt" is in the local folder

  Scenario: An unknown SSH host key is checked before the connection is trusted
    When I connect to a host whose key is not known
    Then a prompt shows the host's key fingerprint before anything is trusted

  Scenario: A changed SSH host key is refused
    Given the saved key for "host" differs from the key the server now shows
    When I connect to "host"
    Then the connection is refused with a warning about the changed key

  Scenario: An FTP folder opens as a panel
    When I press "Ctrl+F1" and type "ftp://user@ftp.example.test/"
    Then the focused panel lists the FTP server's root folder

  Scenario: A WebDAV address opens as a panel
    When I press "Ctrl+F1" and type "davs://host/remote.php/dav/files/user/"
    Then the focused panel lists the WebDAV folder

  Scenario: A connected phone is listed as a drive
    Given an Android phone is connected over MTP
    When I open the drive list
    Then the phone is listed as "mtp://"

  Scenario: A remote connection that cannot be reached shows a message
    When I press "Ctrl+F1" and type "ssh://user@unreachable.example.test/"
    Then the panel stays where it was and a message says the host cannot be reached
