@area-platform
Feature: Health check
  As an orchestrator (Docker, Kubernetes, a load balancer)
  I want an unauthenticated health endpoint
  So that unhealthy instances can be detected and replaced

  Rule: The health check reports whether the API can reach its database

    @smoke
    Scenario: A healthy instance
      When the health check is called anonymously
      Then the request succeeds
      And the health status is "Healthy"

    Scenario: The database is unreachable
      Given the database is unavailable
      When the health check is called anonymously
      Then the health status is "Unhealthy"
      And the response signals service unavailable

  Rule: The health check is always reachable

    Scenario: No access token is required
      When the health check is called without an access token
      Then the request succeeds

    @needs-rate-limiter
    Scenario: The health check is not rate limited
      When the health check is called 200 times within a minute from one address
      Then none of the requests is rate limited

  Rule: The API description is only published in development

    Scenario: The OpenAPI document is published in Development
      Given the API runs in the Development environment
      When the OpenAPI document is requested anonymously
      Then the request succeeds

    # Every address that is not explicitly public falls under the deny-by-default authorization
    # policy, so an anonymous caller is asked to sign in (401) rather than told what exists; a
    # signed-in caller sees that the document is not there at all.
    Scenario: The OpenAPI document is not published in Production
      Given the API runs in the Production environment
      And an organization "Acme" with a user Bob
      When the OpenAPI document is requested anonymously
      Then the request is rejected as unauthenticated
      When Bob requests the OpenAPI document
      Then the request is rejected as not found
