@area-auth
Feature: Signing out
  As a signed-in user
  I want to sign out
  So that my refresh token can no longer be used to resume the session

  Background:
    Given an organization "Acme"
    And Nora is a user in "Acme"
    And Nora has signed in to "Acme" and kept the refresh token

  Rule: Signing out revokes the refresh token

    @smoke
    Scenario: Signing out
      When Nora signs out
      Then the request succeeds
      And Nora cannot refresh the session with the kept refresh token

    Scenario: Signing out twice is harmless
      Given Nora has signed out
      When Nora signs out
      Then the request succeeds

    Scenario: The current access token stays valid until it expires
      Given Nora has signed out
      When Nora views the organization "Acme" using the original access token
      Then the request succeeds

    Scenario: The user can sign in again after signing out
      Given Nora has signed out
      When Nora signs in to "Acme"
      Then the request succeeds

  Rule: Signing out requires a session

    Scenario: An anonymous caller cannot sign out
      When an anonymous caller signs out
      Then the request is rejected as unauthenticated
