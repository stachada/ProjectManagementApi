@area-webhooks
Feature: Webhook subscriptions
  As a project administrator
  I want to register HTTP endpoints that are notified of task events
  So that other systems (chat, CI, dashboards) can react to changes in real time

  Available event types: task.created, task.moved, task.assigned, comment.added.

  Background:
    Given an organization "Acme" whose org Admin is Olivia
    And Nora is a user in "Acme"
    And an organization "Globex" with a user Mallory
    And a project "Apollo" in "Acme" with a board "Sprint 1"
    And Alice is an Admin of project "Apollo"
    And Bob is a Member of project "Apollo"
    And Victor is a Viewer of project "Apollo"

  Rule: Project Admins register webhooks for some or all event types

    @smoke
    Scenario: Registering a webhook for selected events
      When Alice registers a webhook for project "Apollo" at "https://hooks.example.com/ordinis" for "task.created, task.moved"
      Then the request succeeds
      And the response points to the webhooks of project "Apollo"
      And the webhooks of project "Apollo" are:
        | url                               | events                   | registered by |
        | https://hooks.example.com/ordinis | task.created, task.moved | Alice         |

    Scenario: Registering a webhook without event types subscribes to everything
      When Alice registers a webhook for project "Apollo" at "https://hooks.example.com/ordinis"
      Then the request succeeds
      And the webhook at "https://hooks.example.com/ordinis" receives all event types

    Scenario: Plain HTTP endpoints are allowed
      When Alice registers a webhook for project "Apollo" at "http://hooks.example.com/ordinis"
      Then the request succeeds

    Scenario: An unknown event type is rejected
      When Alice registers a webhook for project "Apollo" at "https://hooks.example.com/ordinis" for "task.exploded"
      Then the request is rejected as invalid on field "eventTypes"

    Scenario Outline: The URL must be an absolute http(s) address: <url>
      When Alice registers a webhook for project "Apollo" at "<url>"
      Then the request is rejected as invalid on field "url"

      Examples:
        | url                             |
        | /relative/path                  |
        | ftp://hooks.example.com/ordinis |
        | file:///etc/passwd              |
        | not a url                       |

  Rule: Webhooks cannot target internal infrastructure

    Scenario Outline: An internal address is rejected: <kind>
      When Alice registers a webhook for project "Apollo" at "<url>"
      Then the request is rejected as invalid on field "url"

      Examples:
        | kind                    | url                                      |
        | loopback                | http://127.0.0.1/hook                    |
        | localhost name          | http://localhost/hook                    |
        | "this host"             | http://0.0.0.0/hook                      |
        | private 10/8            | http://10.0.0.5/hook                     |
        | private 172.16/12       | http://172.20.1.1/hook                   |
        | private 192.168/16      | http://192.168.1.10/hook                 |
        | cloud metadata          | http://169.254.169.254/latest/meta-data  |
        | carrier-grade NAT       | http://100.64.0.1/hook                   |
        | multicast               | http://224.0.0.1/hook                    |
        | IPv6 loopback           | http://[::1]/hook                        |
        | IPv6 unique-local       | http://[fd00::1]/hook                    |
        | IPv6 link-local         | http://[fe80::1]/hook                    |
        | unresolvable host name  | https://no-such-host.invalid/hook        |

    Scenario: A host name that resolves to a private address is rejected
      Given the host "intranet.example.com" resolves to 10.1.2.3
      When Alice registers a webhook for project "Apollo" at "https://intranet.example.com/hook"
      Then the request is rejected as invalid on field "url"

  Rule: A project can have at most 20 webhooks

    Scenario: Registering the 20th webhook succeeds
      Given project "Apollo" has 19 webhooks
      When Alice registers a webhook for project "Apollo" at "https://hooks.example.com/twenty"
      Then the request succeeds

    Scenario: Registering a 21st webhook is rejected
      Given project "Apollo" has 20 webhooks
      When Alice registers a webhook for project "Apollo" at "https://hooks.example.com/one-too-many"
      Then the request is rejected as invalid with error "project.webhook-limit-reached"

    Scenario: Unregistering frees a slot
      Given project "Apollo" has 20 webhooks
      And Alice has unregistered one of them
      When Alice registers a webhook for project "Apollo" at "https://hooks.example.com/replacement"
      Then the request succeeds

  Rule: Webhooks can be unregistered

    Background:
      Given project "Apollo" has a webhook at "https://hooks.example.com/ordinis"

    Scenario: Unregistering a webhook
      When Alice unregisters the webhook at "https://hooks.example.com/ordinis" from project "Apollo"
      Then the request succeeds
      And project "Apollo" has no webhooks

    Scenario: Unregistering an unknown webhook is rejected
      When Alice unregisters an unknown webhook from project "Apollo"
      Then the request is rejected as invalid with error "project.webhook-not-found"

    @background
    Scenario: An unregistered webhook receives no further events
      Given Alice has unregistered the webhook at "https://hooks.example.com/ordinis" from project "Apollo"
      When Bob creates a task "Write docs" on board "Sprint 1"
      Then the receiver at "https://hooks.example.com/ordinis" receives nothing within the delivery window

  Rule: Webhooks of an archived project cannot be changed

    Scenario: Registering on an archived project is rejected
      Given the project "Apollo" is archived
      When Alice registers a webhook for project "Apollo" at "https://hooks.example.com/ordinis"
      Then the request is rejected as invalid with error "project.archived"

    Scenario: Unregistering on an archived project is rejected
      Given project "Apollo" has a webhook at "https://hooks.example.com/ordinis"
      And the project "Apollo" is archived
      When Alice unregisters the webhook at "https://hooks.example.com/ordinis" from project "Apollo"
      Then the request is rejected as invalid with error "project.archived"

  Rule: Only project Admins can see and manage webhooks

    @role-matrix
    Scenario Outline: <persona> <action> webhooks of project "Apollo"
      Given project "Apollo" has a webhook at "https://hooks.example.com/ordinis"
      When <persona> <step>
      Then <outcome>

      Examples:
        | persona | action        | step                                                                               | outcome                              |
        | Alice   | lists         | lists the webhooks of project "Apollo"                                             | the request succeeds                 |
        | Olivia  | lists         | lists the webhooks of project "Apollo"                                             | the request succeeds                 |
        | Bob     | lists         | lists the webhooks of project "Apollo"                                             | the request is rejected as forbidden |
        | Victor  | lists         | lists the webhooks of project "Apollo"                                             | the request is rejected as forbidden |
        | Nora    | lists         | lists the webhooks of project "Apollo"                                             | the request is rejected as not found |
        | Mallory | lists         | lists the webhooks of project "Apollo"                                             | the request is rejected as not found |
        | Bob     | registers     | registers a webhook for project "Apollo" at "https://hooks.example.com/bob"         | the request is rejected as forbidden |
        | Olivia  | registers     | registers a webhook for project "Apollo" at "https://hooks.example.com/olivia"      | the request succeeds                 |
        | Bob     | unregisters   | unregisters the webhook at "https://hooks.example.com/ordinis" from project "Apollo" | the request is rejected as forbidden |
        | Mallory | unregisters   | unregisters the webhook at "https://hooks.example.com/ordinis" from project "Apollo" | the request is rejected as not found |

  Rule: Webhook changes are guarded by the project's optimistic concurrency

    Scenario: Registering with a stale project version is rejected
      Given Alice has read the project "Apollo"
      And Olivia has since renamed the project "Apollo" to "Apollo Platform"
      When Alice registers a webhook for project "Apollo Platform" at "https://hooks.example.com/ordinis" using the version Alice read
      Then the request is rejected as conflicting

    Scenario: Registering without stating a version is rejected
      When Alice registers a webhook for project "Apollo" at "https://hooks.example.com/ordinis" without stating a version
      Then the request is rejected as invalid

    Scenario: Unregistering without stating a version is rejected
      Given project "Apollo" has a webhook at "https://hooks.example.com/ordinis"
      When Alice unregisters the webhook at "https://hooks.example.com/ordinis" from project "Apollo" without stating a version
      Then the request is rejected as invalid
