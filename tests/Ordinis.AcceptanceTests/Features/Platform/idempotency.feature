@area-platform
Feature: Idempotent creation
  As an API client on an unreliable network
  I want to safely retry a create request
  So that a timeout never leaves me with duplicate tasks, comments or projects

  Clients may send an Idempotency-Key with create requests. A retry with the
  same key and the same body returns the original response instead of
  creating a second resource. Keys are remembered for 24 hours.

  Background:
    Given an organization "Acme" whose org Admin is Olivia
    And a project "Apollo" in "Acme" with a board "Sprint 1"
    And Alice is an Admin of project "Apollo"
    And Bob is a Member of project "Apollo"
    And a task "Fix login bug" on board "Sprint 1"

  Rule: Retrying with the same key and body replays the original response

    @smoke
    Scenario: A retried create does not duplicate the task
      Given Bob has created a task "Write docs" on board "Sprint 1" with request key "k-1"
      When Bob creates a task "Write docs" on board "Sprint 1" with request key "k-1"
      Then the request succeeds
      And the response is identical to the first one, including where it points
      And board "Sprint 1" has exactly one task "Write docs"

    Scenario Outline: Every create endpoint honours the request key: <create>
      Given Bob has sent "<create>" with request key "k-1"
      When Bob sends "<create>" with request key "k-1" again
      Then the second response replays the first
      And only one <resource> was created

      Examples:
        | create                                                    | resource     |
        | creates a task "Write docs" on board "Sprint 1"           | task         |
        | comments "On it" on the task "Fix login bug"              | comment      |
        | uploads "crash.log" to the task "Fix login bug"           | attachment   |
        | creates a board "Sprint 2" in project "Apollo"            | board        |
        | creates a project "Hermes" in "Acme"                      | project      |
        | adds Olivia to project "Apollo" as a Viewer                | membership   |
        | registers a webhook for project "Apollo" at "https://hooks.example.com/x" | webhook |

    Scenario Outline: Org-level creates honour the request key: <create>
      Given Olivia has sent "<create>" with request key "k-1"
      When Olivia sends "<create>" with request key "k-1" again
      Then the second response replays the first
      And only one <resource> was created

      Examples:
        | create                                                | resource     |
        | creates a user "Dan" with email "dan@acme.example"    | user         |
        | creates an organization "Initech"                     | organization |

    Scenario: Without a request key, every request creates a resource
      Given Bob has created a task "Write docs" on board "Sprint 1"
      When Bob creates a task "Write docs" on board "Sprint 1"
      Then board "Sprint 1" has 2 tasks "Write docs"

  Rule: Reusing a key for a different request is a conflict

    Scenario: Same key, different body
      Given Bob has created a task "Write docs" on board "Sprint 1" with request key "k-1"
      When Bob creates a task "Write tests" on board "Sprint 1" with request key "k-1"
      Then the request is rejected as conflicting
      And the error explains the key was already used with a different request
      And board "Sprint 1" has no task "Write tests"

  Rule: Keys are scoped to the user and the endpoint

    Scenario: Two users may use the same key independently
      Given Bob has created a task "Write docs" on board "Sprint 1" with request key "k-1"
      When Alice creates a task "Write docs" on board "Sprint 1" with request key "k-1"
      Then the request succeeds
      And board "Sprint 1" has 2 tasks "Write docs"

    Scenario: The same key may be used on different endpoints
      Given Bob has created a task "Write docs" on board "Sprint 1" with request key "k-1"
      When Bob creates a board "Sprint 2" in project "Apollo" with request key "k-1"
      Then the request succeeds

  Rule: Only successful responses are remembered

    Scenario: A failed request can be retried with the same key once the cause is fixed
      Given the board "Sprint 1" is archived
      And Bob's attempt to create a task "Write docs" on board "Sprint 1" with request key "k-1" was rejected as invalid
      And Alice has unarchived the board "Sprint 1"
      When Bob creates a task "Write docs" on board "Sprint 1" with request key "k-1"
      Then the request succeeds
      And board "Sprint 1" has exactly one task "Write docs"

  Rule: Keys expire after 24 hours

    @needs-clock
    Scenario: A key can be reused after it expires
      Given Bob has created a task "Write docs" on board "Sprint 1" with request key "k-1"
      And 24 hours and 1 minute have passed
      When Bob creates a task "Write docs" on board "Sprint 1" with request key "k-1"
      Then board "Sprint 1" has 2 tasks "Write docs"

  Rule: Requests that are not creates ignore the key

    Scenario: A state transition is not replayed
      Given Bob has moved the task "Fix login bug" to ToDo with request key "k-1"
      When Bob moves the task "Fix login bug" to ToDo with request key "k-1"
      Then the request is rejected as invalid with error "task.invalid-status-transition"

  # GAP-13: there is no in-flight lock; both concurrent requests execute.
  @gap
  Rule: Concurrent duplicates execute only once

    Scenario: Two simultaneous requests with the same key
      When Bob sends two identical requests to create a task "Write docs" on board "Sprint 1" with request key "k-1" at the same time
      Then board "Sprint 1" has exactly one task "Write docs"
      And neither response reports a server error
