@area-tasks
Feature: Task assignment
  As a project contributor
  I want to assign tasks to people and unassign them
  So that everyone knows who is responsible for what

  Background:
    Given an organization "Acme" whose org Admin is Olivia
    And Nora is a user in "Acme"
    And an organization "Globex" with a user Mallory
    And a project "Apollo" in "Acme" with a board "Sprint 1"
    And Alice is an Admin of project "Apollo"
    And Bob is a Member of project "Apollo"
    And Victor is a Viewer of project "Apollo"
    And a task "Fix login bug" on board "Sprint 1"

  Rule: A task has at most one assignee

    @smoke
    Scenario: Assigning an unassigned task
      When Alice assigns the task "Fix login bug" to Bob
      Then the request succeeds
      And the task "Fix login bug" is assigned to Bob
      And the task "Fix login bug" is listed among the tasks of Bob

    Scenario: Reassigning a task replaces the assignee
      Given the task "Fix login bug" is assigned to Bob
      When Alice assigns the task "Fix login bug" to Alice
      Then the request succeeds
      And the task "Fix login bug" is assigned to Alice
      And the task "Fix login bug" is not listed among the tasks of Bob

    Scenario: Assigning a task to its current assignee is rejected
      Given the task "Fix login bug" is assigned to Bob
      When Alice assigns the task "Fix login bug" to Bob
      Then the request is rejected as invalid with error "task.already-assigned"

    Scenario: The assignee must be an existing user
      When Alice assigns the task "Fix login bug" to an unknown user
      Then the request is rejected as invalid on field "assigneeId"
      And the task "Fix login bug" has no assignee

    # GAP-04: only existence of the assignee is validated today.
    @gap @multi-tenant
    Scenario Outline: The assignee must be a contributor of the project: <assignee>
      When Alice assigns the task "Fix login bug" to <assignee>
      Then the request is rejected as invalid on field "assigneeId"

      Examples:
        | assignee |
        | Victor   |
        | Nora     |
        | Mallory  |

    Scenario Outline: A <status> task cannot be assigned
      Given the task "Fix login bug" has status <status>
      When Alice assigns the task "Fix login bug" to Bob
      Then the request is rejected as invalid with error "task.terminal-state"

      Examples:
        | status    |
        | Done      |
        | Cancelled |

    Scenario Outline: Assignment does not depend on the workflow status: <status>
      Given the task "Fix login bug" has status <status>
      When Alice assigns the task "Fix login bug" to Bob
      Then the request succeeds
      And the task "Fix login bug" still has status <status>

      Examples:
        | status     |
        | Backlog    |
        | ToDo       |
        | InProgress |
        | InReview   |

  Rule: An assigned task can be unassigned

    Scenario: Unassigning a task
      Given the task "Fix login bug" is assigned to Bob
      When Alice unassigns the task "Fix login bug"
      Then the request succeeds
      And the task "Fix login bug" has no assignee
      And the task "Fix login bug" is not listed among the tasks of Bob

    Scenario: Unassigning a task that has no assignee is rejected
      When Alice unassigns the task "Fix login bug"
      Then the request is rejected as invalid with error "task.already-unassigned"

    Scenario Outline: A <status> task cannot be unassigned
      Given the task "Fix login bug" is assigned to Bob
      And the task "Fix login bug" has status <status>
      When Alice unassigns the task "Fix login bug"
      Then the request is rejected as invalid with error "task.terminal-state"
      And the task "Fix login bug" is assigned to Bob

      Examples:
        | status    |
        | Done      |
        | Cancelled |

  Rule: Contributors and the current assignee may change the assignment

    @role-matrix
    Scenario Outline: <persona> assigning a task
      When <persona> assigns the task "Fix login bug" to Bob
      Then <outcome>

      Examples:
        | persona | outcome                              |
        | Alice   | the request succeeds                 |
        | Bob     | the request succeeds                 |
        | Olivia  | the request succeeds                 |
        | Victor  | the request is rejected as forbidden |
        | Nora    | the request is rejected as not found |
        | Mallory | the request is rejected as not found |

    @role-matrix
    Scenario Outline: <persona> unassigning a task
      Given the task "Fix login bug" is assigned to Bob
      When <persona> unassigns the task "Fix login bug"
      Then <outcome>

      Examples:
        | persona | outcome                              |
        | Alice   | the request succeeds                 |
        | Bob     | the request succeeds                 |
        | Olivia  | the request succeeds                 |
        | Victor  | the request is rejected as forbidden |
        | Nora    | the request is rejected as not found |
        | Mallory | the request is rejected as not found |

    Scenario: A Viewer who is the assignee may hand the task over
      Given the task "Fix login bug" is assigned to Victor
      When Victor assigns the task "Fix login bug" to Bob
      Then the request succeeds
      And the task "Fix login bug" is assigned to Bob

    Scenario: An assignee who has left the project may still hand the task over
      Given the task "Fix login bug" is assigned to Bob
      And Bob has been removed from project "Apollo"
      When Bob assigns the task "Fix login bug" to Alice
      Then the request succeeds

  Rule: Assignment changes are guarded by optimistic concurrency

    Scenario: Assigning with a stale version is rejected
      Given Bob has read the task "Fix login bug"
      And Alice has since assigned the task "Fix login bug" to Alice
      When Bob assigns the task "Fix login bug" to Bob using the version Bob read
      Then the request is rejected as conflicting
      And the task "Fix login bug" is assigned to Alice

    Scenario: Unassigning without stating a version is rejected
      Given the task "Fix login bug" is assigned to Bob
      When Alice unassigns the task "Fix login bug" without stating a version
      Then the request is rejected as invalid
      And the task "Fix login bug" is assigned to Bob

  Rule: Assignment changes are recorded and announced

    Scenario: Assigning and unassigning appear in the audit trail
      Given Alice has assigned the task "Fix login bug" to Bob
      When Alice unassigns the task "Fix login bug"
      Then the audit trail of project "Apollo" shows, newest first:
        | event          | actor | task          |
        | TaskUnassigned | Alice | Fix login bug |
        | TaskAssigned   | Alice | Fix login bug |

    @background
    Scenario: Subscribed webhooks receive assignments
      Given project "Apollo" has a webhook subscribed to "task.assigned"
      When Alice assigns the task "Fix login bug" to Bob
      Then the webhook eventually receives a "task.assigned" event for "Fix login bug"
