@area-users
Feature: User deactivation
  As an organization administrator
  I want to deactivate users who leave and reactivate them if they return
  So that former staff lose access without their history being lost

  Background:
    Given an organization "Acme" whose org Admin is Olivia
    And Nora is a user in "Acme" with password "nora-secret-1"
    And an organization "Globex" with a user Mallory

  Rule: An active user can be deactivated, and an inactive one reactivated

    @smoke
    Scenario: Deactivating a user
      When Olivia deactivates the user Nora
      Then the request succeeds
      And the user Nora is inactive

    Scenario: Reactivating a user
      Given Olivia has deactivated the user Nora
      When Olivia reactivates the user Nora
      Then the request succeeds
      And the user Nora is active

    Scenario: Deactivating an inactive user is rejected
      Given Olivia has deactivated the user Nora
      When Olivia deactivates the user Nora
      Then the request is rejected as invalid with error "user.already-inactive"

    Scenario: Reactivating an active user is rejected
      When Olivia reactivates the user Nora
      Then the request is rejected as invalid with error "user.already-active"

    @role-matrix
    Scenario Outline: <persona> deactivating the user Nora
      When <persona> deactivates the user Nora
      Then <outcome>

      Examples:
        | persona | outcome                              |
        | Olivia  | the request succeeds                 |
        | Nora    | the request is rejected as forbidden |
        | Mallory | the request is rejected as not found |

    @role-matrix
    Scenario Outline: <persona> reactivating the user Nora
      Given Olivia has deactivated the user Nora
      When <persona> reactivates the user Nora
      Then <outcome>

      Examples:
        | persona | outcome                              |
        | Olivia  | the request succeeds                 |
        | Mallory | the request is rejected as not found |

    # GAP-05 (fixed): an organization always keeps an active Admin.
    Scenario: The last org Admin cannot deactivate themselves
      When Olivia deactivates the user Olivia
      Then the request is rejected as invalid
      And the user Olivia is active

  Rule: An inactive user loses access

    Background:
      Given Nora has signed in to "Acme"
      And Olivia has deactivated the user Nora

    Scenario: An inactive user cannot sign in
      When Nora signs in to "Acme" with password "nora-secret-1"
      Then the request is rejected as unauthenticated
      And the sign-in error does not reveal that the account is inactive

    Scenario: An inactive user cannot refresh their session
      When Nora refreshes the session
      Then the request is rejected as unauthenticated

    # GAP-06 (fixed): every authenticated request re-checks the user's current status
    # (and org role), so an existing token stops working the moment the user is deactivated.
    Scenario: An inactive user's current session stops working immediately
      When Nora views the organization "Acme"
      Then the request is rejected as unauthenticated

    Scenario: A reactivated user can sign in again
      Given Olivia has reactivated the user Nora
      When Nora signs in to "Acme" with password "nora-secret-1"
      Then the request succeeds

  Rule: Deactivation keeps the user's history

    Scenario: Tasks assigned to a deactivated user keep their assignee
      Given a project "Apollo" in "Acme" with a board "Sprint 1"
      And Nora is a Member of project "Apollo"
      And a task "Fix login bug" on board "Sprint 1" assigned to Nora
      When Olivia deactivates the user Nora
      Then the task "Fix login bug" is assigned to Nora
      And Olivia can still view the user Nora
