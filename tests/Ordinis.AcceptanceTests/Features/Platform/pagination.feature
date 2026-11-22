@area-platform
Feature: Pagination
  As an API client
  I want long lists split into pages with a total count
  So that responses stay small and I can build paging controls

  Background:
    Given an organization "Acme" whose org Admin is Olivia
    And a project "Apollo" in "Acme" with a board "Sprint 1"
    And Bob is a Member of project "Apollo"
    And 45 tasks on board "Sprint 1" assigned to Bob

  Rule: Lists return page 1 of 20 items by default, with the total count

    @smoke
    Scenario: Default page
      When Bob lists all tasks
      Then 20 tasks are listed
      And the total count is 45

    Scenario Outline: Requesting page <page> of size <size>
      When Bob lists all tasks, page <page> with <size> per page
      Then <count> tasks are listed
      And the total count is 45

      Examples:
        | page | size | count |
        | 1    | 10   | 10    |
        | 5    | 10   | 5     |
        | 6    | 10   | 0     |
        | 1    | 100  | 45    |
        | 3    | 20   | 5     |

    Scenario: Pages do not overlap and together contain every task
      When Bob reads every page of all tasks with 10 per page
      Then every task appears exactly once

  Rule: Page sizes are limited to 1..100

    Scenario: Asking for more than 100 per page returns 100
      Given 150 tasks on board "Sprint 1" in total
      When Bob lists all tasks, page 1 with 500 per page
      Then 100 tasks are listed

    Scenario Outline: A page number below 1 returns the first page: <page>
      When Bob lists all tasks, page <page> with 10 per page
      Then the first 10 tasks are listed

      Examples:
        | page |
        | 0    |
        | -1   |

    Scenario Outline: A page size below 1 on <list> is treated as 1
      When Bob lists <list>, page 1 with <size> per page
      Then 1 entry is listed

      Examples: Lists that clamp today
        | list                         | size |
        | the tasks of Bob             | 0    |
        | the projects of organization "Acme" | 0 |

      # GAP-01: these queries only cap the maximum, so 0 / negative sizes
      # yield an empty page or a database error.
      @gap
      Examples: Lists that do not clamp yet
        | list                                | size |
        | all tasks                           | 0    |
        | all tasks                           | -5   |
        | the projects                        | 0    |
        | the tasks of project "Apollo"       | 0    |
        | the tasks of board "Sprint 1"       | 0    |
        | the audit trail of project "Apollo" | 0    |

  Rule: Every paged list reports its total

    Scenario Outline: <list> reports the total count
      When Bob lists <list>
      Then the total count is reported

      Examples:
        | list                                |
        | all tasks                           |
        | the tasks of project "Apollo"       |
        | the tasks of board "Sprint 1"       |
        | the tasks of Bob                    |
        | the projects                        |
        | the projects of organization "Acme" |
        | the audit trail of project "Apollo" |
        | the search results for "task"       |
