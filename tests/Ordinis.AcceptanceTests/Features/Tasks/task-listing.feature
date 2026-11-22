@area-tasks
Feature: Task listing, filtering and sorting
  As a project participant
  I want to find tasks by board, assignee, status, priority and due date
  So that I can focus on the work that matters now

  Task lists are available across all visible projects, per project and per
  board. (Per-user lists are specified in Users/user-tasks.feature; paging and
  field selection in Platform/pagination.feature and sparse-fieldsets.feature.)

  Background:
    Given an organization "Acme" whose org Admin is Olivia
    And an organization "Globex" with a user Mallory
    And a project "Apollo" in "Acme" with boards "Sprint 1" and "Sprint 2"
    And a project "Hermes" in "Acme" with a board "Backlog"
    And Alice is an Admin of project "Apollo"
    And Bob is a Member of project "Apollo"
    And Victor is a Viewer of project "Hermes"
    And these tasks exist:
      | title          | board    | status     | priority | assignee | due in  |
      | Fix login bug  | Sprint 1 | InProgress | Critical | Bob      | 2 days  |
      | Add dark mode  | Sprint 1 | Backlog    | Low      |          |         |
      | Write docs     | Sprint 2 | ToDo       | Medium   | Alice    | 10 days |
      | Release 1.0    | Sprint 2 | InReview   | High     | Bob      | 5 days  |
      | Plan Q3        | Backlog  | Backlog    | Medium   | Victor   | 30 days |

  Rule: Lists only contain tasks of projects the caller can see

    @smoke
    Scenario: A project member sees the tasks of their projects only
      When Bob lists all tasks
      Then the listed tasks are "Fix login bug, Add dark mode, Write docs, Release 1.0"
      And the total count is 4

    Scenario: The org Admin sees the tasks of every project in the organization
      When Olivia lists all tasks
      Then the total count is 5

    @multi-tenant
    Scenario: A user of another organization sees none of them
      When Mallory lists all tasks
      Then no tasks are listed
      And the total count is 0

    Scenario: Deleted tasks are not listed
      Given Alice has deleted the task "Add dark mode"
      When Bob lists all tasks
      Then the task "Add dark mode" is not among the listed tasks

  Rule: Lists can be filtered, and filters combine

    Scenario Outline: Filtering all tasks by <filter>
      When Bob lists all tasks filtered by <filter>
      Then the listed tasks are "<tasks>"

      Examples:
        | filter                   | tasks                       |
        | board "Sprint 2"         | Write docs, Release 1.0     |
        | assignee Bob             | Fix login bug, Release 1.0  |
        | status Backlog           | Add dark mode               |
        | priority Medium          | Write docs                  |
        | due before 6 days        | Fix login bug, Release 1.0  |
        | due after 6 days         | Write docs                  |

    Scenario: Combining filters narrows the result
      When Bob lists all tasks filtered by assignee Bob and priority High
      Then the listed tasks are "Release 1.0"

    Scenario: A filter that matches nothing returns an empty list
      When Bob lists all tasks filtered by status Cancelled
      Then no tasks are listed
      And the total count is 0

    Scenario: A filter value that is not a valid status is rejected
      When Bob lists all tasks filtered by status "Someday"
      Then the request is rejected as malformed

  Rule: Lists can be sorted, ascending by creation date by default

    Scenario: Default order is oldest first
      When Bob lists all tasks
      Then the tasks are listed in creation order

    Scenario Outline: Sorting by <field>
      When Bob lists all tasks sorted by <field> <direction>
      Then the listed tasks are "<tasks>"

      Examples:
        | field    | direction  | tasks                                                 |
        | title    | ascending  | Add dark mode, Fix login bug, Release 1.0, Write docs |
        | title    | descending | Write docs, Release 1.0, Fix login bug, Add dark mode |

      # GAP-14 (fixed): Status and Priority are persisted as strings, so these
      # must sort by workflow position / severity, not alphabetically.
      Examples: Sorting by workflow position and severity
        | field    | direction  | tasks                                                 |
        | priority | descending | Fix login bug, Release 1.0, Write docs, Add dark mode |
        | priority | ascending  | Add dark mode, Write docs, Release 1.0, Fix login bug |
        | status   | ascending  | Add dark mode, Write docs, Fix login bug, Release 1.0 |

    Scenario: Sorting by due date
      When Bob lists all tasks sorted by dueDate ascending
      Then the tasks with a due date are listed in the order "Fix login bug, Release 1.0, Write docs"

    Scenario: Sorting by last update
      Given Bob has renamed the task "Add dark mode" to "Add dark theme"
      When Bob lists all tasks sorted by updatedAt descending
      Then the first listed task is "Add dark theme"

    Scenario: An unknown sort field falls back to creation order
      When Bob lists all tasks sorted by colour ascending
      Then the request succeeds
      And the tasks are listed in creation order

    Scenario: Tasks with equal sort values keep a stable order across requests
      Given 30 tasks on board "Sprint 1" with priority Medium
      When Bob lists all tasks sorted by priority ascending, page 1 and page 2 of size 20, twice
      Then both reads return the same tasks on the same pages

  Rule: List entries summarise each task

    Scenario: A list entry shows the assignee name and activity counts
      Given Bob has commented "On it" on the task "Fix login bug"
      And Bob has uploaded "crash.log" to the task "Fix login bug"
      When Bob lists all tasks
      Then the entry for "Fix login bug" shows:
        | status            | InProgress |
        | priority          | Critical   |
        | assignee name     | Bob        |
        | comment count     | 1          |
        | attachment count  | 1          |

  Rule: Tasks can be listed per project

    Scenario: Listing the tasks of a project
      When Victor lists the tasks of project "Hermes"
      Then the listed tasks are "Plan Q3"

    Scenario Outline: Filtering project tasks by <filter>
      When Bob lists the tasks of project "Apollo" filtered by <filter>
      Then the listed tasks are "<tasks>"

      Examples:
        | filter           | tasks                      |
        | board "Sprint 1" | Fix login bug, Add dark mode |
        | assignee Bob     | Fix login bug, Release 1.0 |
        | status ToDo      | Write docs                 |
        | priority Low     | Add dark mode              |

    # GAP-14 (fixed): priority sorts by severity, not alphabetically.
    Scenario: Project tasks sorted by priority
      When Bob lists the tasks of project "Apollo" sorted by priority descending
      Then the listed tasks are "Fix login bug, Release 1.0, Write docs, Add dark mode"

    Scenario Outline: <persona> listing the tasks of project "Apollo"
      When <persona> lists the tasks of project "Apollo"
      Then <outcome>

      Examples:
        | persona | outcome                              |
        | Bob     | the request succeeds                 |
        | Olivia  | the request succeeds                 |
        | Victor  | the request is rejected as not found |
        | Mallory | the request is rejected as not found |

  Rule: Tasks can be listed per board

    Scenario: Listing the tasks of a board
      When Bob lists the tasks of board "Sprint 2"
      Then the listed tasks are "Write docs, Release 1.0"

    Scenario Outline: Filtering board tasks by <filter>
      When Bob lists the tasks of board "Sprint 1" filtered by <filter>
      Then the listed tasks are "<tasks>"

      Examples:
        | filter            | tasks         |
        | status InProgress | Fix login bug |
        | priority Low      | Add dark mode |
        | assignee Bob      | Fix login bug |
        | due before 3 days | Fix login bug |

    Scenario Outline: <persona> listing the tasks of board "Sprint 1"
      When <persona> lists the tasks of board "Sprint 1"
      Then <outcome>

      Examples:
        | persona | outcome                              |
        | Bob     | the request succeeds                 |
        | Olivia  | the request succeeds                 |
        | Victor  | the request is rejected as not found |
        | Mallory | the request is rejected as not found |
