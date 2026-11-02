@ignore
Feature: Specialised viewers chosen by F3
  F3 picks a viewer for the file type: XML, JSON, CSV, SQLite, .NET solutions, web pages, pictures, videos and music.

  Background:
    Given iTerminal has just started

  Scenario: F3 on an XML file opens a tree view
    Given "config.xml" is under the cursor
    When I press "F3"
    Then the XML viewer shows the document as a tree

  Scenario: XPath finds nodes in an XML document
    Given the XML viewer shows a document with many "item" elements
    When I press "/" and type "//item" and press "Enter"
    Then the matching nodes are listed

  Scenario: F2 validates an XML document against its DTD
    Given the XML viewer shows a document whose DOCTYPE names a DTD
    When I press "F2"
    Then the viewer reports whether the document is valid

  Scenario: F3 on a JSON file opens a tree view
    Given "settings.json" is under the cursor
    When I press "F3"
    Then the JSON viewer shows the document as a tree

  Scenario: F2 reports where a JSON document stops being valid
    Given the JSON viewer shows a file with a missing comma on line 3
    When I press "F2"
    Then the viewer reports the error on line 3

  Scenario: F4 in the JSON viewer shows the plain text
    Given the JSON viewer shows "settings.json"
    When I press "F4"
    Then the file's text is shown

  Scenario: F3 on a CSV file opens a grid
    Given "sales.csv" is under the cursor
    When I press "F3"
    Then the CSV viewer shows the rows as a grid

  Scenario: A query filters and orders the CSV rows
    Given the CSV viewer shows "sales.csv"
    When I type the query "SELECT region, amount WHERE amount > 100 ORDER BY amount LIMIT 5"
    Then at most five rows are shown with amounts above 100 in order

  Scenario: A query that does not parse keeps the rows already shown
    Given the CSV viewer shows rows
    When I type a query that cannot be parsed
    Then the rows already shown stay on screen
    And the status line says the query is not valid

  Scenario: F8 re-reads a CSV file under another code page
    Given the CSV viewer shows "windows1250.csv" with accents
    When I press "F8"
    Then the accents are shown correctly

  Scenario: F3 on a SQLite database lists its tables
    Given "app.db" is a SQLite database
    When I move the cursor to "app.db" and press "F3"
    Then the SQLite viewer lists the tables of the database

  Scenario: A SQLite query cannot change the database
    Given the SQLite viewer is open
    When I type "DELETE FROM items"
    Then the query is refused and the database is unchanged

  Scenario: A SELECT query shows its rows
    Given the SQLite viewer is open on a table with ten rows
    When I type "SELECT * FROM items"
    Then the ten rows are shown

  Scenario: A query that runs longer than ten seconds is stopped
    Given the SQLite viewer is open
    When I type a query that runs for more than ten seconds
    Then the query stops and the status line says it timed out

  Scenario: A file named .db that is not SQLite opens as text
    Given "notes.db" is a plain text file
    When I press "F3" on "notes.db"
    Then the text viewer opens "notes.db"

  Scenario: F3 on a .sln or .slnx file opens the solution viewer
    Given "App.slnx" is under the cursor
    When I press "F3"
    Then the solution viewer lists the projects of the solution

  Scenario: The solution viewer runs build, test and run
    Given the solution viewer is open on "App.slnx"
    When I choose "Build"
    Then "dotnet build" runs and its output is shown

  Scenario: F3 on an HTML file shows the page in the web viewer
    Given "index.html" is under the cursor
    When I press "F3"
    Then the web browser shows "index.html" without running scripts

  Scenario: Enter on an HTML file opens it in the external browser
    Given "index.html" is under the cursor
    When I press "Enter"
    Then the page opens in the system's default browser

  Scenario: F3 on a picture shows it as ASCII art
    Given "photo.png" is under the cursor
    When I press "F3"
    Then the picture is drawn as ASCII art

  Scenario: The image viewer shows the metadata of a picture
    Given the image viewer shows "photo.jpg"
    When I press "i"
    Then the camera, date and dimensions of the picture are shown

  Scenario: F3 on a video plays its frames in the video viewer
    Given "clip.mp4" is under the cursor and ffmpeg is installed
    When I press "F3"
    Then the video viewer opens "clip.mp4"

  Scenario: Space pauses the video
    Given the video viewer is playing "clip.mp4"
    When I press "Space"
    Then the video is paused

  Scenario: F3 on a music file opens the music viewer with its tags
    Given "song.mp3" is under the cursor
    When I press "F3"
    Then the music viewer shows the artist, title and album

  Scenario: The music viewer edits tags
    Given the music viewer shows "song.mp3"
    When I press "e" and change the title to "New title" and save
    Then the title tag of "song.mp3" is "New title"

  Scenario: F3 on a file the viewers do not know opens the text viewer
    Given "data.unknown" is under the cursor
    When I press "F3"
    Then the text viewer opens "data.unknown"
