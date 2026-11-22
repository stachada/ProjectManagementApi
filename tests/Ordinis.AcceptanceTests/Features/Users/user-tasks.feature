@area-users
Feature: A user's assigned tasks
  As a user
  I want to see every task assigned to me (or a colleague) in one list
  So that I know what is on someone's plate across projects

  Background:
    Given an organization "Acme" whose org Admin is Olivia
    And an organization "Globex" with a user Mallory
    And a project "Apollo" in "Acme" with boards "Sprint 1" and "Sprint 2"
    And a project "Hermes" in "Acme" with a board "Inbox"
    And Bob is a Member of project "Apollo"
    And Bob is a Member of project "Hermes"
    And Nora is a Viewer of project "Apollo"
    And these tasks exist, created in this order:
      | title          | board    | status     | priority | assignee | due in  |
      | Fix login bug  | Sprint 1 | InProgress | Critical | Bob      | 2 days  |
      | Write docs     | Sprint 2 | ToDo       | Medium   | Bob      | 10 days |
      | Reply to Initech | Inbox  | Backlog    | High     | Bob      | 1 day   |
      | Add dark mode  | Sprint 1 | Backlog    | Low      | Nora     |         |

  Rule: The list contains only tasks assigned to that user, newest first by default

    @smoke
    Scenario: Listing one's own tasks
      When Bob lists the tasks of Bob
      Then the request succeeds
      And the listed tasks are "Reply to Initech, Write docs, Fix login bug"
      And the total count is 3

    Scenario: The list reflects unassignment
      Given Bob has unassigned the task "Write docs"
      When Bob lists the tasks of Bob
      Then the task "Write docs" is not among the listed tasks

    Scenario: Deleted tasks are not listed
      Given Olivia has deleted the task "Write docs"
      When Bob lists the tasks of Bob
      Then the task "Write docs" is not among the listed tasks

  Rule: The list only contains tasks from projects the caller can see

    Scenario: A colleague sees only the tasks of shared projects
      When Nora lists the tasks of Bob
      Then the listed tasks are "Write docs, Fix login bug"

    Scenario: The org Admin sees all of a user's tasks
      When Olivia lists the tasks of Bob
      Then the total count is 3

    @multi-tenant
    Scenario: Users of another organization are hidden
      When Mallory lists the tasks of Bob
      Then the request is rejected as not found

    Scenario: Listing the tasks of an unknown user
      When Bob lists the tasks of an unknown user
      Then the request is rejected as not found

  Rule: The list can be filtered and sorted

    Scenario Outline: Filtering Bob's tasks by <filter>
      When Bob lists the tasks of Bob filtered by <filter>
      Then the listed tasks are "<tasks>"

      Examples:
        | filter            | tasks                          |
        | board "Sprint 1"  | Fix login bug                  |
        | status ToDo       | Write docs                     |
        | priority High     | Reply to Initech               |
        | due before 5 days | Reply to Initech, Fix login bug |
        | due after 5 days  | Write docs                     |

    Scenario Outline: Sorting Bob's tasks by <field> <direction>
      When Bob lists the tasks of Bob sorted by <field> <direction>
      Then the listed tasks are "<tasks>"

      Examples:
        | field     | direction  | tasks                                       |
        | title     | ascending  | Fix login bug, Reply to Initech, Write docs |
        | dueDate   | ascending  | Reply to Initech, Fix login bug, Write docs |
        | createdAt | ascending  | Fix login bug, Write docs, Reply to Initech |
        | createdAt | descending | Reply to Initech, Write docs, Fix login bug |

    # GAP-14: priority sorts alphabetically today.
    @gap
    Scenario: Sorting Bob's tasks by priority
      When Bob lists the tasks of Bob sorted by priority descending
      Then the listed tasks are "Fix login bug, Reply to Initech, Write docs"

    Scenario: An unknown sort field falls back to newest first
      When Bob lists the tasks of Bob sorted by colour ascending
      Then the listed tasks are "Reply to Initech, Write docs, Fix login bug"
