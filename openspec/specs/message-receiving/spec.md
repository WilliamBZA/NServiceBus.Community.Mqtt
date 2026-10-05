# message-receiving Specification

## Purpose

Defines how an endpoint receives messages: queue semantics on top of MQTT, acknowledgement, recoverability integration, concurrency, graceful and cancelled stop, and resilience to broker disconnects. This is the behavior the NServiceBus transport seam requires of a message pump.

## Requirements

### Requirement: Durable queue semantics
Each endpoint input address SHALL behave as a durable queue with a single consumer. A message sent to the address while the endpoint is not running SHALL be retained by the broker and delivered once the endpoint starts.

#### Scenario: Send between initialization and start
- **WHEN** the transport has been initialized for an endpoint but receiving has not been started yet, a message is sent to the endpoint's input address, and receiving is then started
- **THEN** the endpoint receives the message

#### Scenario: Message sent while stopped
- **WHEN** an endpoint is stopped, a message is sent to its address, and the endpoint is started again
- **THEN** the endpoint receives the message

### Requirement: One consumer per queue
At most one running consumer SHALL hold a given input address at a time. Scale-out of a single input address across several instances is not supported. If a second consumer starts on an address that already has a live consumer, the broker's session takeover SHALL displace the first. The displaced consumer SHALL report a critical error whose message names the address, explains that only one consumer per queue is supported, and says each instance needs its own endpoint name. It SHALL NOT reconnect and fight for the address.

#### Scenario: Second consumer displaces the first
- **WHEN** an endpoint is receiving on an address and a second consumer starts on the same address
- **THEN** the first consumer reports a critical error naming the address and stops receiving, and the second consumer receives subsequent messages

#### Scenario: Displaced consumer does not reconnect
- **WHEN** a consumer has been displaced and ten seconds pass
- **THEN** it has not reconnected, and the second consumer's session is not interrupted

### Requirement: Message content and context
For each received message the pump SHALL pass the handler the original headers (including those with non-ASCII keys or values), the exact body bytes, a non-empty native message ID, and the endpoint's receive address. If the incoming message has no `NServiceBus.MessageId` header, one SHALL be assigned.

#### Scenario: Headers and body preserved
- **WHEN** a message with headers `MyHeader=MyValue` and `a-😅-B7=a-😍-b` and body bytes `{1,2,3}` is sent to the endpoint
- **THEN** the handler receives both headers unchanged and the body `{1,2,3}`

#### Scenario: Receive address exposed
- **WHEN** a message is processed or fails
- **THEN** the message context and error context both carry the pump's receive address

### Requirement: Acknowledgement by transaction mode
In `ReceiveOnly` mode the pump SHALL acknowledge a message to the broker only after the handler succeeds, or after the recoverability callback reports `Handled`. A message not acknowledged SHALL be redelivered. In `None` mode the pump SHALL acknowledge before handling and SHALL NOT redeliver on failure.

#### Scenario: Handler succeeds
- **WHEN** the handler completes without error in `ReceiveOnly` mode
- **THEN** the message is acknowledged and not redelivered

#### Scenario: Endpoint dies mid-processing
- **WHEN** an endpoint in `ReceiveOnly` mode loses its connection while a message is being handled, and the endpoint later reconnects or restarts
- **THEN** the broker redelivers the message to the endpoint

#### Scenario: Failure in None mode
- **WHEN** the handler throws in `None` mode and recoverability reports `Handled`
- **THEN** the message is not processed again

### Requirement: Recoverability integration
When a handler throws, the pump SHALL invoke the recoverability callback with the exception, the message headers and body, the receive address, a one-based count of consecutive processing failures for that message, and the same extension bag the handler used. If the callback returns `Handled`, the message SHALL be consumed. If it returns `RetryRequired`, the message SHALL be handled again immediately, with the original unmodified headers. If the callback itself throws, the pump SHALL report a critical error and SHALL NOT lose the message.

#### Scenario: First failure
- **WHEN** a handler throws `Simulated exception` for a message with header `MyHeader=MyValue`
- **THEN** the callback receives that exception, a failure count of 1, header `MyHeader=MyValue`, and any items the handler placed in the context extensions

#### Scenario: Immediate retry
- **WHEN** the callback returns `RetryRequired`
- **THEN** the handler is invoked again for the same message with the failure count incremented

#### Scenario: Header mutation does not leak into the retry
- **WHEN** the handler or callback modifies a header and the callback returns `RetryRequired`
- **THEN** the next attempt sees the header's original value

#### Scenario: Recoverability itself fails
- **WHEN** the callback throws a non-cancellation exception
- **THEN** the critical-error callback is invoked with the exception, and the message is neither acknowledged nor dropped

### Requirement: Poison messages do not stall the pump
A payload that cannot be decoded into a message SHALL be logged at error level and discarded. The pump SHALL continue to process later messages.

#### Scenario: Garbage payload
- **WHEN** a payload that is not valid message JSON arrives on the endpoint's address, followed by a valid message
- **THEN** the garbage payload is discarded with an error logged and the valid message is handled

### Requirement: Concurrency limits
The pump SHALL process at most the configured maximum number of messages at the same time, and SHALL be able to change that limit while running without losing or duplicating messages.

#### Scenario: Limit is honoured
- **WHEN** the limit is 4 and 20 messages arrive whose handlers block until released
- **THEN** no more than 4 handlers run at once and the others wait

#### Scenario: Limit changed at runtime
- **WHEN** the limit is raised from 2 to 8 while messages are waiting
- **THEN** up to 8 handlers run concurrently without restarting the pump

### Requirement: Graceful stop
When receiving is stopped, the pump SHALL stop accepting new messages, wait for in-flight handlers to finish, and only then complete. Handlers SHALL NOT be cancelled by a stop that is not itself cancelled. After stop completes, the pump SHALL hold no broker connection.

#### Scenario: Stop waits for the handler
- **WHEN** a handler is running and stop is requested with a token that is not cancelled
- **THEN** the handler's cancellation token stays uncancelled, and stop completes only after the handler returns

#### Scenario: Nothing runs after stop
- **WHEN** stop has completed and a message is then sent to the address
- **THEN** no handler is invoked until receiving is started again

### Requirement: Cancelled stop
If the token passed to stop is cancelled, the pump SHALL cancel in-flight handlers' cancellation tokens. A handler cancelled this way SHALL NOT trigger the recoverability callback. In `ReceiveOnly` mode its message SHALL stay unacknowledged so it is redelivered later.

#### Scenario: Handler cancelled by stop
- **WHEN** a handler is blocked on its cancellation token and stop is requested with an already-cancelled token
- **THEN** the handler observes cancellation and the recoverability callback is not invoked

### Requirement: Broker connection resilience
If the broker connection is lost while running, the pump SHALL reconnect with bounded back-off, restore all its subscriptions, and resume processing, logging each failed attempt. If the broker cannot be reached at startup, starting SHALL fail with an exception that names the host and port. The pump SHALL NOT silently stop processing.

#### Scenario: Broker restarts while running
- **WHEN** the broker is restarted while an endpoint is running and a message is sent after it comes back
- **THEN** the endpoint reconnects and handles the message without being restarted

#### Scenario: Broker unreachable at startup
- **WHEN** receiving is started and the broker is not reachable
- **THEN** starting fails with an error naming the configured host and port

### Requirement: Purge on startup
When the receive settings request purge on startup, the transport SHALL discard messages that were queued for the address before it started. The purge SHALL happen when the transport is initialized, before the endpoint's startup tasks run, so that messages those tasks send to the endpoint's own queue are kept. Otherwise queued messages SHALL be preserved.

#### Scenario: Purge discards backlog
- **WHEN** messages are queued for an address and the endpoint starts with purge on startup enabled
- **THEN** the queued messages are not delivered

#### Scenario: Messages sent after initialization survive the purge
- **WHEN** an endpoint with purge on startup enabled has been initialized, a message is then sent to its address (as a startup task does), and receiving is started
- **THEN** the endpoint receives that message

#### Scenario: No purge keeps backlog
- **WHEN** messages are queued for an address and the endpoint starts without purge on startup
- **THEN** the queued messages are delivered
