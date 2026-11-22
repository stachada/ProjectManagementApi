@area-platform
Feature: API versioning
  As an API client
  I want new representations introduced under a new version
  So that my existing integration keeps working when the API evolves

  Versions are part of the address (/api/v1, /api/v2). Version 2 currently
  only changes task details; everything else is served by version 1.

  Background:
    Given an organization "Acme"
    And a project "Apollo" in "Acme" with a board "Sprint 1"
    And Bob is a Member of project "Apollo"
    And a task "Fix login bug" on board "Sprint 1"

  Rule: Version 1 and version 2 of task details coexist

    @smoke
    Scenario: Both versions return the same task
      When Bob views the task "Fix login bug" using API version 1
      And Bob views the task "Fix login bug" using API version 2
      Then both responses describe the same task with the same version
      And the version 2 response has every field of the version 1 response
      And the version 2 response additionally links to board "Sprint 1"

    Scenario: Version 2 task details follow the same access rules as version 1
      Given Nora is a user in "Acme"
      When Nora views the task "Fix login bug" using API version 2
      Then the request is rejected as not found

  Rule: Only resources that changed have a newer version

    Scenario Outline: <request> has no version 2
      When Bob <request> using API version 2
      Then the request is rejected as not found

      Examples:
        | request                                  |
        | lists all tasks                          |
        | moves the task "Fix login bug" to ToDo   |
        | views the project "Apollo"               |
        | views the board "Sprint 1"               |

    Scenario: An unknown version is not found
      When Bob views the task "Fix login bug" using API version 3
      Then the request is rejected as not found

  Rule: Authentication and health endpoints are unversioned

    Scenario Outline: <endpoint> is served without a version
      When someone calls <endpoint>
      Then the endpoint exists

      Examples:
        | endpoint     |
        | sign-in      |
        | refresh      |
        | sign-out     |
        | health check |
