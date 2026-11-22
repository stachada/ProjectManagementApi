@area-authorization @multi-tenant
Feature: Tenant isolation
  As a customer of Ordinis
  I want my organization's data to be completely invisible to other organizations
  So that no other tenant — not even their administrators — can read or change it

  Background:
    Given an organization "Acme" whose org Admin is Olivia
    And a project "Apollo" in "Acme" with a board "Sprint 1"
    And Bob is a Member of project "Apollo"
    And a task "Fix login bug" on board "Sprint 1"
    And an organization "Globex" whose org Admin is Gina
    And Mallory is a user in "Globex"

  Rule: Organization Admin authority stops at the organization boundary

    Scenario Outline: Gina, the Globex org Admin, trying to <action>
      When Gina <step>
      Then the request is rejected as not found

      Examples:
        | action                    | step                                          |
        | view the organization     | views the organization "Acme"                 |
        | rename the organization   | renames the organization "Acme" to "Owned"    |
        | suspend the organization  | suspends the organization "Acme"              |
        | view a user               | views the user Bob                            |
        | deactivate a user         | deactivates the user Bob                      |
        | change a user's org role  | changes the org role of Bob to Viewer         |
        | view a project            | views the project "Apollo"                    |
        | delete a project          | deletes the project "Apollo"                  |
        | view a board              | views the board "Sprint 1"                    |
        | view a task               | views the task "Fix login bug"                |
        | move a task               | moves the task "Fix login bug" to ToDo        |
        | read the audit trail      | reads the audit trail of project "Apollo"     |
        | list a user's tasks       | lists the tasks of Bob                        |

  Rule: Lists and search never cross the organization boundary

    Scenario Outline: Gina's <list> contains nothing from "Acme"
      When Gina <step>
      Then nothing from organization "Acme" is listed

      Examples:
        | list           | step                            |
        | project list   | lists the projects              |
        | task list      | lists all tasks                 |
        | task search    | searches tasks for "login"      |
        | project search | searches projects for "Apollo"  |

  Rule: References to another tenant's resources in a request are rejected

    Scenario: A task cannot be created on another tenant's board
      When Mallory creates a task "Plant a bug" on board "Sprint 1"
      Then the request is rejected as invalid
      And the board "Sprint 1" has no task "Plant a bug"

    Scenario: A project cannot be created in another tenant's organization
      When Mallory creates a project "Trojan" in "Acme"
      Then the request is rejected as invalid

    # GAP-03 / GAP-04 (fixed): users must belong to the project's organization and be able to work on it.
    Scenario Outline: Another tenant's user cannot be pulled into a project: <action>
      Given Olivia is an Admin of project "Apollo"
      When Olivia <action>
      Then the request is rejected as invalid

      Examples:
        | action                                        |
        | adds Mallory to project "Apollo" as a Member  |
        | assigns the task "Fix login bug" to Mallory   |

  Rule: Signing in is scoped to one organization

    Scenario: Credentials of one tenant do not work in another
      When Mallory signs in to "Acme" with Mallory's own email and password
      Then the request is rejected as unauthenticated
