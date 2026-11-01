@ignore
Feature: Removing metadata from pictures
  Edit > Strip Metadata removes camera, GPS, XMP, IPTC and comment data from the marked JPEG and PNG files.

  Background:
    Given iTerminal has just started

  Scenario: Strip Metadata removes GPS data from a marked photo
    Given "holiday.jpg" with GPS coordinates is marked
    When I open the Edit menu and choose "Strip Metadata"
    And I press "Enter" twice
    Then "holiday.jpg" no longer holds its GPS coordinates
    And its picture and colour profile are unchanged

  Scenario: Strip Metadata asks twice before overwriting
    Given "holiday.jpg" is marked
    When I open the Edit menu and choose "Strip Metadata"
    And I press "Enter"
    Then the dialog says the marked files will be overwritten
    And no file has changed yet

  Scenario: Other file types in the selection are skipped
    Given "holiday.jpg" and "notes.txt" are marked
    When I strip the metadata
    Then "notes.txt" is not changed and the summary counts it as skipped

  Scenario: A picture with nothing to remove is not rewritten
    Given "clean.png" has no metadata
    When I strip the metadata of "clean.png"
    Then "clean.png" keeps its modification date

  Scenario: The image viewer shows the metadata of a picture
    Given the image viewer shows "holiday.jpg"
    When I press "i"
    Then the GPS position is listed

  Scenario: The metadata editor changes the description of a picture
    Given the metadata view of "holiday.jpg" is open
    When I press "e" and change the description to "Lake"
    And I save
    Then the description of "holiday.jpg" is "Lake"
