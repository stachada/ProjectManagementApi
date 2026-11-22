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

    Scenario Outline: The OpenAPI document in the <environment> environment
      Given the API runs in the <environment> environment
      When the OpenAPI document is requested anonymously
      Then <outcome>

      Examples:
        | environment | outcome                              |
        | Development | the request succeeds                 |
        | Production  | the request is rejected as not found |
