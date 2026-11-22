@area-projects
Feature: Project membership
  As a project administrator
  I want to control who belongs to a project and with which role
  So that people get exactly the access they need

  Project roles, from least to most privileged:
    Viewer — read everything in the project
    Member — additionally create and change boards, tasks, comments and attachments
    Admin  — additionally manage the project, its members, boards and webhooks

  Background:
    Given an organization "Acme" whose org Admin is Olivia
    And Nora is a user in "Acme"
    And an organization "Globex" with a user Mallory
    And a project "Apollo" in "Acme" with a board "Sprint 1"
    And Alice is an Admin of project "Apollo"
    And Bob is a Member of project "Apollo"
    And Victor is a Viewer of project "Apollo"

  Rule: Any member can see who belongs to the project

    Scenario: Listing the members in the order they joined
      When Victor lists the members of project "Apollo"
      Then the request succeeds
      And the members of project "Apollo" are, in joining order:
        | member | role   |
        | Alice  | Admin  |
        | Bob    | Member |
        | Victor | Viewer |

    @role-matrix
    Scenario Outline: <persona> listing the members
      When <persona> lists the members of project "Apollo"
      Then <outcome>

      Examples:
        | persona | outcome                              |
        | Victor  | the request succeeds                 |
        | Olivia  | the request succeeds                 |
        | Nora    | the request is rejected as not found |
        | Mallory | the request is rejected as not found |

  Rule: Project Admins add members with a role

    @smoke
    Scenario Outline: Adding Nora as a <role>
      When Alice adds Nora to project "Apollo" as a <role>
      Then the request succeeds
      And the response points to the members of project "Apollo"
      And Nora is a <role> of project "Apollo"

      Examples:
        | role   |
        | Viewer |
        | Member |
        | Admin  |

    Scenario: A new member gains access to the project
      Given Alice has added Nora to project "Apollo" as a Viewer
      When Nora views the project "Apollo"
      Then the request succeeds

    Scenario: Adding an existing member is rejected
      When Alice adds Bob to project "Apollo" as an Admin
      Then the request is rejected as invalid
      And Bob is a Member of project "Apollo"

    Scenario: The user must exist
      When Alice adds an unknown user to project "Apollo" as a Member
      Then the request is rejected as invalid on field "userId"

    Scenario: The role must be a known role
      When Alice adds Nora to project "Apollo" with an unknown role
      Then the request is rejected as invalid on field "role"

    # GAP-03 (fixed): users must belong to the project's organization and be able to work on it.
    @multi-tenant
    Scenario: Users from another organization cannot be added
      When Alice adds Mallory to project "Apollo" as a Member
      Then the request is rejected as invalid on field "userId"

    @role-matrix
    Scenario Outline: <persona> adding a member
      When <persona> adds Nora to project "Apollo" as a Member
      Then <outcome>

      Examples:
        | persona | outcome                              |
        | Alice   | the request succeeds                 |
        | Olivia  | the request succeeds                 |
        | Bob     | the request is rejected as forbidden |
        | Victor  | the request is rejected as forbidden |
        | Mallory | the request is rejected as not found |

  Rule: Project Admins change member roles, but a project always keeps an Admin

    Scenario Outline: Changing the role of Bob to <role>
      When Alice changes the role of Bob in project "Apollo" to <role>
      Then the request succeeds
      And Bob is a <role> of project "Apollo"

      Examples:
        | role   |
        | Viewer |
        | Admin  |

    Scenario: A promoted Viewer can start contributing
      Given Alice has changed the role of Victor in project "Apollo" to Member
      When Victor creates a task "Write docs" on board "Sprint 1"
      Then the request succeeds

    Scenario: A demoted Member can no longer contribute
      Given Alice has changed the role of Bob in project "Apollo" to Viewer
      When Bob creates a task "Write docs" on board "Sprint 1"
      Then the request is rejected as invalid

    Scenario: The last Admin cannot be demoted
      When Alice changes the role of Alice in project "Apollo" to Member
      Then the request is rejected as invalid with error "project.last-admin-demotion"
      And Alice is an Admin of project "Apollo"

    Scenario: An Admin can step down when another Admin exists
      Given Alice has changed the role of Bob in project "Apollo" to Admin
      When Alice changes the role of Alice in project "Apollo" to Member
      Then the request succeeds

    # GAP-09: reported as 422 "project.member-not-found" today.
    @gap
    Scenario: Changing the role of a non-member is rejected as not found
      When Alice changes the role of Nora in project "Apollo" to Member
      Then the request is rejected as not found

    Scenario: The new role must be a known role
      When Alice changes the role of Bob in project "Apollo" to an unknown role
      Then the request is rejected as invalid on field "newRole"

    @role-matrix
    Scenario Outline: <persona> changing a member's role
      When <persona> changes the role of Victor in project "Apollo" to Member
      Then <outcome>

      Examples:
        | persona | outcome                              |
        | Alice   | the request succeeds                 |
        | Olivia  | the request succeeds                 |
        | Bob     | the request is rejected as forbidden |
        | Victor  | the request is rejected as forbidden |
        | Nora    | the request is rejected as not found |

  Rule: Project Admins remove members, but never the last Admin

    @smoke
    Scenario: Removing a member revokes access
      When Alice removes Bob from project "Apollo"
      Then the request succeeds
      And Bob is not a member of project "Apollo"
      And Bob can no longer view the project "Apollo"

    Scenario: The last Admin cannot be removed
      When Olivia removes Alice from project "Apollo"
      Then the request is rejected as invalid with error "project.last-admin-removal"
      And Alice is an Admin of project "Apollo"

    Scenario: An Admin can be removed when another Admin exists
      Given Alice has changed the role of Bob in project "Apollo" to Admin
      When Bob removes Alice from project "Apollo"
      Then the request succeeds

    # GAP-09: reported as 422 "project.member-not-found" today.
    @gap
    Scenario: Removing a non-member is rejected as not found
      When Alice removes Nora from project "Apollo"
      Then the request is rejected as not found

    Scenario: A removed member can be added again
      Given Alice has removed Bob from project "Apollo"
      When Alice adds Bob to project "Apollo" as a Viewer
      Then the request succeeds
      And Bob is a Viewer of project "Apollo"

    @role-matrix
    Scenario Outline: <persona> removing a member
      When <persona> removes Victor from project "Apollo"
      Then <outcome>

      Examples:
        | persona | outcome                              |
        | Alice   | the request succeeds                 |
        | Olivia  | the request succeeds                 |
        | Bob     | the request is rejected as forbidden |
        | Victor  | the request is rejected as forbidden |
        | Nora    | the request is rejected as not found |

  Rule: Membership changes are guarded by the project's optimistic concurrency

    Scenario Outline: <change> with a stale project version is rejected
      Given Alice has read the project "Apollo"
      And Olivia has since renamed the project "Apollo" to "Apollo Platform"
      When Alice <change> using the version Alice read
      Then the request is rejected as conflicting

      Examples:
        | change                                                         |
        | adds Nora to project "Apollo Platform" as a Member             |
        | changes the role of Bob in project "Apollo Platform" to Admin  |
        | removes Bob from project "Apollo Platform"                     |

    Scenario Outline: <change> without stating a version is rejected
      When Alice <change> without stating a version
      Then the request is rejected as invalid

      Examples:
        | change                                                 |
        | adds Nora to project "Apollo" as a Member              |
        | changes the role of Bob in project "Apollo" to Admin   |
        | removes Bob from project "Apollo"                      |
