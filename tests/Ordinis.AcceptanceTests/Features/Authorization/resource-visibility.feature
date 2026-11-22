@area-authorization
Feature: Resource visibility
  As the operator of a multi-tenant service
  I want callers to learn nothing about resources they may not see
  So that project names, task IDs and tenants cannot be discovered by probing

  The rule: a caller who is not a member of a project (and not the org Admin)
  gets exactly the same "not found" as for a resource that does not exist.
  A member whose role is merely too low gets "forbidden", because they can
  already see the resource.

  Background:
    Given an organization "Acme" whose org Admin is Olivia
    And Nora is a user in "Acme"
    And an organization "Globex" with a user Mallory
    And a project "Apollo" in "Acme" with a board "Sprint 1"
    And Alice is an Admin of project "Apollo"
    And Victor is a Viewer of project "Apollo"
    And a task "Fix login bug" on board "Sprint 1"

  Rule: Non-members cannot tell a hidden resource from a missing one

    Scenario Outline: Nora looking at <resource>
      When Nora views <resource>
      And Nora views <missing resource>
      Then both requests are rejected as not found
      And both responses are identical apart from the identifier

      Examples:
        | resource                         | missing resource  |
        | the project "Apollo"             | an unknown project |
        | the board "Sprint 1"             | an unknown board   |
        | the task "Fix login bug"         | an unknown task    |
        | the comments of "Fix login bug"  | the comments of an unknown task |
        | the audit trail of project "Apollo" | the audit trail of an unknown project |

    Scenario Outline: Nora attempting to change <target>
      When Nora <action>
      Then the request is rejected as not found

      Examples:
        | target  | action                                         |
        | project | renames the project "Apollo" to "Hijacked"     |
        | board   | renames the board "Sprint 1" to "Hijacked"     |
        | task    | moves the task "Fix login bug" to ToDo         |
        | members | adds Nora to project "Apollo" as an Admin      |
        | webhooks| lists the webhooks of project "Apollo"         |

  Rule: Members with too low a role are told they are forbidden

    Scenario Outline: Victor attempting to <action>
      When Victor <action>
      Then the request is rejected as forbidden
      And the error is "You do not have permission to perform this action."

      Examples:
        | action                                         |
        | renames the project "Apollo" to "Hijacked"     |
        | creates a board "Sprint 2" in project "Apollo" |
        | moves the task "Fix login bug" to ToDo         |
        | comments "Hi" on the task "Fix login bug"      |
        | lists the webhooks of project "Apollo"         |

  Rule: The org Admin can see and manage every project in the organization

    Scenario Outline: Olivia, who is not a project member, can <action>
      When Olivia <action>
      Then the request succeeds

      Examples:
        | action                                         |
        | views the project "Apollo"                     |
        | renames the project "Apollo" to "Apollo 2"     |
        | adds Nora to project "Apollo" as a Member      |
        | deletes the task "Fix login bug"               |
        | lists the webhooks of project "Apollo"         |

  Rule: Lists silently exclude what the caller may not see

    Scenario Outline: Nora's <list> list does not reveal project "Apollo"
      When Nora <listing>
      Then nothing from project "Apollo" is listed

      Examples:
        | list    | listing                                      |
        | project | lists the projects                           |
        | task    | lists all tasks                              |
        | search  | searches tasks for "login"                   |
        | org     | lists the projects of organization "Acme"    |
