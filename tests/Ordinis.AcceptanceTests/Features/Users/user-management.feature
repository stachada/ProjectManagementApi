@area-users
Feature: User management
  As an organization administrator
  I want to create users and manage their profile and organization role
  So that the right people can sign in with the right authority

  Organization roles: Admin (manages users and the organization, and has
  full authority over every project in it) and Member / Viewer (access only
  through project membership).

  Background:
    Given an organization "Acme" whose org Admin is Olivia
    And Nora is a user in "Acme"
    And Bob is a user in "Acme"
    And an organization "Globex" with a user Mallory

  Rule: Org Admins create users in their own organization

    @smoke
    Scenario: Creating a user
      When Olivia creates a user with:
        | display name | Dan Smith        |
        | email        | dan@acme.example |
        | password     | correct-horse-1  |
      Then the request succeeds
      And the response points to the new user
      And the user "Dan Smith" belongs to "Acme" with org role Member
      And the user "Dan Smith" is active
      And Dan can sign in to "Acme" with "dan@acme.example" and "correct-horse-1"

    Scenario Outline: Creating a user with org role <role>
      When Olivia creates a user "Dan Smith" with email "dan@acme.example" and org role <role>
      Then the request succeeds
      And the user "Dan Smith" belongs to "Acme" with org role <role>

      Examples:
        | role   |
        | Viewer |
        | Member |
        | Admin  |

    Scenario: Users are always created in the creator's own organization
      When Olivia creates a user "Dan Smith" with email "dan@acme.example"
      Then the user "Dan Smith" belongs to "Acme"

    Scenario: The password is never returned
      Given Olivia has created a user "Dan Smith" with email "dan@acme.example"
      When Olivia views the user "Dan Smith"
      Then the user details show display name, email, organization, org role, active flag, creation and update times
      And the user details contain no password or token

    Scenario Outline: Invalid user details: <case>
      When Olivia creates a user <details>
      Then the request is rejected as invalid on field "<field>"

      Examples:
        | case                      | details                               | field       |
        | missing display name      | without a display name                | displayName |
        | 101-character name        | with a 101-character display name     | displayName |
        | missing email             | without an email                      | email       |
        | malformed email           | with email "not-an-email"             | email       |
        | 255-character email       | with a 255-character email            | email       |
        | missing password          | without a password                    | password    |
        | 7-character password      | with password "short-1"               | password    |
        | unknown org role          | with an unknown org role              | orgRole     |

    Scenario: A password of exactly 8 characters is accepted
      When Olivia creates a user "Dan Smith" with email "dan@acme.example" and password "eight-ch"
      Then the request succeeds

    Scenario Outline: Emails are unique within the organization: <email>
      Given Olivia has created a user "Dan Smith" with email "dan@acme.example"
      When Olivia creates a user "Daniel Smith" with email "<email>"
      Then the request is rejected as invalid on field "email"

      Examples:
        | email            |
        | dan@acme.example |
        | DAN@ACME.EXAMPLE |

    @multi-tenant
    Scenario: The same email may exist in another organization
      Given Mallory's email is "mallory@shared.example"
      When Olivia creates a user "Mallory Twin" with email "mallory@shared.example"
      Then the request succeeds

    Scenario: Users cannot be created in a suspended organization
      Given the organization "Acme" is suspended
      When Olivia creates a user "Dan Smith" with email "dan@acme.example"
      Then the request is rejected as invalid

    Scenario: A regular user cannot create users
      When Nora creates a user "Dan Smith" with email "dan@acme.example"
      Then the request is rejected as forbidden

    Scenario: An anonymous caller cannot create users
      When an anonymous caller creates a user "Dan Smith" with email "dan@acme.example"
      Then the request is rejected as unauthenticated

  Rule: Users can see the other users of their organization only

    Scenario: Viewing a colleague
      When Nora views the user Bob
      Then the request succeeds

    @multi-tenant
    Scenario: Users of another organization are hidden
      When Mallory views the user Bob
      Then the request is rejected as not found

    Scenario: Viewing an unknown user
      When Nora views an unknown user
      Then the request is rejected as not found

  Rule: Users can change their own display name; Org Admins can change anyone's

    @smoke
    Scenario: Changing one's own display name
      When Nora changes the display name of Nora to "Nora Jones"
      Then the request succeeds
      And the user Nora has display name "Nora Jones"

    @role-matrix
    Scenario Outline: <persona> changing the display name of Bob
      When <persona> changes the display name of Bob to "Robert"
      Then <outcome>

      Examples:
        | persona | outcome                              |
        | Bob     | the request succeeds                 |
        | Olivia  | the request succeeds                 |
        | Nora    | the request is rejected as forbidden |
        | Mallory | the request is rejected as not found |

    Scenario Outline: Invalid display name: <case>
      When Nora changes the display name of Nora to <name>
      Then the request is rejected as invalid on field "displayName"

      Examples:
        | case           | name                  |
        | blank          | a blank name          |
        | 101 characters | a 101-character name  |

    Scenario: The new display name appears on assigned tasks
      Given a project "Apollo" in "Acme" with a board "Sprint 1"
      And Bob is a Member of project "Apollo"
      And a task "Fix login bug" on board "Sprint 1" assigned to Bob
      When Bob changes the display name of Bob to "Robert"
      Then the task "Fix login bug" shows the assignee name "Robert"

    # GAP-12: users expose no version and accept writes without one.
    @gap
    Scenario: Updating with a stale version is rejected
      Given Olivia has read the user Bob
      And Bob has since changed the display name of Bob to "Robert"
      When Olivia changes the display name of Bob to "Bobby" using the version Olivia read
      Then the request is rejected as conflicting
      And the user Bob has display name "Robert"

  Rule: Org Admins change organization roles, but an organization always keeps an Admin

    Scenario: Promoting a user to org Admin
      When Olivia changes the org role of Nora to Admin
      Then the request succeeds
      And the user Nora has org role Admin

    Scenario: A promotion takes effect from the user's next session refresh
      Given Olivia has changed the org role of Nora to Admin
      When Nora refreshes the session
      And Nora creates a user "Dan Smith" with email "dan@acme.example"
      Then the request succeeds

    Scenario: The new org role must be a known role
      When Olivia changes the org role of Nora to an unknown role
      Then the request is rejected as invalid on field "newOrgRole"

    # GAP-05: nothing prevents removing the last org Admin today.
    @gap
    Scenario: The last org Admin cannot be demoted
      When Olivia changes the org role of Olivia to Member
      Then the request is rejected as invalid
      And the user Olivia has org role Admin

    Scenario: An org Admin can step down when another org Admin exists
      Given Olivia has changed the org role of Nora to Admin
      When Olivia changes the org role of Olivia to Member
      Then the request succeeds

    @role-matrix
    Scenario Outline: <persona> changing the org role of Bob
      When <persona> changes the org role of Bob to Viewer
      Then <outcome>

      Examples:
        | persona | outcome                              |
        | Olivia  | the request succeeds                 |
        | Bob     | the request is rejected as forbidden |
        | Nora    | the request is rejected as forbidden |
        | Mallory | the request is rejected as not found |
