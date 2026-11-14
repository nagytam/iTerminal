@ignore
Feature: YouTube client
  The YouTube dialog has a search to type, the videos found on the left and the details of the video under the cursor
  on the right. Enter on a video (or F3) plays it in the video viewer. F2 opens the video's page in the browser.
  Searches and stream look-ups run in the background, and closing the dialog cancels the one in flight.

  Background:
    Given iTerminal has just started

  Scenario: A search lists the videos it finds
    Given the YouTube dialog is open
    When I type "terminal file manager" in the search and press "Enter"
    Then the videos found for "terminal file manager" are listed on the left

  Scenario: The details of the video under the cursor are shown on the right
    Given the YouTube dialog shows the results of a search
    When I move the cursor to the second video
    Then the details of the second video are shown on the right

  Scenario: F3 on a video plays it in the video viewer
    Given the YouTube dialog shows the results of a search
    When I move the cursor to the first video and press "F3"
    Then the video viewer plays the first video

  Scenario: F2 opens the video's page in the browser
    Given the YouTube dialog shows the results of a search
    When I move the cursor to the first video and press "F2"
    Then the browser opens the page of the first video

  Scenario: A search that finds nothing says so
    Given the YouTube dialog is open
    When I type "zzzz no such video" in the search and press "Enter"
    Then the dialog says that no video was found

  Scenario: Closing the dialog cancels a search that is still running
    Given the YouTube dialog is searching
    When I press "Esc"
    Then the search is cancelled
    And its results never appear
