@area-auth
Feature: Session refresh
  As a signed-in user
  I want to exchange my refresh token for a new session
  So that I stay signed in without re-entering my password every 15 minutes

  Background:
    Given an organization "Acme" whose org Admin is Olivia
    And Nora is a user in "Acme"
    And Nora has signed in to "Acme" and kept the refresh token

  Rule: A refresh token can be exchanged exactly once for a new session

    @smoke
    Scenario: Refreshing the session
      When Nora refreshes the session with the kept refresh token
      Then the request succeeds
      And a new access token and a new refresh token are returned
      And the new refresh token differs from the kept one

    Scenario: The refreshed access token reflects the user's current role
      Given Olivia has changed the org role of Nora to Admin
      When Nora refreshes the session with the kept refresh token
      Then the new access token carries org role Admin

    Scenario: A refresh token cannot be used twice
      Given Nora has refreshed the session with the kept refresh token
      When Nora refreshes the session with the kept refresh token
      Then the request is rejected as unauthenticated

    Scenario: The rotated refresh token keeps working
      Given Nora has refreshed the session with the kept refresh token
      When Nora refreshes the session with the latest refresh token
      Then the request succeeds

  Rule: Invalid, expired or orphaned refresh tokens are rejected

    Scenario: An unknown refresh token is rejected
      When someone refreshes a session with a made-up refresh token
      Then the request is rejected as unauthenticated
      And the error is "Invalid or expired refresh token."

    @needs-clock
    Scenario: A refresh token expires after 7 days
      Given 7 days and 1 second have passed
      When Nora refreshes the session with the kept refresh token
      Then the request is rejected as unauthenticated

    @needs-clock
    Scenario: A refresh token is still valid just before it expires
      Given 6 days and 23 hours have passed
      When Nora refreshes the session with the kept refresh token
      Then the request succeeds

    Scenario: The refresh token of an inactive user is rejected
      Given Olivia has deactivated the user Nora
      When Nora refreshes the session with the kept refresh token
      Then the request is rejected as unauthenticated

    Scenario: The refresh token is required
      When someone refreshes a session without a refresh token
      Then the request is rejected as invalid on field "refreshToken"

  Rule: Access tokens expire after 15 minutes with no grace period

    @needs-clock
    Scenario: An expired access token is rejected
      Given 15 minutes and 1 second have passed
      When Nora views the organization "Acme" using the original access token
      Then the request is rejected as unauthenticated

    @needs-clock
    Scenario: An access token is valid until its expiry
      Given 14 minutes and 59 seconds have passed
      When Nora views the organization "Acme" using the original access token
      Then the request succeeds

    Scenario Outline: An invalid access token is rejected: <case>
      When someone views the organization "Acme" using <token>
      Then the request is rejected as unauthenticated

      Examples:
        | case                         | token                                       |
        | no token                     | no access token                             |
        | garbage                      | a malformed access token                    |
        | wrong signature              | an access token signed with a different key |
        | wrong issuer                 | an access token from a different issuer     |
        | wrong audience               | an access token for a different audience    |
