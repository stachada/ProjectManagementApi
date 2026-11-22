@area-auth
Feature: Signing in
  As a user
  I want to sign in to my organization with my email and password
  So that I receive a session for using the API

  A session consists of a short-lived access token (15 minutes) and a
  longer-lived refresh token (7 days).

  Background:
    Given an organization "Acme" with slug "acme"
    And Nora is a user in "Acme" with email "nora@acme.example" and password "nora-secret-1"
    And an organization "Globex" with slug "globex"

  Rule: Valid credentials start a session

    @smoke
    Scenario: Signing in
      When someone signs in to "acme" with "nora@acme.example" and "nora-secret-1"
      Then the request succeeds
      And a session for Nora is returned
      And the access token expires 15 minutes from now
      And the access token identifies the user Nora with email, display name, organization "Acme" and org role Member

    Scenario: The access token grants access to the API
      Given Nora has signed in to "Acme"
      When Nora views the organization "Acme" using the access token
      Then the request succeeds

    # GAP-16 (fixed): Login normalizes email and organization slug like the domain
    # stores them, so matching no longer depends on the database collation.
    # (On SQL Server this passed even before the fix; the regression guard is
    # LoginHandlerTests in Ordinis.UnitTests, whose InMemory provider is case-sensitive.)
    Scenario: Email addresses are matched case-insensitively
      When someone signs in to "acme" with "NORA@ACME.EXAMPLE" and "nora-secret-1"
      Then the request succeeds

    Scenario: Signing in again replaces the previous session
      Given Nora has signed in to "Acme" and kept the refresh token
      When Nora signs in to "Acme" again
      Then the earlier refresh token no longer works

  Rule: Every failed sign-in looks the same, so accounts cannot be probed

    Scenario Outline: Sign-in fails: <case>
      When someone signs in to "<org>" with "<email>" and "<password>"
      Then the request is rejected as unauthenticated
      And the sign-in error is the generic "Invalid organization, email, or password."

      Examples:
        | case                       | org     | email              | password      |
        | wrong password             | acme    | nora@acme.example  | wrong-pass-1  |
        | unknown email              | acme    | ghost@acme.example | nora-secret-1 |
        | unknown organization       | initech | nora@acme.example  | nora-secret-1 |
        | right user, wrong tenant   | globex  | nora@acme.example  | nora-secret-1 |

    Scenario: An inactive user cannot sign in
      Given the user Nora is inactive
      When someone signs in to "acme" with "nora@acme.example" and "nora-secret-1"
      Then the request is rejected as unauthenticated
      And the sign-in error is the generic "Invalid organization, email, or password."

  Rule: Sign-in requests must be well-formed

    Scenario Outline: Malformed sign-in: <case>
      When someone signs in <details>
      Then the request is rejected as invalid on field "<field>"

      Examples:
        | case                 | details                                         | field            |
        | missing organization | without an organization                         | organizationSlug |
        | missing email        | to "acme" without an email                      | email            |
        | malformed email      | to "acme" with email "nora" and "nora-secret-1" | email            |
        | missing password     | to "acme" as "nora@acme.example" without a password | password     |

  Rule: Signing in is protected against brute force

    @needs-rate-limiter
    Scenario: Too many anonymous attempts from one address are throttled
      Given 100 sign-in attempts have been made from one address within a minute
      When another sign-in attempt is made from that address
      Then the request is rejected as too many requests
      And the caller is told when to retry
