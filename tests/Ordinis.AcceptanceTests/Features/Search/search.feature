@area-search
Feature: Search
  As a user
  I want to search tasks and projects by text
  So that I can find work without knowing where it lives

  Background:
    Given an organization "Acme" whose org Admin is Olivia
    And an organization "Globex" with a user Mallory
    And a project "Apollo" in "Acme" described as "Login and identity platform"
    And project "Apollo" has a board "Sprint 1"
    And a project "Hermes" in "Acme" described as "Customer messaging"
    And project "Hermes" has a board "Inbox"
    And Bob is a Member of project "Apollo"
    And these tasks exist:
      | title               | board    | description                    |
      | Fix login bug       | Sprint 1 | Users are logged out at random |
      | Add dark mode       | Sprint 1 | Requested by many users        |
      | Reply to login spam | Inbox    | Bots hitting the login form    |

  Rule: Tasks are matched on title or description, ignoring case

    @smoke
    Scenario: Searching tasks by title
      When Bob searches tasks for "login"
      Then the request succeeds
      And the found tasks are "Fix login bug"
      And the total count is 1

    Scenario: Searching tasks by description
      When Bob searches tasks for "many users"
      Then the found tasks are "Add dark mode"

    Scenario Outline: Matching ignores case: "<term>"
      When Bob searches tasks for "<term>"
      Then the found tasks are "Fix login bug"

      Examples:
        | term   |
        | LOGIN  |
        | Login  |
        | lOgIn  |

    Scenario: Partial words match
      When Bob searches tasks for "dar"
      Then the found tasks are "Add dark mode"

    Scenario: Nothing matches
      When Bob searches tasks for "kubernetes"
      Then nothing is found
      And the total count is 0

    Scenario: Deleted tasks are not found
      Given Olivia has deleted the task "Fix login bug"
      When Olivia searches tasks for "login"
      Then the found tasks are "Reply to login spam"

  Rule: Projects are matched on name or description, ignoring case

    Scenario: Searching projects by name
      When Olivia searches projects for "apollo"
      Then the found projects are "Apollo"

    Scenario: Searching projects by description
      When Olivia searches projects for "messaging"
      Then the found projects are "Hermes"

    Scenario: Archived projects are not found
      Given the project "Hermes" is archived
      When Olivia searches projects for "messaging"
      Then nothing is found

  Rule: Results only include what the caller can see

    Scenario: A member only finds tasks of their projects
      When Bob searches tasks for "login"
      Then the found tasks are "Fix login bug"

    Scenario: The org Admin finds tasks across the organization
      When Olivia searches tasks for "login"
      Then the found tasks are "Fix login bug, Reply to login spam"

    Scenario: A member only finds their projects
      When Bob searches projects for "a"
      Then the found projects are "Apollo"

    @multi-tenant
    Scenario: Another organization finds nothing
      When Mallory searches tasks for "login"
      Then nothing is found

  Rule: Search results are paged and can be trimmed to selected fields

    Scenario: Paging search results
      Given 25 tasks on board "Sprint 1" titled "Login follow-up 1" to "Login follow-up 25"
      When Bob searches tasks for "follow-up", page 2 with 10 results per page
      Then 10 tasks are found
      And the total count is 25

    Scenario: Selecting fields of search results
      When Bob searches tasks for "login" returning only "title,status"
      Then every result contains only id, title and status

  Rule: A search needs a term and a known resource type

    Scenario Outline: Malformed search: <case>
      When Bob searches <request>
      Then the request is rejected as malformed

      Examples:
        | case             | request                           |
        | no term          | tasks without a term              |
        | blank term       | tasks for "   "                   |
        | unknown type     | comments for "login"              |

    Scenario: The resource type is case-insensitive
      When Bob searches "TASKS" for "login"
      Then the request succeeds

    Scenario: An anonymous caller cannot search
      When an anonymous caller searches tasks for "login"
      Then the request is rejected as unauthenticated
