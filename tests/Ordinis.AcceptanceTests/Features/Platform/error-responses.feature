@area-platform
Feature: Error responses
  As an API client
  I want every error to have the same machine-readable shape
  So that I can handle failures generically and show useful messages

  Every error is an RFC 9457 Problem Details document (application/problem+json)
  with type, title, status, detail, instance (the request path) and the
  request's correlationId.

  Background:
    Given an organization "Acme"
    And a project "Apollo" in "Acme" with a board "Sprint 1"
    And Alice is an Admin of project "Apollo"
    And Bob is a Member of project "Apollo"
    And Victor is a Viewer of project "Apollo"
    And a task "Fix login bug" on board "Sprint 1"

  Rule: Every error is a Problem Details document

    @smoke
    Scenario Outline: <outcome> errors are Problem Details
      When <cause>
      Then the request is rejected as <outcome>
      And the response is a Problem Details document with status <status> and title "<title>"
      And the Problem Details instance is the request path
      And the Problem Details carry the request's correlation id

      Examples:
        | outcome            | status | title                                    | cause                                                       |
        | malformed          | 400    | One or more request fields were invalid. | Bob creates a task with a body that is not valid JSON       |
        | unauthenticated    | 401    | Authentication required                  | an anonymous caller views the task "Fix login bug"          |
        | forbidden          | 403    | Access denied                            | Victor moves the task "Fix login bug" to ToDo               |
        | not found          | 404    | Resource not found                       | Bob views an unknown task                                   |
        | method not allowed | 405    | Method not allowed                       | Bob patches the task "Fix login bug"                        |
        | conflicting        | 409    | Concurrency conflict                     | Bob moves the task "Fix login bug" to ToDo using an outdated version |
        | invalid            | 422    | One or more validation errors occurred.  | Bob creates a task with a blank title on board "Sprint 1"   |
        | invalid            | 422    | Business rule violated                   | Bob moves the task "Fix login bug" to Done                  |

    Scenario: An unknown address is a Problem Details "not found"
      When Bob calls an address that does not exist
      Then the request is rejected as not found
      And the response is a Problem Details document with status 404 and title "Resource not found"

  Rule: Validation errors list every failing field

    Scenario: Several invalid fields are reported together
      When Bob creates a task on board "Sprint 1" with a blank title and an unknown priority
      Then the request is rejected as invalid
      And the validation errors contain messages for the fields:
        | field    |
        | title    |
        | priority |

  Rule: Business rule violations carry a stable error code

    Scenario Outline: Error code <code>
      When <cause>
      Then the Problem Details type is "urn:ordinis:error:<code>"

      Examples:
        | code                           | cause                                                |
        | task.invalid-status-transition | Bob moves the task "Fix login bug" to Done           |
        | task.already-unassigned        | Bob unassigns the task "Fix login bug"               |
        | board.not-archived             | Alice unarchives the board "Sprint 1"                |
        | project.not-archived           | Alice unarchives the project "Apollo"                |

    Scenario: Other errors use an HTTP status type
      When Bob views an unknown task
      Then the Problem Details type is "https://httpstatuses.io/404"

  Rule: Unexpected failures do not leak internals

    Scenario: A server error hides implementation details
      Given the database is unavailable
      When Bob views the task "Fix login bug"
      Then the response is a Problem Details document with status 500 and title "An unexpected error occurred"
      And the response contains no stack trace, SQL or connection details
      And the Problem Details carry the request's correlation id
