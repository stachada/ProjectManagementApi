@area-tasks
Feature: Task creation
  As a project contributor
  I want to create tasks on a board
  So that work can be planned and tracked

  Background:
    Given an organization "Acme" whose org Admin is Olivia
    And Nora is a user in "Acme"
    And an organization "Globex" with a user Mallory
    And a project "Apollo" in "Acme" with a board "Sprint 1"
    And Alice is an Admin of project "Apollo"
    And Bob is a Member of project "Apollo"
    And Victor is a Viewer of project "Apollo"

  Rule: A created task starts unassigned in the backlog unless an assignee is given

    @smoke
    Scenario: Creating a task with only the required details
      When Bob creates a task "Write release notes" on board "Sprint 1" with priority Medium
      Then the request succeeds
      And the response points to the new task
      And the task "Write release notes" has status Backlog
      And the task "Write release notes" has no assignee
      And the task "Write release notes" has no description and no due date

    Scenario: Creating a task with every detail
      When Bob creates a task on board "Sprint 1" with:
        | title       | Write release notes          |
        | description | Cover every merged PR        |
        | priority    | High                         |
        | assignee    | Alice                        |
        | due date    | 7 days from now              |
      Then the request succeeds
      And the task "Write release notes" has status Backlog
      And the task "Write release notes" is assigned to Alice
      And the task "Write release notes" has priority High
      And the task "Write release notes" is due 7 days from now

    Scenario: A task created with an assignee announces the assignment
      When Bob creates a task "Write release notes" on board "Sprint 1" assigned to Alice
      Then the audit trail of project "Apollo" shows a "TaskCreated" entry by Bob for "Write release notes"
      And the audit trail of project "Apollo" shows a "TaskAssigned" entry by Bob for "Write release notes"

    Scenario Outline: Every priority is accepted: <priority>
      When Bob creates a task "Write release notes" on board "Sprint 1" with priority <priority>
      Then the request succeeds
      And the task "Write release notes" has priority <priority>

      Examples:
        | priority |
        | Low      |
        | Medium   |
        | High     |
        | Critical |

  Rule: Task details must be valid

    Scenario Outline: Invalid title: <case>
      When Bob creates a task <title> on board "Sprint 1"
      Then the request is rejected as invalid on field "title"

      Examples:
        | case               | title                    |
        | missing            | without a title          |
        | blank              | with a blank title       |
        | 201 characters     | with a 201-character title |

    Scenario: A title of exactly 200 characters is accepted
      When Bob creates a task with a 200-character title on board "Sprint 1"
      Then the request succeeds

    Scenario: An unknown priority is rejected
      When Bob creates a task "Write release notes" on board "Sprint 1" with an unknown priority
      Then the request is rejected as invalid on field "priority"

    Scenario Outline: The due date must lie in the future: <due date>
      When Bob creates a task "Write release notes" on board "Sprint 1" due <due date>
      Then the request is rejected as invalid with error "task.due-date-in-past"

      Examples:
        | due date  |
        | yesterday |
        | now       |

    Scenario: The assignee must be an existing user
      When Bob creates a task "Write release notes" on board "Sprint 1" assigned to an unknown user
      Then the request is rejected as invalid on field "assigneeId"

    # GAP-04: only existence of the assignee is validated today.
    @gap @multi-tenant
    Scenario Outline: The assignee must be a contributor of the project: <assignee>
      When Bob creates a task "Write release notes" on board "Sprint 1" assigned to <assignee>
      Then the request is rejected as invalid on field "assigneeId"

      Examples:
        | assignee |
        | Nora     |
        | Mallory  |

  Rule: A task can only be created on an existing, active board

    Scenario: The board must exist
      When Bob creates a task "Write release notes" on an unknown board
      Then the request is rejected as invalid on field "boardId"

    Scenario: The board must not be archived
      Given the board "Sprint 1" is archived
      When Bob creates a task "Write release notes" on board "Sprint 1"
      Then the request is rejected as invalid on field "boardId"

    # GAP-08: CreateTask checks the board but not the project.
    @gap
    Scenario: The project must not be archived
      Given the project "Apollo" is archived
      When Bob creates a task "Write release notes" on board "Sprint 1"
      Then the request is rejected as invalid

  Rule: Only contributors of the project may create tasks

    @role-matrix
    Scenario Outline: <persona> creating a task
      When <persona> creates a task "Write release notes" on board "Sprint 1"
      Then <outcome>

      Examples:
        | persona | outcome                            |
        | Alice   | the request succeeds               |
        | Bob     | the request succeeds               |
        | Olivia  | the request succeeds               |
        | Victor  | the request is rejected as invalid |
        | Nora    | the request is rejected as invalid |
        | Mallory | the request is rejected as invalid |

    Scenario: An anonymous caller cannot create a task
      When an anonymous caller creates a task "Write release notes" on board "Sprint 1"
      Then the request is rejected as unauthenticated

  Rule: Task creation is announced

    @background
    Scenario: Subscribed webhooks receive the created task
      Given project "Apollo" has a webhook subscribed to "task.created"
      When Bob creates a task "Write release notes" on board "Sprint 1"
      Then the webhook eventually receives a "task.created" event for "Write release notes"
