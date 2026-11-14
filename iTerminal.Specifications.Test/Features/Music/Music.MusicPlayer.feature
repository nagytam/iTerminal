@ignore
Feature: Music player, library and playlists
  Ctrl+F7 opens the music player as two panels: a library catalog and a playing queue. Space plays and pauses.

  Background:
    Given iTerminal has just started
    And the music library holds "song1.mp3" and "song2.flac"

  Scenario: Ctrl+F7 opens the music player panels
    When I press "Ctrl+F7"
    Then the library is shown in one panel and the queue in the other

  Scenario: Enter on a track plays it
    Given the library panel lists "song1.mp3"
    When I press "Enter" on "song1.mp3"
    Then "song1.mp3" starts playing

  Scenario: Space pauses and resumes playback
    Given "song1.mp3" is playing
    When I press "Space"
    Then playback is paused
    When I press "Space"
    Then playback resumes

  Scenario: Insert marks tracks to add to a playlist
    When I press "Insert" on "song1.mp3"
    And I press "Insert" on "song2.flac"
    And I press "F5"
    Then both tracks are added to the queue

  Scenario: Delete removes a track from the queue
    Given the queue holds "song1.mp3"
    When I press "Delete" on "song1.mp3" in the queue
    Then "song1.mp3" is no longer in the queue

  Scenario: The queue is kept after a restart
    Given the queue holds "song1.mp3" and "song2.flac"
    When iTerminal is restarted
    Then the queue still holds both tracks
    And the track that was cued is selected but not playing

  Scenario: Plus and minus change the volume
    When I press "+"
    Then the volume goes up
    When I press "-"
    Then the volume goes down

  Scenario: The library search filters the tracks
    When I search the library for "song2"
    Then only "song2.flac" is listed

  Scenario: A library scan shows its progress in two stages
    When I rescan the music folder
    Then the scan shows folder discovery first and then indexing with a count

  Scenario: Results appear while a scan is still running
    Given a scan of a large music folder is running
    Then the library lists the tracks found so far

  Scenario: Playlists load and save as M3U8
    Given the queue holds "song1.mp3"
    When I save the queue as a playlist "mix.m3u8"
    Then "mix.m3u8" lists "song1.mp3"

  Scenario: Ctrl+Shift+M switches between the music drives and the file panels
    When I press "Ctrl+Shift+M"
    Then the file panels are shown again

  Scenario: Playing a file from the file panel
    Given "song1.mp3" is under the cursor in a file panel
    When I press "Enter"
    Then the file plays in the music player

  Scenario: Music output uses the device chosen in settings
    Given an audio output device "Speakers" is selected in the settings
    When a track plays
    Then the track is sent to "Speakers"
