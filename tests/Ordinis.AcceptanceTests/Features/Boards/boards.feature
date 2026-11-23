@area-boards
Feature: Boards
  As a project contributor
  I want to organise a project's tasks on boards
  So that each sprint, team or stream of work has its own view

  Background:
    Given an organization "Acme" whose org Admin is Olivia
    And Nora is a user in "Acme"
    And an organization "Globex" with a user Mallory
    And a project "Apollo" in "Acme" with a board "Sprint 1"
    And Alice is an Admin of project "Apollo"
    And Bob is a Member of project "Apollo"
    And Victor is a Viewer of project "Apollo"

  Rule: Contributors can create boards with a name unique within the project

    @smoke
    Scenario: Creating a board
      When Bob creates a board "Sprint 2" in project "Apollo"
      Then the request succeeds
      And the response points to the new board
      And the boards of project "Apollo" are "Sprint 1, Sprint 2"

    Scenario Outline: Board names must be unique within the project: "<name>"
      When Bob creates a board "<name>" in project "Apollo"
      Then the request is rejected as invalid on field "name"

      Examples:
        | name     |
        | Sprint 1 |
        | sprint 1 |
        | SPRINT 1 |

    Scenario: The same board name may be used in another project
      Given a project "Hermes" in "Acme" where Bob is a Member
      When Bob creates a board "Sprint 1" in project "Hermes"
      Then the request succeeds

    # A request whose required field is absent cannot be bound at all, so it is malformed (400);
    # one that binds but breaks a rule is invalid (422).
    Scenario: A board without a name is rejected as malformed
      When Bob creates a board without a name in project "Apollo"
      Then the request is rejected as malformed

    Scenario Outline: Invalid board name: <case>
      When Bob creates a board <name> in project "Apollo"
      Then the request is rejected as invalid on field "name"

      Examples:
        | case               | name                        |
        | 101 characters     | with a 101-character name   |

    Scenario: The project must not be archived
      Given the project "Apollo" is archived
      When Bob creates a board "Sprint 2" in project "Apollo"
      Then the request is rejected as invalid on field "projectId"

    @role-matrix
    Scenario Outline: <persona> creating a board
      When <persona> creates a board "Sprint 2" in project "Apollo"
      Then <outcome>

      Examples:
        | persona | outcome                              |
        | Alice   | the request succeeds                 |
        | Bob     | the request succeeds                 |
        | Olivia  | the request succeeds                 |
        | Victor  | the request is rejected as forbidden |
        | Nora    | the request is rejected as not found |
        | Mallory | the request is rejected as not found |

  Rule: Board details show the board and its most recent tasks

    @smoke
    Scenario: Viewing a board
      Given a task "Fix login bug" on board "Sprint 1"
      When Victor views the board "Sprint 1"
      Then the request succeeds
      And the board details show name, project, archived flag, creation and update times
      And the board details list the task "Fix login bug"
      And the board details state the current version

    Scenario: At most the 50 most recent tasks are embedded
      Given 55 tasks on board "Sprint 1"
      When Victor views the board "Sprint 1"
      Then the board details list the 50 most recently created tasks

    Scenario: Viewing an unknown board
      When Victor views an unknown board
      Then the request is rejected as not found

    @role-matrix
    Scenario Outline: <persona> viewing a board
      When <persona> views the board "Sprint 1"
      Then <outcome>

      Examples:
        | persona | outcome                              |
        | Victor  | the request succeeds                 |
        | Olivia  | the request succeeds                 |
        | Nora    | the request is rejected as not found |
        | Mallory | the request is rejected as not found |

  Rule: Project Admins can rename a board

    Scenario: Renaming a board
      When Alice renames the board "Sprint 1" to "Sprint 1 – Auth"
      Then the request succeeds
      And the boards of project "Apollo" are "Sprint 1 – Auth"

    Scenario: Changing only the letter case of the name is allowed
      When Alice renames the board "Sprint 1" to "SPRINT 1"
      Then the request succeeds

    Scenario: Renaming to the name of another board is rejected
      Given project "Apollo" also has a board "Sprint 2"
      When Alice renames the board "Sprint 2" to "sprint 1"
      Then the request is rejected as invalid on field "newName"

    Scenario Outline: Invalid new name: <case>
      When Alice renames the board "Sprint 1" <name>
      Then the request is rejected as invalid on field "newName"

      Examples:
        | case           | name                        |
        | blank          | to a blank name             |
        | 101 characters | to a 101-character name     |

    Scenario: An archived board cannot be renamed
      Given the board "Sprint 1" is archived
      When Alice renames the board "Sprint 1" to "Old sprint"
      Then the request is rejected as invalid with error "board.archived"

    @role-matrix
    Scenario Outline: <persona> renaming a board
      When <persona> renames the board "Sprint 1" to "Sprint One"
      Then <outcome>

      Examples:
        | persona | outcome                              |
        | Alice   | the request succeeds                 |
        | Olivia  | the request succeeds                 |
        | Bob     | the request is rejected as forbidden |
        | Victor  | the request is rejected as forbidden |
        | Nora    | the request is rejected as not found |
        | Mallory | the request is rejected as not found |

  Rule: Project Admins can archive and unarchive a board

    Scenario: Archiving a board
      When Alice archives the board "Sprint 1"
      Then the request succeeds
      And the board "Sprint 1" is archived

    Scenario: Unarchiving a board
      Given the board "Sprint 1" is archived
      When Alice unarchives the board "Sprint 1"
      Then the request succeeds
      And the board "Sprint 1" is not archived

    Scenario: Archiving an archived board is rejected
      Given the board "Sprint 1" is archived
      When Alice archives the board "Sprint 1"
      Then the request is rejected as invalid with error "board.already-archived"

    Scenario: Unarchiving an active board is rejected
      When Alice unarchives the board "Sprint 1"
      Then the request is rejected as invalid with error "board.not-archived"

    Scenario: No new tasks can be created on an archived board
      Given the board "Sprint 1" is archived
      When Bob creates a task "Write docs" on board "Sprint 1"
      Then the request is rejected as invalid on field "boardId"

    # GAP-08 (fixed): an archived board or project makes its tasks read-only.
    Scenario: Tasks on an archived board cannot be moved
      Given a task "Fix login bug" on board "Sprint 1"
      And the board "Sprint 1" is archived
      When Bob moves the task "Fix login bug" to ToDo
      Then the request is rejected as invalid

    @role-matrix
    Scenario Outline: <persona> archiving a board
      When <persona> archives the board "Sprint 1"
      Then <outcome>

      Examples:
        | persona | outcome                              |
        | Alice   | the request succeeds                 |
        | Olivia  | the request succeeds                 |
        | Bob     | the request is rejected as forbidden |
        | Victor  | the request is rejected as forbidden |
        | Nora    | the request is rejected as not found |
        | Mallory | the request is rejected as not found |

  Rule: Board changes are guarded by optimistic concurrency

    Scenario Outline: <change> with a stale version is rejected
      Given Alice has read the board "Sprint 1"
      And Olivia has since renamed the board "Sprint 1" to "Sprint One"
      When Alice <change> using the version Alice read
      Then the request is rejected as conflicting

      Examples:
        | change                                        |
        | renames the board "Sprint One" to "Sprint 1a" |
        | archives the board "Sprint One"               |

    Scenario Outline: <change> without stating a version is rejected
      When Alice <change> without stating a version
      Then the request is rejected as invalid

      Examples:
        | change                                     |
        | renames the board "Sprint 1" to "Sprint 1a" |
        | archives the board "Sprint 1"              |
        | unarchives the board "Sprint 1"            |
