@area-platform
Feature: Response caching
  As an API client
  I want to know how long I may reuse a response
  So that I avoid redundant requests without seeing other users' data

  Single-resource reads may be cached privately by the client for 30 seconds.
  Responses are personalised (access depends on the caller), so shared caches
  must not store them.

  Background:
    Given an organization "Acme" whose org Admin is Olivia
    And a project "Apollo" in "Acme" with a board "Sprint 1"
    And Bob is a Member of project "Apollo"
    And a task "Fix login bug" on board "Sprint 1"

  Rule: Single-resource reads are privately cacheable for 30 seconds

    Scenario Outline: Reading <resource>
      When Bob views <resource>
      Then the response may be cached by the client only, for 30 seconds
      And the response varies by caller and encoding

      Examples:
        | resource                           |
        | the task "Fix login bug"           |
        | the task "Fix login bug" in API v2 |
        | the project "Apollo"               |
        | the board "Sprint 1"               |
        | the organization "Acme"            |
        | the user Bob                       |

  Rule: Lists and writes are not cacheable

    Scenario Outline: <request> carries no caching permission
      When Bob <request>
      Then the response does not allow caching

      Examples:
        | request                                    |
        | lists all tasks                            |
        | lists the tasks of board "Sprint 1"        |
        | searches tasks for "login"                 |
        | moves the task "Fix login bug" to ToDo     |

  Rule: Cached reads are never shared between callers

    Scenario: The server does not serve one user's cached response to another
      Given Bob has viewed the task "Fix login bug"
      And Nora is a user in "Acme"
      When Nora views the task "Fix login bug"
      Then the request is rejected as not found
