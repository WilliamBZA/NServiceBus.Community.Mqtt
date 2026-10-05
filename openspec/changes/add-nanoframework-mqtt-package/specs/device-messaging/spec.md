# Spec Delta

## Purpose

Defines how a device sends and receives NServiceBus messages so that devices and .NET endpoints using `NServiceBus.Community.Mqtt` understand each other. It covers the wire format, the standard headers, routing, handler invocation, replies, dispatch from handlers and time to be received.

## ADDED Requirements

### Requirement: Same wire format as the .NET transport
Every message a device dispatches SHALL use the .NET transport's payload format: the JSON envelope with the message ID, the headers, and the body as base64. A device SHALL decode every payload the .NET transport produces, including:
- header keys and values with non-ASCII characters
- JSON escape sequences in any string
- empty bodies

Message bodies SHALL be UTF-8 JSON, and the `NServiceBus.ContentType` header SHALL be `application/json`. Bodies a device writes SHALL be readable by NServiceBus's System.Text.Json serializer with its default settings. Bodies that serializer writes SHALL be readable by a device, for the supported member types.

#### Scenario: Unicode headers from .NET
- **WHEN** a .NET endpoint sends a device a message with header `a-😅-B7=a-😍-b`
- **THEN** the device's handler sees that header with the same key and value

#### Scenario: Device message read by .NET
- **WHEN** a device sends a .NET endpoint a message
- **THEN** the .NET handler receives a typed message whose property values equal the ones the device set, and the custom headers the device added

### Requirement: Supported message member types
Message types on a device SHALL be classes with a public parameterless constructor and public read/write properties. A device and a .NET endpoint SHALL exchange, unchanged within each type's precision, properties of these types:
- `string`, `bool`, `int`, `long` and `double`
- `DateTime` in UTC
- enums
- nested classes of the same shape
- arrays of any of these

The README SHALL list these types and say that other types are not supported.

#### Scenario: Every supported type round-trips
- **WHEN** a message with one property of each supported type is sent from a device to a .NET endpoint, and the same message is sent from a .NET endpoint to a device
- **THEN** each receiver sees the values the sender set

### Requirement: Standard headers on outgoing messages
Every message a device dispatches SHALL carry these headers:

| Header | Value |
|---|---|
| `NServiceBus.MessageId` | A new unique ID |
| `NServiceBus.MessageIntent` | `Send`, `Publish` or `Reply` |
| `NServiceBus.EnclosedMessageTypes` | The namespace-qualified names, without assembly, of the message type, its base classes and its interfaces. Excludes `object`, `System` types and the message marker interfaces. |
| `NServiceBus.ContentType` | `application/json` |
| `NServiceBus.ConversationId` | See below |
| `NServiceBus.CorrelationId` | See below |
| `NServiceBus.ReplyToAddress` | The device's own address |
| `NServiceBus.OriginatingEndpoint` | The device's endpoint name |
| `NServiceBus.TimeSent` | Time of dispatch, in UTC, in NServiceBus's wire format |

The conversation headers depend on where the message is sent from:

| Header | Sent while handling a message | Sent outside a handler |
|---|---|---|
| `NServiceBus.ConversationId` | The incoming message's conversation ID | A new ID |
| `NServiceBus.RelatedTo` | The incoming message's ID | Not set |
| `NServiceBus.CorrelationId` | The incoming message's correlation ID, or its message ID when it has none | The new message's own ID |

Headers the application adds to an outgoing message SHALL be sent unchanged, unless they are one of the headers above.

#### Scenario: Sent from a handler
- **WHEN** a device handler, handling message `M` with conversation ID `C`, sends a command
- **THEN** the command carries conversation ID `C`, `NServiceBus.RelatedTo` equal to `M`'s message ID, and `NServiceBus.ReplyToAddress` equal to the device's address

#### Scenario: Sent outside a handler
- **WHEN** the application sends a message from its main loop
- **THEN** the message has a new conversation ID, and its correlation ID equals its own message ID

#### Scenario: .NET endpoint resolves a device message type
- **WHEN** a device sends `Contracts.ReadingTaken`, whose `NServiceBus.EnclosedMessageTypes` holds no assembly name, to a .NET endpoint that has a handler for `Contracts.ReadingTaken`
- **THEN** the .NET handler is invoked

### Requirement: Sending and routing
A send SHALL go to the destination given with the send. Without one, it SHALL go to the endpoint configured as the route for the message type. A send with neither SHALL fail before anything is dispatched, with an error that names the message type. A destination SHALL be validated with the same rules as an endpoint name. Sending or replying with a type that implements the event marker SHALL fail, and so SHALL publishing a type that does not.

#### Scenario: Configured route
- **WHEN** `Contracts.OpenValve` is routed to `Plant` and the application sends an `OpenValve` without a destination
- **THEN** the message is delivered to `Plant`

#### Scenario: Explicit destination wins
- **WHEN** `Contracts.OpenValve` is routed to `Plant` and the application sends an `OpenValve` to `Plant_Backup`
- **THEN** the message is delivered to `Plant_Backup` and not to `Plant`

#### Scenario: No route
- **WHEN** the application sends a message type that has no route, without a destination
- **THEN** the send fails with an error naming the message type, and nothing is published

#### Scenario: Sending an event
- **WHEN** the application sends a type that implements the event marker
- **THEN** the send fails with an error that says events must be published

### Requirement: Dispatch waits for the broker
A send or publish from outside a handler SHALL return only after the broker has acknowledged every MQTT publish it makes. In these cases it SHALL fail with an error that names the destination and the broker's host and port:
- The device is not connected. Nothing SHALL be published.
- The broker rejects a publish.
- The broker does not acknowledge within the dispatch timeout (configurable, default 10 seconds). The README SHALL state that a message that timed out may still reach the broker.

The device SHALL NOT buffer messages to send later while it is offline.

#### Scenario: Broker unreachable
- **WHEN** the device has lost its broker connection and the application sends a message
- **THEN** the send fails at once with an error naming the destination, host and port

#### Scenario: Acknowledged send
- **WHEN** the broker is reachable and the application sends a message
- **THEN** the send returns after the broker acknowledged it, and the destination receives the message

### Requirement: Messages from a handler are dispatched after the handlers succeed
Messages sent, published or replied while a received message is being handled SHALL be collected. They SHALL be dispatched, in the order they were issued, only after every handler for the received message has completed. If a handler throws, none of them SHALL be dispatched. If dispatching them fails, the failure SHALL count as a processing failure of the received message, and immediate retries SHALL apply. Messages that were dispatched before the failure are dispatched again by the retry, and the README SHALL say so.

#### Scenario: Handler sends, then throws
- **WHEN** a handler sends a command and then throws on its first attempt, and succeeds on its second
- **THEN** the command is dispatched exactly once, after the second attempt

#### Scenario: Dispatch failure is retried
- **WHEN** a handler sends a command and the broker rejects the publish
- **THEN** the received message is handled again as an immediate retry

### Requirement: Message type resolution and handler invocation
Handlers SHALL be registered explicitly for a message type. A device SHALL find the type of a received message as follows:
- Read the namespace-qualified names in `NServiceBus.EnclosedMessageTypes`, ignoring any assembly part, in order.
- Pick the first name that matches a message type registered on the device.
- Deserialize the body into that type.

The device SHALL then invoke every handler registered for that type, or for one of its base classes or interfaces, once each, in the order they were registered. The handler context SHALL expose the incoming message ID, the reply-to address and the incoming headers.

#### Scenario: Assembly-qualified type from .NET
- **WHEN** a .NET endpoint sends `Contracts.OpenValve`, which `NServiceBus.EnclosedMessageTypes` lists with its assembly name and version, to a device with a handler for `Contracts.OpenValve`
- **THEN** that handler is invoked with the deserialized message

#### Scenario: Device only knows the base type
- **WHEN** a .NET endpoint publishes `Contracts.ValveOpened`, which derives from `Contracts.ValveEvent`, and the device has a handler only for `Contracts.ValveEvent`
- **THEN** the `ValveEvent` handler is invoked with a `ValveEvent` populated from the body

#### Scenario: Handlers for a type and its interface
- **WHEN** a device has a handler for `Contracts.ValveOpened` and a handler for the interface `Contracts.IAlarm`, and a `ValveOpened` that implements `IAlarm` arrives
- **THEN** each of the two handlers is invoked once

### Requirement: Reply
While handling a message, a handler SHALL be able to reply. The reply SHALL do the following:
- Go to the incoming message's `NServiceBus.ReplyToAddress`, with intent `Reply`.
- Carry the correlation and conversation headers described above.
- If the incoming message carries `NServiceBus.OriginatingSagaId` and `NServiceBus.OriginatingSagaType`, carry them as `NServiceBus.SagaId` and `NServiceBus.SagaType`, so the .NET saga that sent the request handles the reply.

A reply to a message that has no reply-to address SHALL fail, and the failure counts as a handler failure.

#### Scenario: Request and response with a .NET endpoint
- **WHEN** a .NET endpoint sends the device a request and the device's handler replies
- **THEN** the .NET endpoint's reply handler is invoked, and the reply's `NServiceBus.RelatedTo` equals the request's message ID

#### Scenario: Reply to a saga
- **WHEN** a .NET saga sends the device a request and the device's handler replies
- **THEN** the reply is handled by that saga instance

#### Scenario: No reply-to address
- **WHEN** a device handler replies to a message without `NServiceBus.ReplyToAddress`
- **THEN** the reply fails and the received message goes through recoverability

### Requirement: Time to be received
An outgoing message SHALL be able to carry a time to be received. The device SHALL send it in two forms:
- The MQTT 5 message expiry interval, in whole seconds, rounded up and never zero.
- The `NServiceBus.TimeToBeReceived` header.

Every copy of a published event SHALL carry the same interval. A time to be received larger than MQTT 5 can express SHALL mean that the message does not expire.

#### Scenario: Expired before the receiver starts
- **WHEN** a device sends a message with a time to be received of two seconds to a .NET endpoint that is stopped, and the endpoint starts four seconds later
- **THEN** the endpoint does not receive that message

#### Scenario: Not expired
- **WHEN** a device sends a message with a time to be received of thirty seconds to a running .NET endpoint
- **THEN** the endpoint receives it
