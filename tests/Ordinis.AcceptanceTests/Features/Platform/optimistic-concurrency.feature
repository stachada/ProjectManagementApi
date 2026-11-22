@area-platform
Feature: Optimistic concurrency
  As an API client editing shared data
  I want my change rejected if someone else changed the resource since I read it
  So that I never silently overwrite another person's work

  Readers receive the resource's current version (an ETag). Writers state
  the version their change is based on (If-Match). A stale version is a
  conflict; a missing version is an invalid request.

  Background:
    Given an organization "Acme" whose org Admin is Olivia
    And a project "Apollo" in "Acme" with a board "Sprint 1"
    And Alice is an Admin of project "Apollo"
    And Bob is a Member of project "Apollo"
    And a task "Fix login bug" on board "Sprint 1"

  Rule: Versioned resources state their current version when read

    Scenario Outline: Reading <resource> returns its version
      When Bob views <resource>
      Then the response states the current version of <resource>
      And the stated version matches the version in the body

      Examples:
        | resource                            |
        | the task "Fix login bug"            |
        | the task "Fix login bug" in API v2  |
        | the project "Apollo"                |
        | the board "Sprint 1"                |

    Scenario: The version changes with every successful write
      Given Bob has read the task "Fix login bug"
      When Bob moves the task "Fix login bug" to ToDo
      Then the current version of the task "Fix login bug" differs from the one Bob read

    Scenario: A rejected write does not change the version
      Given Bob has read the task "Fix login bug"
      When Bob moves the task "Fix login bug" to Done
      Then the request is rejected as invalid
      And the current version of the task "Fix login bug" equals the one Bob read

    Scenario: Changing a task's comments changes the task's version
      Given Bob has read the task "Fix login bug"
      When Bob comments "On it" on the task "Fix login bug"
      Then the current version of the task "Fix login bug" differs from the one Bob read

    Scenario: Changing project membership changes the project's version
      Given Alice has read the project "Apollo"
      When Alice adds Olivia to project "Apollo" as a Viewer
      Then the current version of the project "Apollo" differs from the one Alice read

  Rule: A write based on a stale version is rejected as a conflict

    @smoke
    Scenario: Lost update is prevented
      Given Alice has read the task "Fix login bug"
      And Bob has read the task "Fix login bug"
      And Alice has renamed the task "Fix login bug" to "Fix login (Alice)" using the version Alice read
      When Bob renames the task "Fix login (Alice)" to "Fix login (Bob)" using the version Bob read
      Then the request is rejected as conflicting
      And the error explains that the task was changed concurrently
      And the task "Fix login (Alice)" exists

    Scenario: Re-reading and retrying succeeds
      Given Bob's change to the task "Fix login bug" was rejected as conflicting
      When Bob reads the task "Fix login bug" again
      And Bob renames the task "Fix login bug" to "Fix login (Bob)" using the version Bob read
      Then the request succeeds

    Scenario: Of two simultaneous writers with the same version, exactly one wins
      Given Alice and Bob have both read the task "Fix login bug"
      When Alice and Bob simultaneously move the task "Fix login bug" to ToDo and Cancelled using the versions they read
      Then exactly one request succeeds
      And the other is rejected as conflicting

  Rule: Versioned writes must state a version

    Scenario Outline: A write without a version is rejected: <write>
      When <write> without stating a version
      Then the request is rejected as invalid
      And the error says a version (If-Match) is required

      Examples:
        | write                                              |
        | Bob renames the task "Fix login bug" to "X"        |
        | Bob moves the task "Fix login bug" to ToDo         |
        | Bob assigns the task "Fix login bug" to Bob        |
        | Alice deletes the task "Fix login bug"             |
        | Alice renames the project "Apollo" to "X"          |
        | Alice archives the project "Apollo"                |
        | Alice adds Olivia to project "Apollo" as a Viewer  |
        | Alice renames the board "Sprint 1" to "X"          |
        | Alice archives the board "Sprint 1"                |

    Scenario: A malformed version is treated as missing
      When Bob moves the task "Fix login bug" to ToDo stating the version "not-base64!"
      Then the request is rejected as invalid

    Scenario Outline: The version is accepted in any standard ETag notation: <notation>
      Given Bob has read the task "Fix login bug"
      When Bob moves the task "Fix login bug" to ToDo stating the version read as <notation>
      Then the request succeeds

      Examples:
        | notation              |
        | a quoted strong ETag  |
        | a weak ETag (W/"…")   |
        | the bare token        |

  Rule: Append-only and creation requests need no version

    Scenario Outline: <write> succeeds without a version
      When <write> without stating a version
      Then the request succeeds

      Examples:
        | write                                                     |
        | Bob creates a task "Write docs" on board "Sprint 1"       |
        | Bob comments "On it" on the task "Fix login bug"          |
        | Bob uploads "crash.log" to the task "Fix login bug"       |
        | Bob creates a board "Sprint 2" in project "Apollo"        |

  # GAP-12 (fixed): users and organizations are versioned like every other aggregate
  # (ETag on GET, required If-Match on writes).
  Rule: Users and organizations are versioned like every other aggregate

    Scenario Outline: Reading <resource> as org Admin returns its version
      When Olivia views <resource>
      Then the response states the current version of <resource>

      Examples:
        | resource                    |
        | the organization "Acme"     |
        | the user Bob                |

    Scenario Outline: Writing <resource> without a version is rejected
      When Olivia <write> without stating a version
      Then the request is rejected as invalid

      Examples:
        | resource     | write                                      |
        | organization | renames the organization "Acme" to "Acme 2" |
        | user         | changes the display name of Bob to "Robert" |
