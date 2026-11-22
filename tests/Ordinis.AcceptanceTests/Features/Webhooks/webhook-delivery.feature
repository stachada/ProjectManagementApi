@area-webhooks @background
Feature: Webhook delivery
  As an integrator receiving Ordinis webhooks
  I want every subscribed event delivered reliably, retried on failure and never sent to internal hosts
  So that my system can trust the notifications it receives

  Changes and their events are committed together (transactional outbox);
  background workers then fan events out to subscribed webhooks and deliver
  them. Delivery is at-least-once: receivers should de-duplicate.

  Background:
    Given an organization "Acme"
    And a project "Apollo" in "Acme" with a board "Sprint 1"
    And Alice is an Admin of project "Apollo"
    And Bob is a Member of project "Apollo"
    And a receiver "Chat" listening at "https://chat.example.com/hooks/ordinis"

  Rule: Subscribed events are delivered as a JSON envelope

    @smoke
    Scenario Outline: Delivering "<event>"
      Given project "Apollo" has a webhook at receiver "Chat" for "<event>"
      And a task "Fix login bug" on board "Sprint 1"
      When Bob <action>
      Then receiver "Chat" eventually receives one POST with content type "application/json"
      And the payload has event "<event>", the time it occurred, and data about "Fix login bug"

      Examples:
        | event         | action                                            |
        | task.created  | creates a task "Fix login bug" on board "Sprint 1" |
        | task.moved    | moves the task "Fix login bug" to ToDo            |
        | task.assigned | assigns the task "Fix login bug" to Bob           |
        | comment.added | comments "On it" on the task "Fix login bug"      |

    Scenario: Events the webhook did not subscribe to are not delivered
      Given project "Apollo" has a webhook at receiver "Chat" for "task.moved"
      And a task "Fix login bug" on board "Sprint 1"
      When Bob assigns the task "Fix login bug" to Bob
      And Bob moves the task "Fix login bug" to ToDo
      Then receiver "Chat" eventually receives exactly one event, "task.moved"

    Scenario: A webhook with no event filter receives every event type
      Given project "Apollo" has a webhook at receiver "Chat" for all events
      When Bob creates a task "Fix login bug" on board "Sprint 1"
      And Bob comments "On it" on the task "Fix login bug"
      Then receiver "Chat" eventually receives "task.created" and "comment.added"

    Scenario: Events without a webhook type are not delivered
      Given project "Apollo" has a webhook at receiver "Chat" for all events
      And a task "Fix login bug" on board "Sprint 1"
      When Bob renames the task "Fix login bug" to "Fix login bug (iOS)"
      And Alice deletes the task "Fix login bug (iOS)"
      Then receiver "Chat" receives nothing within the delivery window

    Scenario: Every matching webhook gets its own delivery
      Given a receiver "CI" listening at "https://ci.example.com/hooks"
      And project "Apollo" has a webhook at receiver "Chat" for "task.created"
      And project "Apollo" has a webhook at receiver "CI" for "task.created"
      When Bob creates a task "Fix login bug" on board "Sprint 1"
      Then receivers "Chat" and "CI" each eventually receive one "task.created" event

    Scenario: Webhooks of other projects are not notified
      Given a project "Hermes" in "Acme" with a board "Inbox"
      And project "Hermes" has a webhook at receiver "Chat" for all events
      When Bob creates a task "Fix login bug" on board "Sprint 1"
      Then receiver "Chat" receives nothing within the delivery window

    Scenario: A change that is rolled back sends no event
      Given project "Apollo" has a webhook at receiver "Chat" for "task.moved"
      And a task "Fix login bug" on board "Sprint 1"
      When Bob moves the task "Fix login bug" to Done
      Then the request is rejected as invalid
      And receiver "Chat" receives nothing within the delivery window

  Rule: Failed deliveries are retried with exponential backoff, then given up

    Background:
      Given project "Apollo" has a webhook at receiver "Chat" for "task.created"

    @needs-clock
    Scenario: A delivery that eventually succeeds
      Given receiver "Chat" responds with 503 to the first 2 requests
      When Bob creates a task "Fix login bug" on board "Sprint 1"
      Then receiver "Chat" receives attempt 1 immediately
      And receiver "Chat" receives attempt 2 about 30 seconds later
      And receiver "Chat" receives attempt 3 about 60 seconds after that
      And the delivery is recorded as delivered after 3 attempts

    @needs-clock
    Scenario: A delivery that keeps failing is given up after 3 attempts
      Given receiver "Chat" always responds with 500
      When Bob creates a task "Fix login bug" on board "Sprint 1"
      Then receiver "Chat" eventually receives exactly 3 attempts
      And the delivery is recorded as failed with the last error

    Scenario Outline: A <response> counts as a failure
      Given receiver "Chat" responds to the first request with <response>
      When Bob creates a task "Fix login bug" on board "Sprint 1"
      Then the delivery is retried

      Examples:
        | response                          |
        | status 500                        |
        | status 404                        |
        | no answer within 10 seconds       |
        | a refused connection              |

      # GAP-17 (fixed): the delivery client never follows redirects.
      Examples: Redirects are not followed
        | response                          |
        | status 301 redirect               |
        | status 302 redirect               |

    Scenario: Every attempt sends exactly the same payload
      Given receiver "Chat" responds with 503 to the first request
      When Bob creates a task "Fix login bug" on board "Sprint 1"
      Then both attempts received by "Chat" have byte-identical bodies

    Scenario: A slow receiver does not delay other webhooks
      Given a receiver "CI" listening at "https://ci.example.com/hooks"
      And project "Apollo" has a webhook at receiver "CI" for "task.created"
      And receiver "Chat" never answers
      When Bob creates a task "Fix login bug" on board "Sprint 1"
      Then receiver "CI" eventually receives one "task.created" event

  Rule: The destination is re-checked before every attempt

    Scenario: A host that now resolves to an internal address is not called
      Given project "Apollo" has a webhook at "https://rebind.example.com/hook" for "task.created"
      And the host "rebind.example.com" now resolves to 127.0.0.1
      When Bob creates a task "Fix login bug" on board "Sprint 1"
      Then no request is sent to 127.0.0.1
      And the delivery attempt is recorded as failed because the destination is not allowed

    # GAP-17 (fixed): the URL guard only vets the registered host, so the delivery
    # client must never follow redirects towards internal addresses.
    Scenario Outline: A redirect to an internal address is not followed: <target>
      Given project "Apollo" has a webhook at receiver "Chat" for "task.created"
      And receiver "Chat" redirects every request to "<target>"
      When Bob creates a task "Fix login bug" on board "Sprint 1"
      Then no request is sent to "<target>"
      And the delivery attempt is recorded as failed

      Examples:
        | target                                   |
        | http://169.254.169.254/latest/meta-data  |
        | http://127.0.0.1:5000/health             |
        | http://10.0.0.5/admin                    |

  Rule: Event processing survives restarts and parallel workers

    Scenario: Events committed before a restart are still delivered
      Given project "Apollo" has a webhook at receiver "Chat" for "task.created"
      And the background workers are stopped
      When Bob creates a task "Fix login bug" on board "Sprint 1"
      And the background workers are started again
      Then receiver "Chat" eventually receives one "task.created" event

    Scenario: Two API instances never deliver the same event twice concurrently
      Given two API instances share the database
      And project "Apollo" has a webhook at receiver "Chat" for "task.created"
      When Bob creates 20 tasks on board "Sprint 1"
      Then receiver "Chat" eventually receives 20 "task.created" events, one per task
