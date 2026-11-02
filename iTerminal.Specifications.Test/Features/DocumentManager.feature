@ignore
Feature: Document manager and full-text search
  Alt+F3 opens the document manager, which keeps an index of document text and searches it with full-text search.

  Background:
    Given iTerminal has just started

  Scenario: Alt+F3 opens the document manager
    When I press "Alt+F3"
    Then the document manager is shown with a search box

  Scenario: A search finds documents by the words they contain
    Given the index holds "invoice-2026.pdf" containing "consulting services"
    When I search for "consulting"
    Then "invoice-2026.pdf" is listed in the results

  Scenario: A search reads Office and OpenDocument files
    Given the index holds "report.docx" containing "quarterly review"
    When I search for "quarterly review"
    Then "report.docx" is listed

  Scenario: An image is found by its metadata
    Given the index holds "holiday.jpg" with the description "lake"
    When I search for "lake"
    Then "holiday.jpg" is listed

  Scenario: A scanned PDF is marked as having no text
    Given "scan.pdf" is a scanned image without text
    When the index reads it
    Then "scan.pdf" is listed with no text extracted

  Scenario: A rescan reads only what changed
    Given the index holds "notes.txt" with the same size and date
    When I rescan the folder
    Then "notes.txt" is not read again

  Scenario: Enter on a result opens the file in the viewer
    Given a search lists "invoice-2026.pdf"
    When I press "Enter" on "invoice-2026.pdf"
    Then the file opens in the viewer
