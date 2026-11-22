@area-tasks
Feature: Task details and hypermedia links
  As an API client
  I want task details to tell me what I can do next
  So that I can drive the workflow without hard-coding the transition rules

  Background:
    Given an organization "Acme" whose org Admin is Olivia
    And Nora is a user in "Acme"
    And an organization "Globex" with a user Mallory
    And a project "Apollo" in "Acme" with a board "Sprint 1"
    And Bob is a Member of project "Apollo"
    And Victor is a Viewer of project "Apollo"
    And a task "Fix login bug" on board "Sprint 1"

  Rule: Task details include everything about the task

    @smoke
    Scenario: Viewing a task
      Given the task "Fix login bug" is assigned to Bob
      And Bob has commented "On it" on the task "Fix login bug"
      When Victor views the task "Fix login bug"
      Then the request succeeds
      And the task details show title, description, status, priority, assignee name, due date, creation and update times
      And the task details include its comments and attachments
      And the task details state the current version

    Scenario Outline: <persona> viewing a task
      When <persona> views the task "Fix login bug"
      Then <outcome>

      Examples:
        | persona | outcome                              |
        | Bob     | the request succeeds                 |
        | Victor  | the request succeeds                 |
        | Olivia  | the request succeeds                 |
        | Nora    | the request is rejected as not found |
        | Mallory | the request is rejected as not found |

    Scenario: Viewing an unknown task
      When Bob views an unknown task
      Then the request is rejected as not found

    Scenario: Hidden and missing tasks are indistinguishable
      When Nora views the task "Fix login bug"
      And Nora views an unknown task
      Then both responses are identical apart from the identifier

  Rule: Every task links to itself and its always-available actions

    Scenario: Standard links
      When Bob views the task "Fix login bug"
      Then the task has the links:
        | rel    | method |
        | self   | GET    |
        | assign | POST   |
        | delete | DELETE |

  Rule: Move links advertise exactly the transitions allowed from the current status

    Scenario Outline: Move links for a <status> task
      Given the task "Fix login bug" has status <status>
      When Bob views the task "Fix login bug"
      Then the task has a "move" link
      And the task's move links are "<move links>"

      Examples:
        | status     | move links                                        |
        | Backlog    | move:todo, move:cancelled                         |
        | ToDo       | move:inprogress, move:cancelled                   |
        | InProgress | move:inreview, move:todo, move:cancelled          |
        | InReview   | move:done, move:inprogress, move:cancelled        |
        | Done       | move:todo                                         |

    Scenario: A cancelled task offers no move links
      Given the task "Fix login bug" has status Cancelled
      When Bob views the task "Fix login bug"
      Then the task has no "move" link
      And the task has no move links

    Scenario: Following an advertised move link succeeds
      Given the task "Fix login bug" has status ToDo
      When Bob follows the "move:inprogress" link of the task "Fix login bug"
      Then the request succeeds
      And the task "Fix login bug" has status InProgress

  Rule: Version 2 of task details also links to the board

    Scenario: The v2 representation adds a board link
      When Bob views the task "Fix login bug" using API version 2
      Then the request succeeds
      And the task has a "board" link to board "Sprint 1"
      And the task has all the links of version 1

    Scenario: The v1 representation has no board link
      When Bob views the task "Fix login bug" using API version 1
      Then the task has no "board" link
