@area-tasks
Feature: Task attachments
  As a project contributor
  I want to attach files to a task
  So that screenshots, logs and specs live next to the work

  Background:
    Given an organization "Acme" whose org Admin is Olivia
    And Nora is a user in "Acme"
    And an organization "Globex" with a user Mallory
    And a project "Apollo" in "Acme" with a board "Sprint 1"
    And Alice is an Admin of project "Apollo"
    And Bob is a Member of project "Apollo"
    And Carol is a Member of project "Apollo"
    And Victor is a Viewer of project "Apollo"
    And a task "Fix login bug" on board "Sprint 1"

  Rule: Contributors can upload files to a task

    @smoke
    Scenario: Uploading an attachment
      When Bob uploads "crash.log" (text/plain, 2048 bytes) to the task "Fix login bug"
      Then the request succeeds
      And the response points to the attachments of "Fix login bug"
      And the attachments of "Fix login bug" are:
        | file name | content type | size | uploaded by |
        | crash.log | text/plain   | 2048 | Bob         |

    Scenario: Attachments are included in the task details and counts
      Given Bob has uploaded "crash.log" to the task "Fix login bug"
      And Bob has uploaded "screenshot.png" to the task "Fix login bug"
      When Victor views the task "Fix login bug"
      Then the task shows 2 attachments
      And the task "Fix login bug" is listed on board "Sprint 1" with 2 attachments

    Scenario Outline: Invalid upload: <case>
      When Bob uploads <file> to the task "Fix login bug"
      Then the request is rejected as invalid

      Examples:
        | case                        | file                                      |
        | empty file                  | an empty file "crash.log"                 |
        | file name of 256 characters | a file with a 256-character name          |
        | content type over 100 chars | a file with a 101-character content type  |

    Scenario: A file name of exactly 255 characters is accepted
      When Bob uploads a file with a 255-character name to the task "Fix login bug"
      Then the request succeeds

    Scenario: Uploading to a deleted task is rejected
      Given Alice has deleted the task "Fix login bug"
      When Bob uploads "crash.log" to the task "Fix login bug"
      Then the request is rejected as not found

    @role-matrix
    Scenario Outline: <persona> uploading an attachment
      When <persona> uploads "crash.log" to the task "Fix login bug"
      Then <outcome>

      Examples:
        | persona | outcome                              |
        | Alice   | the request succeeds                 |
        | Bob     | the request succeeds                 |
        | Olivia  | the request succeeds                 |
        | Victor  | the request is rejected as forbidden |
        | Nora    | the request is rejected as not found |
        | Mallory | the request is rejected as not found |

    @role-matrix
    Scenario Outline: <persona> listing attachments
      Given Bob has uploaded "crash.log" to the task "Fix login bug"
      When <persona> views the attachments of "Fix login bug"
      Then <outcome>

      Examples:
        | persona | outcome                              |
        | Victor  | the request succeeds                 |
        | Olivia  | the request succeeds                 |
        | Nora    | the request is rejected as not found |
        | Mallory | the request is rejected as not found |

  Rule: Attachments can be removed, which deletes the stored file

    Background:
      Given Bob has uploaded "crash.log" to the task "Fix login bug"

    Scenario Outline: <persona> removes an attachment
      When <persona> removes the attachment "crash.log" from the task "Fix login bug"
      Then the request succeeds
      And the task "Fix login bug" has no attachments
      And the stored file "crash.log" no longer exists

      Examples: Uploader and moderators
        | persona |
        | Bob     |
        | Alice   |
        | Olivia  |

    # GAP-11 (fixed): same uploader-or-moderator rule (and 422 + error code) as comment removal.
    Scenario: Another member cannot remove an attachment
      When Carol removes the attachment "crash.log" from the task "Fix login bug"
      Then the request is rejected as invalid with error "task.attachment-not-uploader"
      And the task "Fix login bug" has 1 attachment

    Scenario: Removing an unknown attachment is rejected
      When Bob removes an unknown attachment from the task "Fix login bug"
      Then the request is rejected as not found

    Scenario: A Viewer cannot remove attachments
      When Victor removes the attachment "crash.log" from the task "Fix login bug"
      Then the request is rejected as forbidden

  Rule: Attachment activity is recorded

    Scenario: Uploading and removing appear in the audit trail
      Given Bob has uploaded "crash.log" to the task "Fix login bug"
      When Alice removes the attachment "crash.log" from the task "Fix login bug"
      Then the audit trail of project "Apollo" shows, newest first:
        | event             | actor | task          |
        | AttachmentRemoved | Alice | Fix login bug |
        | AttachmentAdded   | Bob   | Fix login bug |
