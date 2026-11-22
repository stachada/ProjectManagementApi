@area-projects
Feature: Project archiving and deletion
  As a project administrator
  I want to archive finished projects and delete abandoned ones
  So that active work stays uncluttered without losing history

  Background:
    Given an organization "Acme" whose org Admin is Olivia
    And Nora is a user in "Acme"
    And an organization "Globex" with a user Mallory
    And a project "Apollo" in "Acme" with a board "Sprint 1"
    And Alice is an Admin of project "Apollo"
    And Bob is a Member of project "Apollo"
    And Victor is a Viewer of project "Apollo"
    And a task "Fix login bug" on board "Sprint 1"

  Rule: An active project can be archived, and an archived one unarchived

    @smoke
    Scenario: Archiving a project
      When Alice archives the project "Apollo"
      Then the request succeeds
      And the project "Apollo" is archived

    Scenario: Unarchiving a project
      Given the project "Apollo" is archived
      When Alice unarchives the project "Apollo"
      Then the request succeeds
      And the project "Apollo" is not archived

    Scenario: Archiving an archived project is rejected
      Given the project "Apollo" is archived
      When Alice archives the project "Apollo"
      Then the request is rejected as invalid with error "project.already-archived"

    Scenario: Unarchiving an active project is rejected
      When Alice unarchives the project "Apollo"
      Then the request is rejected as invalid with error "project.not-archived"

    @role-matrix
    Scenario Outline: <persona> archiving the project
      When <persona> archives the project "Apollo"
      Then <outcome>

      Examples:
        | persona | outcome                              |
        | Alice   | the request succeeds                 |
        | Olivia  | the request succeeds                 |
        | Bob     | the request is rejected as forbidden |
        | Victor  | the request is rejected as forbidden |
        | Nora    | the request is rejected as not found |
        | Mallory | the request is rejected as not found |

    Scenario: Archiving with a stale version is rejected
      Given Alice has read the project "Apollo"
      And Olivia has since renamed the project "Apollo" to "Apollo Platform"
      When Alice archives the project "Apollo Platform" using the version Alice read
      Then the request is rejected as conflicting
      And the project "Apollo Platform" is not archived

    Scenario: Unarchiving without stating a version is rejected
      Given the project "Apollo" is archived
      When Alice unarchives the project "Apollo" without stating a version
      Then the request is rejected as invalid
      And the project "Apollo" is archived

  Rule: An archived project is read-only

    Background:
      Given the project "Apollo" is archived

    Scenario Outline: An archived project rejects changes: <change>
      When Alice <change>
      Then the request is rejected as invalid with error "project.archived"

      Examples:
        | change                                                           |
        | renames the project "Apollo" to "Apollo Platform"                |
        | adds Nora to project "Apollo" as a Member                        |
        | changes the role of Bob in project "Apollo" to Admin             |
        | removes Bob from project "Apollo"                                |
        | registers a webhook for project "Apollo" at "https://hooks.example.com/ordinis" |

    Scenario: Boards cannot be created in an archived project
      When Alice creates a board "Sprint 2" in project "Apollo"
      Then the request is rejected as invalid on field "projectId"

    # GAP-08: archived projects do not lock their tasks today.
    @gap
    Scenario Outline: Tasks of an archived project cannot be changed: <change>
      When Bob <change>
      Then the request is rejected as invalid

      Examples:
        | change                                                         |
        | creates a task "Write docs" on board "Sprint 1"                |
        | moves the task "Fix login bug" to ToDo                         |
        | assigns the task "Fix login bug" to Bob                        |
        | renames the task "Fix login bug" to "Fix login bug (iOS)"      |

    Scenario: An archived project remains readable
      When Victor views the project "Apollo"
      Then the request succeeds
      And the project "Apollo" is archived
      And Victor can still view the task "Fix login bug"

    Scenario: Archived projects are excluded from search
      When Olivia searches projects for "Apollo"
      Then no projects are found

  Rule: Deleting a project hides it and everything in it

    @smoke
    Scenario: Deleting a project
      When Alice deletes the project "Apollo"
      Then the request succeeds
      And the project "Apollo" is not found
      And the project "Apollo" is not listed for Olivia
      And the board "Sprint 1" is not found
      And the task "Fix login bug" is not found
      And the task "Fix login bug" is not listed among all tasks for Olivia

    Scenario: An archived project can be deleted
      Given the project "Apollo" is archived
      When Alice deletes the project "Apollo"
      Then the request succeeds

    Scenario: A deleted project cannot be deleted again
      Given Alice has deleted the project "Apollo"
      When Alice deletes the project "Apollo"
      Then the request is rejected as not found

    # GAP-15: the slug-uniqueness validator ignores soft-deleted projects, but the
    # unique (OrganizationId, Slug) index does not, so this fails with a server error.
    @gap
    Scenario: The name of a deleted project stays reserved
      Given Alice has deleted the project "Apollo"
      When Nora creates a project "Apollo" in "Acme"
      Then the request is rejected as invalid on field "name"

    @role-matrix
    Scenario Outline: <persona> deleting the project
      When <persona> deletes the project "Apollo"
      Then <outcome>

      Examples:
        | persona | outcome                              |
        | Alice   | the request succeeds                 |
        | Olivia  | the request succeeds                 |
        | Bob     | the request is rejected as forbidden |
        | Victor  | the request is rejected as forbidden |
        | Nora    | the request is rejected as not found |
        | Mallory | the request is rejected as not found |

    Scenario: Deleting with a stale version is rejected
      Given Alice has read the project "Apollo"
      And Olivia has since renamed the project "Apollo" to "Apollo Platform"
      When Alice deletes the project "Apollo Platform" using the version Alice read
      Then the request is rejected as conflicting
      And the project "Apollo Platform" exists

    Scenario: Deleting without stating a version is rejected
      When Alice deletes the project "Apollo" without stating a version
      Then the request is rejected as invalid
      And the project "Apollo" exists
