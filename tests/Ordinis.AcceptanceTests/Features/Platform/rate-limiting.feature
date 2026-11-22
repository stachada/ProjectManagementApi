@area-platform @needs-rate-limiter
Feature: Rate limiting
  As the operator of the API
  I want to cap how many requests each caller can make
  So that one noisy client cannot degrade the service for everyone else

  Limits:
    anonymous callers     — 100 requests per minute per IP address (fixed window)
    authenticated callers — 500 requests per minute per user (sliding window)
  Excess requests are rejected immediately (no queueing).

  Background:
    Given an organization "Acme"
    And Bob is a user in "Acme"
    And Nora is a user in "Acme"

  Rule: Anonymous callers are limited per IP address

    Scenario: The 101st anonymous request within a minute is rejected
      Given 100 anonymous requests have been made from one address within the current minute
      When another anonymous request is made from that address
      Then the request is rejected as too many requests
      And the caller is told how many seconds to wait before retrying
      And the response is a Problem Details document with status 429 and title "Too many requests"

    Scenario: Another address is not affected
      Given 100 anonymous requests have been made from one address within the current minute
      When an anonymous request is made from a different address
      Then the request is not rate limited

    @needs-clock
    Scenario: The limit resets in the next window
      Given 100 anonymous requests have been made from one address within the current minute
      And the next minute has started
      When another anonymous request is made from that address
      Then the request is not rate limited

  Rule: Authenticated callers are limited per user, wherever they connect from

    Scenario: The 501st request by one user within a minute is rejected
      Given Bob has made 500 requests within the last minute
      When Bob makes another request
      Then the request is rejected as too many requests
      And the caller is told how many seconds to wait before retrying

    Scenario: Other users are not affected
      Given Bob has made 500 requests within the last minute
      When Nora makes a request from the same address
      Then the request is not rate limited

    Scenario: A user's limit applies across addresses
      Given Bob has made 500 requests within the last minute from several addresses
      When Bob makes another request from a new address
      Then the request is rejected as too many requests

    @needs-clock
    Scenario: The sliding window frees capacity gradually
      Given Bob made 500 requests within one 10-second span
      And 61 seconds have passed since that span
      When Bob makes another request
      Then the request is not rate limited

  Rule: Infrastructure probes are never limited

    Scenario: The health check is exempt
      Given 100 anonymous requests have been made from one address within the current minute
      When the health check is called from that address
      Then the request succeeds
