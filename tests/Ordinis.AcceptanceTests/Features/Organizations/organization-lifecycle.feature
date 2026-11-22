@area-organizations
Feature: Organization suspension
  As an organization administrator
  I want to suspend and reactivate an organization
  So that a workspace can be frozen (e.g. for billing) without losing data

  Background:
    Given an organization "Acme" whose org Admin is Olivia
    And Nora is a user in "Acme"
    And an organization "Globex" with a user Mallory

  Rule: An active organization can be suspended, and a suspended one reactivated

    @smoke
    Scenario: Suspending an organization
      When Olivia suspends the organization "Acme"
      Then the request succeeds
      And the organization "Acme" is suspended

    Scenario: Reactivating an organization
      Given the organization "Acme" is suspended
      When Olivia reactivates the organization "Acme"
      Then the request succeeds
      And the organization "Acme" is active

    Scenario: Suspending a suspended organization is rejected
      Given the organization "Acme" is suspended
      When Olivia suspends the organization "Acme"
      Then the request is rejected as invalid with error "organization.already-suspended"

    Scenario: Reactivating an active organization is rejected
      When Olivia reactivates the organization "Acme"
      Then the request is rejected as invalid with error "organization.already-active"

    @role-matrix
    Scenario Outline: <persona> <action> the organization "Acme"
      Given the organization "Acme" is <state>
      When <persona> <action> the organization "Acme"
      Then <outcome>

      Examples:
        | persona | state     | action      | outcome                              |
        | Olivia  | active    | suspends    | the request succeeds                 |
        | Nora    | active    | suspends    | the request is rejected as forbidden |
        | Mallory | active    | suspends    | the request is rejected as not found |
        | Olivia  | suspended | reactivates | the request succeeds                 |
        | Nora    | suspended | reactivates | the request is rejected as forbidden |
        | Mallory | suspended | reactivates | the request is rejected as not found |

  Rule: A suspended organization is frozen

    Background:
      Given the organization "Acme" is suspended

    Scenario: No users can be added to a suspended organization
      When Olivia creates a user "Dan" with email "dan@acme.example" in "Acme"
      Then the request is rejected as invalid

    Scenario: A suspended organization cannot be renamed
      When Olivia renames the organization "Acme" to "Acme Corp"
      Then the request is rejected as invalid with error "organization.suspended"

    # GAP-07: project creation does not check suspension today.
    @gap
    Scenario: No projects can be created in a suspended organization
      When Nora creates a project "Hermes" in "Acme"
      Then the request is rejected as invalid on field "organizationId"

    Scenario: A suspended organization remains readable by its users
      When Nora views the organization "Acme"
      Then the request succeeds
      And the organization "Acme" is suspended

    @multi-tenant
    Scenario: Suspending one organization does not affect another
      When Mallory views the organization "Globex"
      Then the request succeeds
      And the organization "Globex" is active
