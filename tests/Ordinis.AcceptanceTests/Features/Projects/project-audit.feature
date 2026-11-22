@area-projects
Feature: Project audit trail
  As a project participant
  I want a chronological record of everything that happened to the project's tasks
  So that I can answer "who changed what, and when?"

  The audit trail is built from the same domain events that drive webhooks,
  so it is written in the same transaction as the change itself.

  Background:
    Given an organization "Acme" whose org Admin is Olivia
    And Nora is a user in "Acme"
    And an organization "Globex" with a user Mallory
    And a project "Apollo" in "Acme" with a board "Sprint 1"
    And a project "Hermes" in "Acme" with a board "Inbox"
    And Alice is an Admin of project "Apollo"
    And Bob is a Member of project "Apollo"
    And Bob is a Member of project "Hermes"
    And Victor is a Viewer of project "Apollo"

  Rule: Every task event is recorded with the user who caused it

    @smoke
    Scenario Outline: Recording "<event>"
      Given a task "Fix login bug" on board "Sprint 1" created by Alice
      And Alice has assigned the task "Fix login bug" to Bob
      And Bob has commented "On it" on the task "Fix login bug"
      And Bob has uploaded "crash.log" to the task "Fix login bug"
      When <actor> <action>
      Then the newest audit entry of project "Apollo" is "<event>" by <actor> for "Fix login bug"
      And the entry records when it occurred and its event details

      Examples:
        | event             | actor | action                                                    |
        | TaskUpdated       | Bob   | changes the priority of the task "Fix login bug" to High  |
        | TaskMoved         | Bob   | moves the task "Fix login bug" to ToDo                    |
        | TaskAssigned      | Alice | assigns the task "Fix login bug" to Alice                 |
        | TaskUnassigned    | Bob   | unassigns the task "Fix login bug"                        |
        | CommentAdded      | Alice | comments "Thanks" on the task "Fix login bug"             |
        | CommentRemoved    | Alice | removes the comment "On it" from the task "Fix login bug" |
        | AttachmentAdded   | Alice | uploads "trace.har" to the task "Fix login bug"           |
        | AttachmentRemoved | Bob   | removes the attachment "crash.log" from the task "Fix login bug" |
        | TaskDeleted       | Alice | deletes the task "Fix login bug"                          |

    Scenario: Creating a task is recorded with its reporter
      When Bob creates a task "Write docs" on board "Sprint 1"
      Then the newest audit entry of project "Apollo" is "TaskCreated" by Bob for "Write docs"

    Scenario: Entries are listed newest first
      Given a task "Fix login bug" on board "Sprint 1" created by Alice
      And Bob has moved the task "Fix login bug" to ToDo
      And Bob has moved the task "Fix login bug" to InProgress
      When Victor reads the audit trail of project "Apollo"
      Then the audit entries are, newest first:
        | event       | actor |
        | TaskMoved   | Bob   |
        | TaskMoved   | Bob   |
        | TaskCreated | Alice |
      And the total count is 3

    Scenario: The history of a deleted task stays in the audit trail
      Given a task "Fix login bug" on board "Sprint 1" created by Alice
      And Alice has deleted the task "Fix login bug"
      When Victor reads the audit trail of project "Apollo"
      Then the audit entries include "TaskCreated" and "TaskDeleted" for "Fix login bug"

  Rule: The audit trail of a project only contains that project's task events

    Scenario: Events of other projects are not included
      Given a task "Fix login bug" on board "Sprint 1" created by Bob
      And a task "Reply to customer" on board "Inbox" created by Bob
      When Victor reads the audit trail of project "Apollo"
      Then the audit entries only concern "Fix login bug"

    Scenario: Changes to the project itself are not task events
      When Alice renames the project "Apollo" to "Apollo Platform"
      And Alice adds Nora to project "Apollo Platform" as a Viewer
      Then the audit trail of project "Apollo Platform" is empty

  Rule: The audit trail is paged

    Scenario: Reading a page of the audit trail
      Given 25 task events have occurred in project "Apollo"
      When Victor reads page 2 of the audit trail of project "Apollo" with 10 entries per page
      Then 10 audit entries are returned
      And the total count is 25

  Rule: Every project member may read the audit trail

    @role-matrix
    Scenario Outline: <persona> reading the audit trail
      When <persona> reads the audit trail of project "Apollo"
      Then <outcome>

      Examples:
        | persona | outcome                              |
        | Alice   | the request succeeds                 |
        | Bob     | the request succeeds                 |
        | Victor  | the request succeeds                 |
        | Olivia  | the request succeeds                 |
        | Nora    | the request is rejected as not found |
        | Mallory | the request is rejected as not found |
