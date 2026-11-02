@ignore
Feature: Browsing an iPhone
  Alt+F11 opens the iPhone browser. An iPhone's photos and files are listed as a panel over the phone's file access protocol (AFC).

  Background:
    Given iTerminal has just started

  Scenario: Alt+F11 lists the connected iPhone
    Given an iPhone is connected and trusted
    When I press "Alt+F11"
    Then the iPhone is listed as "iphone://"

  Scenario: The first connection without trust explains what to do
    Given an iPhone is connected but not trusted
    When I press "Alt+F11"
    Then the message asks the user to trust the computer on the phone

  Scenario: Copying a photo from the iPhone to a local folder
    Given the left panel shows "iphone://" with a photo "IMG_0001.jpg"
    When I copy "IMG_0001.jpg" to the right panel
    Then "IMG_0001.jpg" is in the local folder
