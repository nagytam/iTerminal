@ignore
Feature: Viewing documents and e-books with F3
  F3 shows the text of Office, OpenDocument, PDF, EPUB, Kindle, FictionBook, e-mail and XPS files without the program that made them.

  Background:
    Given iTerminal has just started

  Scenario: F3 shows the text of a Word document
    Given "report.docx" is under the cursor
    When I press "F3"
    Then the viewer shows the text of "report.docx"

  Scenario: F3 shows the sheets of a spreadsheet
    Given "figures.xlsx" is under the cursor
    When I press "F3"
    Then each sheet's cells are shown as text

  Scenario: F3 shows the text of a PDF
    Given "manual.pdf" is under the cursor
    When I press "F3"
    Then the text of "manual.pdf" is shown

  Scenario: F3 shows an e-book chapter by chapter
    Given "novel.epub" is under the cursor
    When I press "F3"
    Then the chapters of the e-book are shown in reading order

  Scenario: F3 shows an e-mail message
    Given "message.eml" is under the cursor
    When I press "F3"
    Then the headers and body of the message are shown with the subject decoded

  Scenario: F3 shows an Outlook message
    Given "message.msg" is under the cursor
    When I press "F3"
    Then the sender, subject and body of the message are shown

  Scenario: F3 on an old Word file reads the text
    Given "letter.doc" is under the cursor
    When I press "F3"
    Then the text of "letter.doc" is shown

  Scenario: A corrupt document is shown as an error in the viewer
    Given "broken.docx" is not a valid document
    When I press "F3"
    Then the viewer says the document could not be read
