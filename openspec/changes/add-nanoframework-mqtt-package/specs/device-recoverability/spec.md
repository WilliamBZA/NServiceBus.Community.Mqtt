# Spec Delta

## Purpose

Defines what a device does when handling a message fails: immediate retries, then forwarding to the error queue with the standard NServiceBus failure headers. It also covers messages that can never succeed, and payloads that are not messages at all.

## ADDED Requirements

### Requirement: Immediate retries
When processing a received message fails, the device SHALL handle the message again at once, without a delay. Processing fails when a handler throws or when the messages collected during handling cannot be dispatched. The device SHALL retry up to the configured number of immediate retries. The number SHALL default to 5, and `0` SHALL turn retries off. The configuration SHALL reject a negative number. Every attempt SHALL start from the original headers and body, deserialized again, so that changes made by an earlier attempt do not leak into the next one. There SHALL be no delayed retries.

#### Scenario: Succeeds on the third attempt
- **WHEN** a handler throws on its first two attempts and succeeds on the third
- **THEN** the handler has been invoked three times and nothing is sent to the error queue

#### Scenario: Header change does not leak
- **WHEN** a handler changes a header in its context and then throws
- **THEN** the next attempt sees the header's original value

#### Scenario: Retries turned off
- **WHEN** immediate retries are set to `0` and a handler throws
- **THEN** the handler is invoked once and the message is sent to the error queue

### Requirement: Failed messages go to the error queue
When the last attempt fails, the device SHALL send the message to the configured error queue, which defaults to `error`. The forwarded message SHALL have the original body and the original headers, including its message ID. The device SHALL add these failure headers, with the names and value formats NServiceBus 10 uses for failed messages:
- `NServiceBus.FailedQ`: the device's address.
- `NServiceBus.TimeOfFailure`.
- `NServiceBus.ExceptionInfo.ExceptionType`: the exception type's full name.
- `NServiceBus.ExceptionInfo.Message`.
- `NServiceBus.ExceptionInfo.StackTrace`.
- `NServiceBus.ProcessingEndpoint`: the device's endpoint name.

After forwarding, the device SHALL continue with the next message.

#### Scenario: Always failing handler
- **WHEN** a handler always throws `InvalidOperationException` for a message and immediate retries are 5
- **THEN** the handler is invoked six times, and the error queue receives the message with its original body and message ID, `NServiceBus.FailedQ` equal to the device's address, and `NServiceBus.ExceptionInfo.ExceptionType` equal to `System.InvalidOperationException`

#### Scenario: Processing continues
- **WHEN** a message has been forwarded to the error queue and another message is waiting
- **THEN** the device handles the waiting message

### Requirement: Forwarding to the error queue is retried
If the device cannot forward a failed message to the error queue, it SHALL do the following, without processing other messages:
1. Raise a critical error.
2. Wait, with a back-off.
3. Try to forward the message again.

It SHALL repeat this until the forwarding succeeds or the endpoint is stopped. If the endpoint is stopped first, the message is lost, and the device SHALL log the loss with the message ID.

#### Scenario: Broker comes back
- **WHEN** a message has exhausted its retries while the broker is unreachable, and the broker becomes reachable again
- **THEN** a critical error has been raised, and the message arrives in the error queue once the device has reconnected

### Requirement: Payloads that are not messages are discarded
A payload that cannot be decoded as a message envelope SHALL be logged at error level, with the topic it arrived on, and discarded. The device SHALL then continue with the next message. Such a payload SHALL NOT be sent to the error queue.

#### Scenario: Garbage payload
- **WHEN** a payload that is not valid message JSON arrives on the device's address, followed by a valid message
- **THEN** an error naming the topic is logged, and the valid message is handled

### Requirement: Messages that cannot succeed skip retries
The device SHALL send a message to the error queue at once, with the failure headers and without immediate retries, in any of these cases:
- It has no `NServiceBus.EnclosedMessageTypes` header.
- None of its enclosed types is registered on the device.
- Its `NServiceBus.ContentType` header is present and is not JSON.
- Its body cannot be deserialized into the resolved type.

The failure headers SHALL describe the reason.

#### Scenario: Unknown message type
- **WHEN** a message arrives whose enclosed types are not registered on the device
- **THEN** no handler is invoked, and the message is in the error queue after a single attempt, with an exception message that names the enclosed types

#### Scenario: Malformed body
- **WHEN** a message of a registered type arrives whose body is not valid JSON for that type
- **THEN** no handler is invoked, and the message is in the error queue after a single attempt
