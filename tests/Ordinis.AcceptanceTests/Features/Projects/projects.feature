@area-projects
Feature: Projects
  As a member of an organization
  I want to create, browse and maintain projects
  So that work is grouped by the product or initiative it belongs to

  Background:
    Given an organization "Acme" whose org Admin is Olivia
    And Nora is a user in "Acme"
    And an organization "Globex" with a user Mallory
    And a project "Apollo" in "Acme" with a board "Sprint 1"
    And Alice is an Admin of project "Apollo"
    And Bob is a Member of project "Apollo"
    And Victor is a Viewer of project "Apollo"

  Rule: Any user of an organization can create a project in it and becomes its first Admin

    @smoke
    Scenario: Creating a project
      When Nora creates a project "Hermes" in "Acme" described as "Customer messaging"
      Then the request succeeds
      And the response points to the new project
      And the project "Hermes" has the slug "hermes"
      And the members of project "Hermes" are:
        | member | role  |
        | Nora   | Admin |

    Scenario: The description is optional
      When Nora creates a project "Hermes" in "Acme"
      Then the request succeeds
      And the project "Hermes" has no description

    Scenario Outline: Invalid project details: <case>
      When Nora creates a project in "Acme" <details>
      Then the request is rejected as invalid on field "<field>"

      Examples:
        | case                          | details                             | field       |
        | missing name                  | without a name                      | name        |
        | 101-character name            | with a 101-character name           | name        |
        | 1,001-character description   | with a 1001-character description   | description |

    Scenario: A name of exactly 100 characters is accepted
      When Nora creates a project in "Acme" with a 100-character name
      Then the request succeeds

    Scenario Outline: Project names must be unique within the organization: "<name>"
      When Nora creates a project "<name>" in "Acme"
      Then the request is rejected as invalid on field "name"

      Examples:
        | name    |
        | Apollo  |
        | apollo  |
        | APOLLO  |

    @multi-tenant
    Scenario: The same name may be used in another organization
      When Mallory creates a project "Apollo" in "Globex"
      Then the request succeeds

    Scenario: The organization must exist
      When Nora creates a project "Hermes" in an unknown organization
      Then the request is rejected as invalid on field "organizationId"

    @multi-tenant
    Scenario: Users cannot create projects in an organization they do not belong to
      When Mallory creates a project "Trojan" in "Acme"
      Then the request is rejected as invalid

    # GAP-07 (fixed): a suspended organization gets no new projects.
    Scenario: Projects cannot be created in a suspended organization
      Given the organization "Acme" is suspended
      When Nora creates a project "Hermes" in "Acme"
      Then the request is rejected as invalid on field "organizationId"

    Scenario: An anonymous caller cannot create a project
      When an anonymous caller creates a project "Hermes" in "Acme"
      Then the request is rejected as unauthenticated

  Rule: Project details include boards, members and navigation links

    @smoke
    Scenario: Viewing a project
      Given project "Apollo" also has a board "Sprint 2"
      When Victor views the project "Apollo"
      Then the request succeeds
      And the project details show name, slug, description, archived flag, creation and update times
      And the project details list the boards "Sprint 1, Sprint 2"
      And the project details list the members Alice, Bob and Victor with their roles
      And the project details state the current version
      And the project has the links:
        | rel     | method |
        | self    | GET    |
        | tasks   | GET    |
        | boards  | GET    |
        | members | GET    |
        | audit   | GET    |
        | delete  | DELETE |

    Scenario: Listing the boards of a project
      Given project "Apollo" also has a board "Sprint 2"
      When Victor lists the boards of project "Apollo"
      Then the listed boards are "Sprint 1, Sprint 2"

    Scenario: At most 100 boards are embedded in the project details
      Given project "Apollo" has 105 boards in total
      When Victor views the project "Apollo"
      Then the project details list 100 boards

    @role-matrix
    Scenario Outline: <persona> viewing the project
      When <persona> views the project "Apollo"
      Then <outcome>

      Examples:
        | persona | outcome                              |
        | Alice   | the request succeeds                 |
        | Bob     | the request succeeds                 |
        | Victor  | the request succeeds                 |
        | Olivia  | the request succeeds                 |
        | Nora    | the request is rejected as not found |
        | Mallory | the request is rejected as not found |

  Rule: Project lists only contain projects the caller can see

    Background:
      Given a project "Hermes" in "Acme" where Nora is an Admin
      And a project "Zeus" in "Globex" where Mallory is an Admin

    Scenario: A user sees the projects they are a member of
      When Bob lists the projects
      Then the listed projects are "Apollo"

    Scenario: The org Admin sees every project of the organization
      When Olivia lists the projects
      Then the listed projects are "Apollo, Hermes"

    Scenario: Filtering projects by member
      When Olivia lists the projects filtered by member Nora
      Then the listed projects are "Hermes"

    @multi-tenant
    Scenario: Filtering by another organization reveals nothing
      When Olivia lists the projects filtered by organization "Globex"
      Then no projects are listed

    Scenario: Archived projects are hidden unless asked for
      Given the project "Hermes" is archived
      When Olivia lists the projects
      Then the listed projects are "Apollo"
      When Olivia lists the projects including archived ones
      Then the listed projects are "Apollo, Hermes"

    Scenario Outline: Sorting projects by <field> <direction>
      When Olivia lists the projects sorted by <field> <direction>
      Then the listed projects are "<projects>"

      Examples:
        | field     | direction  | projects       |
        | name      | ascending  | Apollo, Hermes |
        | name      | descending | Hermes, Apollo |
        | createdAt | ascending  | Apollo, Hermes |

    Scenario: A list entry summarises the project
      When Bob lists the projects
      Then the entry for "Apollo" shows its name, slug, organization and archived flag

  Rule: Project Admins can rename and re-describe a project

    @smoke
    Scenario: Updating a project
      When Alice updates the project "Apollo" to name "Apollo Platform" and description "Core platform"
      Then the request succeeds
      And the project "Apollo Platform" has description "Core platform"

    Scenario: Renaming keeps the original slug
      When Alice renames the project "Apollo" to "Apollo Platform"
      Then the project "Apollo Platform" has the slug "apollo"

    Scenario Outline: Invalid update: <case>
      When Alice updates the project "Apollo" <details>
      Then the request is rejected as invalid

      Examples:
        | case                        | details                              |
        | blank name                  | with a blank name                    |
        | 101-character name          | with a 101-character name            |
        | 1,001-character description | with a 1001-character description    |

    # GAP-10 (fixed): renames are checked for name clashes like creation.
    Scenario: Renaming to the name of another project is rejected
      Given a project "Hermes" in "Acme" where Alice is an Admin
      When Alice renames the project "Hermes" to "Apollo"
      Then the request is rejected as invalid on field "newName"

    @role-matrix
    Scenario Outline: <persona> updating the project
      When <persona> renames the project "Apollo" to "Apollo Platform"
      Then <outcome>

      Examples:
        | persona | outcome                              |
        | Alice   | the request succeeds                 |
        | Olivia  | the request succeeds                 |
        | Bob     | the request is rejected as forbidden |
        | Victor  | the request is rejected as forbidden |
        | Nora    | the request is rejected as not found |
        | Mallory | the request is rejected as not found |

    Scenario: Updating with a stale version is rejected
      Given Alice has read the project "Apollo"
      And Olivia has since renamed the project "Apollo" to "Apollo (Olivia)"
      When Alice renames the project "Apollo (Olivia)" to "Apollo (Alice)" using the version Alice read
      Then the request is rejected as conflicting
      And the project "Apollo (Olivia)" exists

    Scenario: Updating without stating a version is rejected
      When Alice renames the project "Apollo" to "Apollo Platform" without stating a version
      Then the request is rejected as invalid
