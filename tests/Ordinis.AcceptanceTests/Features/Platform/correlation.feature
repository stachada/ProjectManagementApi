@area-platform
Feature: Request correlation
  As a developer or support engineer
  I want every request to carry a correlation id through responses and logs
  So that a client-reported problem can be traced to the exact server log lines

  Background:
    Given an organization "Acme"
    And Bob is a user in "Acme"

  Rule: Every response carries a correlation id

    @smoke
    Scenario: A correlation id is generated when the client sends none
      When Bob views the organization "Acme" without a correlation id
      Then the response carries a newly generated, time-ordered correlation id

    Scenario: The client's correlation id is echoed back
      When Bob views the organization "Acme" with correlation id "support-ticket-4711"
      Then the response carries the correlation id "support-ticket-4711"

    Scenario: Each request gets its own generated id
      When Bob views the organization "Acme" twice without a correlation id
      Then the two responses carry different correlation ids

    Scenario: Error responses also carry the correlation id
      When Bob views an unknown task with correlation id "support-ticket-4711"
      Then the response carries the correlation id "support-ticket-4711"
      And the Problem Details correlationId is "support-ticket-4711"

  Rule: The correlation id appears in the server logs

    Scenario: The request log line includes the correlation id
      When Bob views the organization "Acme" with correlation id "support-ticket-4711"
      Then a request log entry records the method, path, status code, duration and correlation id "support-ticket-4711"

    Scenario: Log entries written while handling the request include the correlation id
      When Bob views the organization "Acme" with correlation id "support-ticket-4711"
      Then every log entry written while handling the request has CorrelationId "support-ticket-4711"
