@area-authorization @role-matrix
Feature: Role permissions
  As a project or organization administrator
  I want each role to grant a predictable set of capabilities
  So that access can be reasoned about without reading the code

  This is the consolidated permission matrix. The individual feature files
  repeat the relevant rows next to the behaviour they protect.

    Capability                          Viewer  Member  Admin  Org Admin  Assignee*
    View project / board / task / audit   ✓       ✓       ✓       ✓          –
    Create board / task                   ✗       ✓       ✓       ✓          –
    Comment / attach                      ✗       ✓       ✓       ✓          –
    Update / move / assign task           ✗       ✓       ✓       ✓          ✓
    Delete task                           ✗       ✗       ✓       ✓          ✗
    Manage project / members / webhooks   ✗       ✗       ✓       ✓          –
    Rename / archive board                ✗       ✗       ✓       ✓          –

    * the current assignee of a task, whatever their project role

  Background:
    Given an organization "Acme" whose org Admin is Olivia
    And a project "Apollo" in "Acme" with a board "Sprint 1"
    And Alice is an Admin of project "Apollo"
    And Bob is a Member of project "Apollo"
    And Victor is a Viewer of project "Apollo"
    And Nora is a user in "Acme"
    And a task "Fix login bug" on board "Sprint 1"

  Rule: Project capabilities follow the project role

    Scenario Outline: A <role> trying to <capability>
      When the <role> <action>
      Then <outcome>

      Examples: Reading
        | role      | capability    | action                                        | outcome                              |
        | Viewer    | view the task | views the task "Fix login bug"                | the request succeeds                 |
        | Viewer    | read audit    | reads the audit trail of project "Apollo"     | the request succeeds                 |

      Examples: Contributing
        | role      | capability    | action                                          | outcome                              |
        | Viewer    | create a task | creates a task "Write docs" on board "Sprint 1" | the request is rejected as invalid   |
        | Member    | create a task | creates a task "Write docs" on board "Sprint 1" | the request succeeds                 |
        | Viewer    | create board  | creates a board "Sprint 2" in project "Apollo"  | the request is rejected as forbidden |
        | Member    | create board  | creates a board "Sprint 2" in project "Apollo"  | the request succeeds                 |
        | Viewer    | comment       | comments "Hi" on the task "Fix login bug"       | the request is rejected as forbidden |
        | Member    | comment       | comments "Hi" on the task "Fix login bug"       | the request succeeds                 |
        | Viewer    | move a task   | moves the task "Fix login bug" to ToDo          | the request is rejected as forbidden |
        | Member    | move a task   | moves the task "Fix login bug" to ToDo          | the request succeeds                 |

      Examples: Administering
        | role      | capability     | action                                           | outcome                              |
        | Member    | delete a task  | deletes the task "Fix login bug"                 | the request is rejected as forbidden |
        | Admin     | delete a task  | deletes the task "Fix login bug"                 | the request succeeds                 |
        | Member    | rename project | renames the project "Apollo" to "Apollo 2"       | the request is rejected as forbidden |
        | Admin     | rename project | renames the project "Apollo" to "Apollo 2"       | the request succeeds                 |
        | Member    | add a member   | adds Nora to project "Apollo" as a Viewer        | the request is rejected as forbidden |
        | Admin     | add a member   | adds Nora to project "Apollo" as a Viewer        | the request succeeds                 |
        | Member    | rename board   | renames the board "Sprint 1" to "Sprint One"     | the request is rejected as forbidden |
        | Admin     | rename board   | renames the board "Sprint 1" to "Sprint One"     | the request succeeds                 |
        | Member    | list webhooks  | lists the webhooks of project "Apollo"           | the request is rejected as forbidden |
        | Admin     | list webhooks  | lists the webhooks of project "Apollo"           | the request succeeds                 |
        | Org Admin | delete a task  | deletes the task "Fix login bug"                 | the request succeeds                 |
        | Org Admin | list webhooks  | lists the webhooks of project "Apollo"           | the request succeeds                 |

  Rule: The assignee of a task may work on it whatever their project role

    Background:
      Given the task "Fix login bug" is assigned to Victor

    Scenario Outline: The Viewer assignee trying to <capability>
      When Victor <action>
      Then <outcome>

      Examples:
        | capability    | action                                            | outcome                              |
        | move it       | moves the task "Fix login bug" to ToDo            | the request succeeds                 |
        | update it     | renames the task "Fix login bug" to "Fix login"   | the request succeeds                 |
        | reassign it   | assigns the task "Fix login bug" to Bob           | the request succeeds                 |
        | close it      | closes the task "Fix login bug"                   | the request is rejected as invalid with error "task.invalid-status-transition" |
        | delete it     | deletes the task "Fix login bug"                  | the request is rejected as forbidden |
        | comment on it | comments "Done" on the task "Fix login bug"       | the request is rejected as forbidden |

  Rule: Organization capabilities follow the organization role

    Scenario Outline: An org <role> trying to <capability>
      Given Quinn is a user in "Acme" with org role <role>
      When Quinn <action>
      Then <outcome>

      Examples:
        | role   | capability          | action                                                | outcome                              |
        | Member | create a user       | creates a user "Dan" with email "dan@acme.example"    | the request is rejected as forbidden |
        | Admin  | create a user       | creates a user "Dan" with email "dan@acme.example"    | the request succeeds                 |
        | Member | create an org       | creates an organization "Initech"                     | the request is rejected as forbidden |
        | Admin  | create an org       | creates an organization "Initech"                     | the request succeeds                 |
        | Member | suspend the org     | suspends the organization "Acme"                      | the request is rejected as forbidden |
        | Admin  | suspend the org     | suspends the organization "Acme"                      | the request succeeds                 |
        | Member | deactivate a user   | deactivates the user Bob                              | the request is rejected as forbidden |
        | Admin  | deactivate a user   | deactivates the user Bob                              | the request succeeds                 |
        | Member | create a project    | creates a project "Hermes" in "Acme"                  | the request succeeds                 |
        | Viewer | create a project    | creates a project "Hermes" in "Acme"                  | the request succeeds                 |
