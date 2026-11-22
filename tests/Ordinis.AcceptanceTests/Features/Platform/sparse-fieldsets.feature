@area-platform
Feature: Selecting fields in lists
  As an API client on a slow connection
  I want to ask for only the fields I need
  So that list responses are smaller and faster

  Background:
    Given an organization "Acme" whose org Admin is Olivia
    And a project "Apollo" in "Acme" with a board "Sprint 1"
    And Bob is a Member of project "Apollo"
    And a task "Fix login bug" on board "Sprint 1" with priority High

  Rule: Only the requested fields are returned, and the id is always included

    @smoke
    Scenario: Selecting fields
      When Bob lists all tasks returning only "title,priority"
      Then every entry contains exactly the fields "id, title, priority"

    Scenario: Field names are case-insensitive and returned in camelCase
      When Bob lists all tasks returning only "TITLE,DueDate"
      Then every entry contains exactly the fields "id, title, dueDate"

    Scenario: Unknown fields are ignored
      When Bob lists all tasks returning only "title,colour"
      Then every entry contains exactly the fields "id, title"

    Scenario: Duplicates and spaces are tolerated
      When Bob lists all tasks returning only " title , title,status "
      Then every entry contains exactly the fields "id, title, status"

    Scenario: Asking for only the id
      When Bob lists all tasks returning only "id"
      Then every entry contains exactly the fields "id"

    Scenario: Without a selection every field is returned
      When Bob lists all tasks
      Then every entry contains all task summary fields

  Rule: Every list endpoint supports field selection

    Scenario Outline: Selecting fields on <list>
      When Bob lists <list> returning only "<fields>"
      Then every entry contains exactly the fields "id, <fields>"

      Examples:
        | list                                | fields |
        | all tasks                           | title  |
        | the tasks of project "Apollo"       | title  |
        | the tasks of board "Sprint 1"       | title  |
        | the projects                        | name   |
        | the projects of organization "Acme" | name   |
        | the search results for "login"      | title  |
