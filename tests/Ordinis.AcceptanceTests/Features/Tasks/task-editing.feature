@area-tasks
Feature: Task editing and deletion
  As a project contributor
  I want to keep task details up to date and remove obsolete tasks
  So that the board reflects reality

  Background:
    Given an organization "Acme" whose org Admin is Olivia
    And Nora is a user in "Acme"
    And an organization "Globex" with a user Mallory
    And a project "Apollo" in "Acme" with a board "Sprint 1"
    And Alice is an Admin of project "Apollo"
    And Bob is a Member of project "Apollo"
    And Victor is a Viewer of project "Apollo"
    And a task "Fix login bug" on board "Sprint 1" with:
      | description | Users are logged out at random |
      | priority    | Medium                         |
      | due date    | 7 days from now                |

  Rule: Title, description, priority and due date can be changed

    @smoke
    Scenario: Updating every detail of a task
      When Bob updates the task "Fix login bug" with:
        | title       | Fix session expiry bug       |
        | description | Sessions expire after 5 min  |
        | priority    | Critical                     |
        | due date    | 2 days from now              |
      Then the request succeeds
      And the task "Fix session expiry bug" has priority Critical
      And the task "Fix session expiry bug" has description "Sessions expire after 5 min"
      And the task "Fix session expiry bug" is due 2 days from now

    Scenario: Omitting the description and due date clears them
      When Bob updates the task "Fix login bug" with only a title "Fix login bug" and priority Medium
      Then the request succeeds
      And the task "Fix login bug" has no description and no due date

    Scenario: Updating a task does not change its status or assignee
      Given the task "Fix login bug" has status InProgress
      And the task "Fix login bug" is assigned to Bob
      When Bob renames the task "Fix login bug" to "Fix login bug (iOS)"
      Then the task "Fix login bug (iOS)" still has status InProgress
      And the task "Fix login bug (iOS)" is assigned to Bob

    Scenario: An update is recorded in the audit trail
      When Bob renames the task "Fix login bug" to "Fix login bug (iOS)"
      Then the audit trail of project "Apollo" shows a "TaskUpdated" entry by Bob for "Fix login bug (iOS)"

  Rule: Updated details must be valid

    Scenario Outline: Invalid update: <case>
      When Bob updates the task "Fix login bug" <change>
      Then the request is rejected as invalid <reason>
      And the task "Fix login bug" still has priority Medium

      Examples:
        | case                 | change                       | reason                                 |
        | blank title          | with a blank title           | on field "title"                       |
        | 201-character title  | with a 201-character title   | on field "title"                       |
        | unknown priority     | with an unknown priority     | on field "priority"                    |
        | due date in the past | with a due date of yesterday | with error "task.due-date-in-past"     |

    Scenario: A Done or Cancelled task cannot be updated
      Given the task "Fix login bug" has status Done
      When Bob renames the task "Fix login bug" to "Fix login bug (iOS)"
      Then the request is rejected as invalid with error "task.terminal-state"

    # GAP-08 (fixed): an archived board or project makes its tasks read-only.
    Scenario Outline: A task on an archived <container> cannot be updated
      Given the <container> is archived
      When Bob renames the task "Fix login bug" to "Fix login bug (iOS)"
      Then the request is rejected as invalid

      Examples:
        | container           |
        | board "Sprint 1"    |
        | project "Apollo"    |

  Rule: Contributors and the assignee may update a task

    @role-matrix
    Scenario Outline: <persona> updating a task
      When <persona> renames the task "Fix login bug" to "Fix login bug (iOS)"
      Then <outcome>

      Examples:
        | persona | outcome                              |
        | Alice   | the request succeeds                 |
        | Bob     | the request succeeds                 |
        | Olivia  | the request succeeds                 |
        | Victor  | the request is rejected as forbidden |
        | Nora    | the request is rejected as not found |
        | Mallory | the request is rejected as not found |

    Scenario: A Viewer who is the assignee may update their own task
      Given the task "Fix login bug" is assigned to Victor
      When Victor renames the task "Fix login bug" to "Fix login bug (iOS)"
      Then the request succeeds

  Rule: Updates are guarded by optimistic concurrency

    Scenario: Updating with a stale version is rejected
      Given Bob has read the task "Fix login bug"
      And Alice has since renamed the task "Fix login bug" to "Fix login (Alice)"
      When Bob renames the task "Fix login (Alice)" to "Fix login (Bob)" using the version Bob read
      Then the request is rejected as conflicting
      And the task "Fix login (Alice)" exists

    Scenario: Updating without stating a version is rejected
      When Bob renames the task "Fix login bug" to "Fix login bug (iOS)" without stating a version
      Then the request is rejected as invalid

  Rule: Only project administrators may delete a task, and deletion is soft

    @smoke
    Scenario: Deleting a task hides it everywhere
      When Alice deletes the task "Fix login bug"
      Then the request succeeds
      And the task "Fix login bug" is not found
      And the task "Fix login bug" is not listed on board "Sprint 1"
      And the task "Fix login bug" is not listed in project "Apollo"
      And the audit trail of project "Apollo" shows a "TaskDeleted" entry by Alice for "Fix login bug"

    Scenario Outline: A task can be deleted in any status: <status>
      Given the task "Fix login bug" has status <status>
      When Alice deletes the task "Fix login bug"
      Then the request succeeds

      Examples:
        | status     |
        | Backlog    |
        | InProgress |
        | Done       |
        | Cancelled  |

    Scenario: A deleted task cannot be deleted again
      Given Alice has deleted the task "Fix login bug"
      When Alice deletes the task "Fix login bug"
      Then the request is rejected as not found

    @role-matrix
    Scenario Outline: <persona> deleting a task
      When <persona> deletes the task "Fix login bug"
      Then <outcome>

      Examples:
        | persona | outcome                              |
        | Alice   | the request succeeds                 |
        | Olivia  | the request succeeds                 |
        | Bob     | the request is rejected as forbidden |
        | Victor  | the request is rejected as forbidden |
        | Nora    | the request is rejected as not found |
        | Mallory | the request is rejected as not found |

    Scenario: Deleting with a stale version is rejected
      Given Alice has read the task "Fix login bug"
      And Bob has since renamed the task "Fix login bug" to "Fix login bug (iOS)"
      When Alice deletes the task "Fix login bug (iOS)" using the version Alice read
      Then the request is rejected as conflicting
      And the task "Fix login bug (iOS)" exists

    Scenario: Deleting without stating a version is rejected
      When Alice deletes the task "Fix login bug" without stating a version
      Then the request is rejected as invalid
      And the task "Fix login bug" exists
