@area-organizations
Feature: Organizations
  As an organization administrator
  I want to create and maintain organizations
  So that each company using Ordinis has its own isolated workspace

  Every user belongs to exactly one organization (their tenant). Other
  organizations are invisible to them: any reference to one is "not found".

  Background:
    Given an organization "Acme" whose org Admin is Olivia
    And Nora is a user in "Acme"
    And an organization "Globex" with a user Mallory

  Rule: Org Admins can create organizations with a globally unique name

    @smoke
    Scenario: Creating an organization
      When Olivia creates an organization "Initech" described as "Software consultancy"
      Then the request succeeds
      And the response points to the new organization
      And the organization "Initech" has the slug "initech"
      And the organization "Initech" is active

    Scenario Outline: Organization names must be unique across all tenants: "<name>"
      When Olivia creates an organization "<name>"
      Then the request is rejected as invalid on field "name"

      Examples:
        | name   |
        | Acme   |
        | ACME   |
        | Globex |

    Scenario Outline: Invalid organization details: <case>
      When Olivia creates an organization <details>
      Then the request is rejected as invalid on field "<field>"

      Examples:
        | case                        | details                           | field       |
        | missing name                | without a name                    | name        |
        | 101-character name          | with a 101-character name         | name        |
        | 1,001-character description | with a 1001-character description | description |

    Scenario: A regular user cannot create organizations
      When Nora creates an organization "Initech"
      Then the request is rejected as forbidden

    Scenario: An anonymous caller cannot create organizations
      When an anonymous caller creates an organization "Initech"
      Then the request is rejected as unauthenticated

  Rule: Users can view their own organization only

    Scenario: Viewing one's own organization
      When Nora views the organization "Acme"
      Then the request succeeds
      And the organization details show name, slug, description, active flag, creation and update times

    @multi-tenant
    Scenario: Another organization is hidden
      When Mallory views the organization "Acme"
      Then the request is rejected as not found

    Scenario: Viewing an unknown organization
      When Nora views an unknown organization
      Then the request is rejected as not found

  Rule: An organization lists the projects its users can see

    Background:
      Given a project "Apollo" in "Acme" where Nora is a Member
      And a project "Hermes" in "Acme" where Olivia is an Admin
      And a project "Athena" in "Acme" where Nora is a Viewer

    Scenario: A user sees the organization projects they are a member of
      When Nora lists the projects of organization "Acme"
      Then the listed projects are "Apollo, Athena"

    Scenario: The org Admin sees every project of the organization
      When Olivia lists the projects of organization "Acme"
      Then the listed projects are "Apollo, Hermes, Athena"
      And the total count is 3

    Scenario: Filtering organization projects by member
      When Olivia lists the projects of organization "Acme" filtered by member Nora
      Then the listed projects are "Apollo, Athena"

    Scenario: Archived projects are hidden unless asked for
      Given the project "Athena" is archived
      When Olivia lists the projects of organization "Acme"
      Then the listed projects are "Apollo, Hermes"
      When Olivia lists the projects of organization "Acme" including archived ones
      Then the listed projects are "Apollo, Hermes, Athena"

    Scenario Outline: Sorting organization projects by <field> <direction>
      When Olivia lists the projects of organization "Acme" sorted by <field> <direction>
      Then the listed projects are "<projects>"

      Examples:
        | field     | direction  | projects               |
        | name      | ascending  | Apollo, Athena, Hermes |
        | name      | descending | Hermes, Athena, Apollo |
        | createdAt | ascending  | Apollo, Hermes, Athena |

    @multi-tenant
    Scenario: Projects of another organization cannot be listed
      When Mallory lists the projects of organization "Acme"
      Then the request is rejected as not found

  Rule: Org Admins can update their organization

    @smoke
    Scenario: Updating the organization
      When Olivia updates the organization "Acme" to name "Acme Corp" and description "Road runners welcome"
      Then the request succeeds
      And the organization "Acme Corp" has description "Road runners welcome"

    Scenario: Renaming keeps the original slug
      When Olivia renames the organization "Acme" to "Acme Corp"
      Then the organization "Acme Corp" has the slug "acme"

    Scenario Outline: Invalid update: <case>
      When Olivia updates the organization "Acme" <details>
      Then the request is rejected as invalid

      Examples:
        | case                        | details                           |
        | blank name                  | with a blank name                 |
        | 101-character name          | with a 101-character name         |
        | 1,001-character description | with a 1001-character description |

    # GAP-10 (fixed): renames are checked for name clashes like creation.
    @multi-tenant
    Scenario: Renaming to the name of another organization is rejected
      When Olivia renames the organization "Acme" to "Globex"
      Then the request is rejected as invalid

    Scenario: A suspended organization cannot be updated
      Given the organization "Acme" is suspended
      When Olivia renames the organization "Acme" to "Acme Corp"
      Then the request is rejected as invalid with error "organization.suspended"

    @role-matrix
    Scenario Outline: <persona> updating the organization "Acme"
      When <persona> renames the organization "Acme" to "Acme Corp"
      Then <outcome>

      Examples:
        | persona | outcome                              |
        | Olivia  | the request succeeds                 |
        | Nora    | the request is rejected as forbidden |
        | Mallory | the request is rejected as not found |

    # GAP-12 (fixed): users and organizations are versioned like every other aggregate
    # (ETag on GET, required If-Match on writes).
    Scenario: Updating with a stale version is rejected
      Given Olivia has read the organization "Acme"
      And another Acme admin has since renamed the organization "Acme" to "Acme Corp"
      When Olivia renames the organization "Acme Corp" to "Acme Inc" using the version Olivia read
      Then the request is rejected as conflicting
