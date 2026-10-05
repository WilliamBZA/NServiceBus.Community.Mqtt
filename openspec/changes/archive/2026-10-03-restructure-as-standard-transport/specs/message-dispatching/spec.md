# Spec Delta

## Purpose

Defines how an endpoint sends and publishes messages: what is delivered, what the caller can rely on when dispatch returns, how failures surface, and send-only endpoints. Together with message-receiving, this makes MQTT usable as an NServiceBus transport.

## ADDED Requirements

### Requirement: Unicast delivery preserves the message
A unicast operation SHALL deliver the message to the receivers of the destination address with its headers and body unchanged. Headers with non-ASCII keys or values and bodies that are empty or contain arbitrary bytes SHALL round-trip exactly.

#### Scenario: Binary body and unicode headers
- **WHEN** a message with body bytes `{0, 255, 1, 2}` and header `a-😅-B7=a-😍-b` is dispatched to an address
- **THEN** a receiver on that address gets the same bytes and the same header

#### Scenario: Empty body
- **WHEN** a message with an empty body is dispatched
- **THEN** the receiver gets an empty body and the message is not treated as a poison message

### Requirement: Message ID is always on the wire
Every dispatched message SHALL carry a non-empty `NServiceBus.MessageId` header. If the outgoing message's headers lack one, the transport SHALL set it from the outgoing message's ID, without altering a header that is already present.

#### Scenario: Missing header filled in
- **WHEN** a message is dispatched whose headers do not include `NServiceBus.MessageId`
- **THEN** the receiver sees that header equal to the outgoing message's ID

#### Scenario: Existing header kept
- **WHEN** a message is dispatched whose headers already include `NServiceBus.MessageId`
- **THEN** the receiver sees the original value

### Requirement: Time to be received
A message dispatched with a time to be received SHALL carry it to the broker as the MQTT 5 message expiry interval, in whole seconds rounded up and never zero, and the broker SHALL NOT deliver the message after it has expired, even if it was waiting for an endpoint that was offline. Every copy of a published event SHALL carry the same interval. A message dispatched without a time to be received, or with one that MQTT cannot express, SHALL NOT expire.

#### Scenario: Expired while the endpoint is stopped
- **WHEN** a message with a time to be received of two seconds is sent to a stopped endpoint, and the endpoint is started after four seconds
- **THEN** the endpoint does not receive that message, and still receives the messages sent with a longer time to be received or none

#### Scenario: Not expired
- **WHEN** a message with a time to be received of thirty seconds is sent to a running endpoint
- **THEN** the endpoint receives it at once

### Requirement: Dispatch reports broker acceptance or failure
Dispatch SHALL complete only after the broker has acknowledged every publish in the batch. If the broker is unreachable or rejects a publish, dispatch SHALL fail with an exception, and SHALL NOT silently drop the message. Dispatch SHALL honour its cancellation token.

#### Scenario: Broker down during send
- **WHEN** a message is dispatched while the broker connection is unavailable and cannot be re-established
- **THEN** dispatch throws an exception and the caller's recoverability policy applies

#### Scenario: Cancelled dispatch
- **WHEN** the cancellation token passed to dispatch is cancelled before the broker acknowledges
- **THEN** dispatch ends with an `OperationCanceledException`

### Requirement: Batches publish every operation
A dispatch containing several unicast and multicast operations SHALL publish all of them. If any operation fails, dispatch SHALL fail and the exception SHALL identify the failing destination.

#### Scenario: Mixed batch
- **WHEN** one dispatch carries two unicast operations and one multicast operation
- **THEN** both addressees and all subscribers of the event receive their message

### Requirement: Declared addresses retain messages
Every address given to the transport at initialization, for the endpoint's own input queues and for the sending addresses (error, audit and similar), SHALL retain messages dispatched to it until a consumer collects them, even if no consumer has ever connected. Dispatch to an address that was never declared and has no live subscribers is delivered on a best-effort basis, and this SHALL be documented in the README.

#### Scenario: Error queue with no consumer yet
- **WHEN** the transport initializes with error address `error` and an endpoint dispatches a failed message to `error` before any consumer connects
- **THEN** a consumer that connects to `error` later receives the message

### Requirement: Send-only endpoints
Initializing the transport with no receivers SHALL succeed and expose an empty set of receivers, and the dispatcher SHALL still be able to send and publish.

#### Scenario: Send-only startup
- **WHEN** the transport is initialized with no receive settings
- **THEN** the infrastructure exposes zero receivers and a message dispatched through it reaches its destination

### Requirement: Clean shutdown of the sending side
Shutting down the transport infrastructure SHALL close the dispatcher's broker connection and release its resources. A second shutdown call SHALL be a no-op.

#### Scenario: Shutdown releases the connection
- **WHEN** the infrastructure is shut down
- **THEN** the broker reports the dispatcher's client as disconnected and no further publishes are accepted
