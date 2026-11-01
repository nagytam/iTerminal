@ignore
Feature: BitTorrent manager
  Ctrl+Shift+F1 adds torrents from a magnet link or a .torrent file, and lets the user pause, resume and remove them.

  Background:
    Given iTerminal has just started

  Scenario: Ctrl+Shift+F1 opens the torrent manager
    When I press "Ctrl+Shift+F1"
    Then the torrent manager lists the torrents it knows

  Scenario: A magnet link is added
    When I press "Ctrl+Shift+F1" and add the magnet link "magnet:?xt=urn:btih:example"
    Then the torrent is listed

  Scenario: A .torrent file is added and copied beside the database
    When I add the file "ubuntu.torrent"
    Then the file is copied to the torrents folder of the data directory

  Scenario: Pause and resume a torrent
    Given a torrent is downloading
    When I pause it
    Then it stops transferring
    When I resume it
    Then it downloads again

  Scenario: Remove a torrent
    Given a torrent is listed
    When I remove it
    Then it is no longer listed

  Scenario: Torrents survive a restart
    Given a torrent is listed
    When iTerminal is restarted
    Then the torrent is listed again
