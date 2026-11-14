@ignore
Feature: Streams and attributes of a file
  The Streams and attributes dialog lists the NTFS alternate data streams (Windows) or the extended attributes
  (Linux, macOS) of the file or folder under the cursor, with the name and size of each. A text or hex preview
  shows the stream under the dialog's cursor, and a stream can be deleted.

  Background:
    Given iTerminal has just started

  Scenario: The dialog lists each stream or attribute with its size
    Given "photo.jpg" has an extra stream "Zone.Identifier" of 26 bytes
    When I move the cursor to "photo.jpg" in the left panel
    And I open "Streams and attributes" from the context menu
    Then the dialog lists "Zone.Identifier" with 26 bytes

  Scenario: A text preview shows the stream under the dialog's cursor
    Given the streams dialog is open for "photo.jpg"
    When I move the cursor to "Zone.Identifier" in the dialog
    Then the preview shows the text of "Zone.Identifier"

  Scenario: A hex preview shows the bytes of a binary stream
    Given the streams dialog is open for "photo.jpg"
    When I press "F4" on "Zone.Identifier"
    Then the preview shows the bytes of "Zone.Identifier" in hex

  Scenario: Deleting a stream removes it from the file
    Given the streams dialog is open for "photo.jpg"
    When I press "Delete" on "Zone.Identifier" and confirm
    Then "photo.jpg" no longer has a stream named "Zone.Identifier"

  Scenario: A file without any stream or attribute says so
    Given "plain.txt" has no extra streams or attributes
    When I move the cursor to "plain.txt" in the left panel
    And I open "Streams and attributes" from the context menu
    Then the dialog says that there are no streams or attributes
