@area-tasks
Feature: Task comments
  As a project contributor
  I want to discuss a task in comments
  So that decisions and context stay with the work

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

  Rule: Contributors can comment on a task

    @smoke
    Scenario: Adding a comment
      When Bob comments "Reproduced on Safari 18" on the task "Fix login bug"
      Then the request succeeds
      And the response points to the comments of "Fix login bug"
      And the comments of "Fix login bug" are:
        | author | content                 |
        | Bob    | Reproduced on Safari 18 |

    Scenario: Comments are listed with their author
      Given Bob has commented "Reproduced on Safari 18" on the task "Fix login bug"
      And Alice has commented "Assigning to the auth team" on the task "Fix login bug"
      When Victor views the comments of "Fix login bug"
      Then the request succeeds
      And the comments of "Fix login bug" include 2 comments by Bob and Alice

    Scenario: Comments are included in the task details
      Given Bob has commented "Reproduced on Safari 18" on the task "Fix login bug"
      When Victor views the task "Fix login bug"
      Then the task shows 1 comment

    # A request whose required field is absent cannot be bound at all, so it is malformed (400);
    # one that binds but breaks a rule is invalid (422).
    Scenario: A comment without content is rejected as malformed
      When Bob comments nothing on the task "Fix login bug"
      Then the request is rejected as malformed

    Scenario Outline: Invalid comment content: <case>
      When Bob comments <content> on the task "Fix login bug"
      Then the request is rejected as invalid on field "content"

      Examples:
        | case                | content                          |
        | blank               | a blank text                     |
        | 10,001 characters   | a 10001-character text           |

    Scenario: A comment of exactly 10,000 characters is accepted
      When Bob comments a 10000-character text on the task "Fix login bug"
      Then the request succeeds

    Scenario: Commenting on a deleted task is rejected
      Given Alice has deleted the task "Fix login bug"
      When Bob comments "Too late" on the task "Fix login bug"
      Then the request is rejected as not found

    @role-matrix
    Scenario Outline: <persona> adding a comment
      When <persona> comments "Looks good" on the task "Fix login bug"
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
    Scenario Outline: <persona> reading comments
      When <persona> views the comments of "Fix login bug"
      Then <outcome>

      Examples:
        | persona | outcome                              |
        | Victor  | the request succeeds                 |
        | Olivia  | the request succeeds                 |
        | Nora    | the request is rejected as not found |
        | Mallory | the request is rejected as not found |

  Rule: Only the author can edit a comment

    Background:
      Given Bob has commented "Reproduced on Safari 18" on the task "Fix login bug"

    Scenario: The author edits a comment
      When Bob edits that comment to "Reproduced on Safari 18 and Firefox 140"
      Then the request succeeds
      And the comments of "Fix login bug" are:
        | author | content                                 |
        | Bob    | Reproduced on Safari 18 and Firefox 140 |

    Scenario Outline: Somebody other than the author cannot edit a comment: <persona>
      When <persona> edits that comment to "Hijacked"
      Then the request is rejected as invalid
      And the comments of "Fix login bug" are:
        | author | content                 |
        | Bob    | Reproduced on Safari 18 |

      Examples:
        | persona |
        | Carol   |
        | Alice   |
        | Olivia  |

    Scenario: A removed comment cannot be edited
      Given Bob has removed that comment
      When Bob edits that comment to "Back from the dead"
      Then the request is rejected as invalid

    Scenario: Edited content must be valid
      When Bob edits that comment to a 10001-character text
      Then the request is rejected as invalid on field "newContent"

  Rule: A comment can be removed by its author or a moderator

    Background:
      Given Bob has commented "Reproduced on Safari 18" on the task "Fix login bug"

    Scenario Outline: <persona> removes a comment
      When <persona> removes that comment
      Then the request succeeds
      And the task "Fix login bug" has no comments

      Examples: Author and moderators
        | persona |
        | Bob     |
        | Alice   |
        | Olivia  |

    Scenario: Another member cannot remove a comment
      When Carol removes that comment
      Then the request is rejected as invalid with error "task.comment-not-author"
      And the task "Fix login bug" has 1 comment

    Scenario: Removing a comment that does not belong to the task is rejected
      When Bob removes an unknown comment from the task "Fix login bug"
      Then the request is rejected as invalid with error "task.comment-not-found"

    Scenario: A Viewer cannot remove comments
      When Victor removes that comment
      Then the request is rejected as forbidden

  Rule: Comment activity is recorded and announced

    Scenario: Adding and removing a comment appear in the audit trail
      Given Bob has commented "Reproduced on Safari 18" on the task "Fix login bug"
      When Alice removes that comment
      Then the audit trail of project "Apollo" shows, newest first:
        | event          | actor | task          |
        | CommentRemoved | Alice | Fix login bug |
        | CommentAdded   | Bob   | Fix login bug |

    @background
    Scenario: Subscribed webhooks receive new comments
      Given project "Apollo" has a webhook subscribed to "comment.added"
      When Bob comments "Reproduced on Safari 18" on the task "Fix login bug"
      Then the webhook eventually receives a "comment.added" event for "Fix login bug"
