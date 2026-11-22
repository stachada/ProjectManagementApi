@area-tasks
Feature: Task workflow
  As a project contributor
  I want tasks to move through a controlled lifecycle
  So that the board always reflects a valid, auditable state of work

  The lifecycle is:

    Backlog ──► ToDo ──► InProgress ──► InReview ──► Done
                 ▲           │  ▲           │          │
                 └───────────┘  └───────────┘          │
                 ▲                                     │
                 └──────────────── reopen ─────────────┘

    Any non-terminal status ──► Cancelled
    Done and Cancelled are terminal: a Done task can only be reopened,
    a Cancelled task can never move again.

  Background:
    Given an organization "Acme" whose org Admin is Olivia
    And Nora is a user in "Acme"
    And an organization "Globex" with a user Mallory
    And a project "Apollo" in "Acme" with a board "Sprint 1"
    And Alice is an Admin of project "Apollo"
    And Bob is a Member of project "Apollo"
    And Victor is a Viewer of project "Apollo"
    And a task "Fix login bug" on board "Sprint 1"

  Rule: New tasks start in the backlog

    @smoke
    Scenario: A newly created task is in Backlog
      When Bob creates a task "Write release notes" on board "Sprint 1"
      Then the task "Write release notes" has status Backlog

  Rule: A task may only follow the transitions of the workflow

    @smoke
    Scenario Outline: Allowed transition from <from> to <to>
      Given the task "Fix login bug" has status <from>
      When Bob moves the task "Fix login bug" to <to>
      Then the request succeeds
      And the task "Fix login bug" has status <to>

      Examples: Forward progress
        | from       | to         |
        | Backlog    | ToDo       |
        | ToDo       | InProgress |
        | InProgress | InReview   |
        | InReview   | Done       |

      Examples: Stepping back
        | from       | to         |
        | InProgress | ToDo       |
        | InReview   | InProgress |
        | Done       | ToDo       |

      Examples: Cancelling
        | from       | to        |
        | Backlog    | Cancelled |
        | ToDo       | Cancelled |
        | InProgress | Cancelled |
        | InReview   | Cancelled |

    Scenario Outline: Forbidden transition from <from> to <to>
      Given the task "Fix login bug" has status <from>
      When Bob moves the task "Fix login bug" to <to>
      Then the request is rejected as invalid with error "task.invalid-status-transition"
      And the task "Fix login bug" still has status <from>

      Examples: Skipping steps
        | from       | to         |
        | Backlog    | InProgress |
        | Backlog    | InReview   |
        | Backlog    | Done       |
        | ToDo       | InReview   |
        | ToDo       | Done       |
        | InProgress | Done       |

      Examples: Illegal steps back
        | from     | to      |
        | ToDo     | Backlog |
        | InReview | ToDo    |
        | InReview | Backlog |

      Examples: Leaving Done other than by reopening
        | from | to         |
        | Done | Backlog    |
        | Done | InProgress |
        | Done | InReview   |
        | Done | Cancelled  |

      Examples: Leaving Cancelled
        | from      | to         |
        | Cancelled | Backlog    |
        | Cancelled | ToDo       |
        | Cancelled | InProgress |
        | Cancelled | InReview   |
        | Cancelled | Done       |

    Scenario Outline: Moving a task to the status it already has is rejected
      Given the task "Fix login bug" has status <status>
      When Bob moves the task "Fix login bug" to <status>
      Then the request is rejected as invalid with error "task.invalid-status-transition"

      Examples:
        | status     |
        | Backlog    |
        | ToDo       |
        | InProgress |
        | InReview   |
        | Done       |
        | Cancelled  |

    Scenario: Moving to an unknown status is rejected
      When Bob moves the task "Fix login bug" to an unknown status
      Then the request is rejected as invalid
      And the task "Fix login bug" still has status Backlog

  Rule: Closing a task completes a reviewed task

    Scenario: Closing a task that is in review
      Given the task "Fix login bug" has status InReview
      When Bob closes the task "Fix login bug"
      Then the request succeeds
      And the task "Fix login bug" has status Done

    Scenario Outline: A task that is not in review cannot be closed
      Given the task "Fix login bug" has status <status>
      When Bob closes the task "Fix login bug"
      Then the request is rejected as invalid with error "task.invalid-status-transition"
      And the task "Fix login bug" still has status <status>

      Examples:
        | status     |
        | Backlog    |
        | ToDo       |
        | InProgress |
        | Done       |
        | Cancelled  |

  Rule: Reopening returns a completed task to ToDo

    Scenario: Reopening a done task
      Given the task "Fix login bug" has status Done
      When Bob reopens the task "Fix login bug"
      Then the request succeeds
      And the task "Fix login bug" has status ToDo

    Scenario Outline: A task that is not done cannot be reopened
      Given the task "Fix login bug" has status <status>
      When Bob reopens the task "Fix login bug"
      Then the request is rejected as invalid with error "task.invalid-status-transition"
      And the task "Fix login bug" still has status <status>

      Examples:
        | status    |
        | Backlog   |
        | ToDo      |
        | InReview  |
        | Cancelled |

      # GAP-02: reopen is implemented as a plain move to ToDo, so it also
      # succeeds from InProgress (InProgress -> ToDo is a legal move).
      # See src/Ordinis.Api/Tasks/TasksController.cs (Reopen action).
      @gap
      Examples: Currently accepted by the API
        | status     |
        | InProgress |

  Rule: Terminal tasks are locked for editing until reopened

    Scenario Outline: A <status> task cannot be changed: <action>
      Given the task "Fix login bug" has status <status>
      And the task "Fix login bug" is assigned to Bob
      When Bob <action>
      Then the request is rejected as invalid with error "task.terminal-state"

      Examples:
        | status    | action                                          |
        | Done      | renames the task "Fix login bug" to "Login fix" |
        | Done      | assigns the task "Fix login bug" to Alice       |
        | Done      | unassigns the task "Fix login bug"              |
        | Cancelled | renames the task "Fix login bug" to "Login fix" |
        | Cancelled | assigns the task "Fix login bug" to Alice       |
        | Cancelled | unassigns the task "Fix login bug"              |

    Scenario Outline: A <status> task still accepts comments
      Given the task "Fix login bug" has status <status>
      When Bob comments "Post-mortem notes" on the task "Fix login bug"
      Then the request succeeds

      Examples:
        | status    |
        | Done      |
        | Cancelled |

    Scenario: A reopened task can be edited again
      Given the task "Fix login bug" has status Done
      And Bob has reopened the task "Fix login bug"
      When Bob renames the task "Fix login bug" to "Fix login bug (regression)"
      Then the request succeeds

  Rule: Only contributors and the assignee may move a task

    @role-matrix
    Scenario Outline: <persona> moving a task
      Given the task "Fix login bug" has status Backlog
      When <persona> moves the task "Fix login bug" to ToDo
      Then <outcome>

      # Olivia: org Admin, not a project member. Nora: same org, not a member.
      # Mallory: another tenant. Non-visible resources are hidden as "not found".
      Examples:
        | persona | outcome                              |
        | Alice   | the request succeeds                 |
        | Bob     | the request succeeds                 |
        | Olivia  | the request succeeds                 |
        | Victor  | the request is rejected as forbidden |
        | Nora    | the request is rejected as not found |
        | Mallory | the request is rejected as not found |

    Scenario: A Viewer who is the assignee may move their own task
      Given the task "Fix login bug" is assigned to Victor
      When Victor moves the task "Fix login bug" to ToDo
      Then the request succeeds

    Scenario: An anonymous caller cannot move a task
      When an anonymous caller moves the task "Fix login bug" to ToDo
      Then the request is rejected as unauthenticated

  Rule: Every move is guarded by optimistic concurrency

    Scenario: Moving with a stale version is rejected
      Given Bob has read the task "Fix login bug"
      And Alice has since moved the task "Fix login bug" to ToDo
      When Bob moves the task "Fix login bug" to Cancelled using the version Bob read
      Then the request is rejected as conflicting
      And the task "Fix login bug" still has status ToDo

    Scenario: Moving without stating a version is rejected
      When Bob moves the task "Fix login bug" to ToDo without stating a version
      Then the request is rejected as invalid
      And the task "Fix login bug" still has status Backlog

  Rule: Every move is recorded and announced

    Scenario: A move appears in the project audit trail
      When Bob moves the task "Fix login bug" to ToDo
      Then the audit trail of project "Apollo" shows a "TaskMoved" entry by Bob for "Fix login bug"

    @background
    Scenario: A move is delivered to subscribed webhooks
      Given project "Apollo" has a webhook subscribed to "task.moved"
      When Bob moves the task "Fix login bug" to ToDo
      Then the webhook eventually receives a "task.moved" event for "Fix login bug"
