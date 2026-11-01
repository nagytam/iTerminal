@ignore
Feature: Performance benchmark
  Ctrl+Shift+B measures CPU, memory, disk, network and timer latency and gives an overall score. It only reads: it creates no files.

  Background:
    Given iTerminal has just started

  Scenario: Ctrl+Shift+B runs the benchmark and shows the scores
    When I press "Ctrl+Shift+B"
    Then the dialog shows a score for CPU, memory, disk, network and timer latency
    And an overall score is shown when the run ends

  Scenario: The screen keeps responding during the run
    When the benchmark is running
    Then the dialog redraws while the probes run

  Scenario: Escape stops the run and keeps the scores so far
    Given the benchmark is running
    When I press "Esc"
    Then the run stops and the scores measured so far are shown

  Scenario: The disk probe never writes a file
    When the benchmark runs
    Then no file is created, renamed or changed by the disk probe

  Scenario: A probe that fails does not stop the others
    Given the network probe cannot reach its server
    When the benchmark runs
    Then the network score is shown as unavailable and the other scores are shown
