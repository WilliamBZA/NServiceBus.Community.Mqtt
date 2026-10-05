# Spec Delta

## Purpose

Defines how a nanoFramework device is configured and run as an NServiceBus endpoint over MQTT. It covers the endpoint's name and queue, the broker connection, start and stop, reconnects and critical errors, and the delivery limits that come from acknowledging messages on receipt.

## ADDED Requirements

### Requirement: A device is an endpoint with its own queue
Every device endpoint SHALL have an endpoint name that is also its queue address. The address SHALL map to an MQTT topic in the same way the .NET transport maps addresses (`_` becomes `/`). The queue SHALL be the same persistent MQTT 5 session, with the same client ID, that a .NET endpoint of that name would use. The broker SHALL keep messages sent to the device's address while the device is offline, from the device's first start until the session expires. The configuration SHALL reject an endpoint name that is empty or whitespace, contains `+` or `#`, or starts with `$`, at the moment it is created, with an error that names the endpoint name.

#### Scenario: .NET endpoint routes a command to a device
- **WHEN** a device runs as endpoint `Gate_01`, and a .NET endpoint using the MQTT transport sends a command routed to `Gate_01`
- **THEN** the device's handler for that command is invoked

#### Scenario: Message sent while the device is offline
- **WHEN** a device has started at least once and is then powered off, a .NET endpoint sends a message to the device's address, and the device starts again before its session expires
- **THEN** the device receives the message

#### Scenario: Invalid endpoint name
- **WHEN** a device endpoint configuration is created with the name `gate/#`
- **THEN** creating it fails with an argument error that names `gate/#` and explains that addresses cannot contain `+` or `#` or start with `$`

### Requirement: Broker connection settings
The configuration SHALL take the broker's host and port, and optionally a username and password. It SHALL reject an empty host, and a port outside 1–65535, when it is created. The device SHALL connect with MQTT 5 and SHALL send the username and password when they are set. The session expiry SHALL default to 7 days and SHALL be settable to a value from one second up to the largest interval MQTT 5 can express. The device SHALL send it to the broker with every connection.

#### Scenario: Invalid port
- **WHEN** a configuration is created with port `0` or `70000`
- **THEN** creating it fails with an argument error that names the port

#### Scenario: Credentials are used
- **WHEN** the broker requires a username and password and the configuration sets them correctly
- **THEN** the device starts and receives messages

#### Scenario: Credentials rejected
- **WHEN** the broker rejects the configured username and password
- **THEN** starting fails with an error that names the host, the port and the reason the broker gave

### Requirement: Starting a device endpoint
Starting SHALL do the following before it returns:
- Connect to the broker and resume the device's queue session.
- Subscribe to the queue's topic and to the topics of every event the device subscribes to.
- Wait for the broker to confirm those subscriptions.

If the broker cannot be reached, starting SHALL fail with an error that names the host and the port. A failed start SHALL leave no connection or background thread running.

#### Scenario: Broker unreachable at start
- **WHEN** a device endpoint is started and nothing listens at the configured host and port
- **THEN** starting fails with an error naming the host and the port, and no connection or background thread is left running

#### Scenario: Backlog delivered after start
- **WHEN** messages are waiting in the device's queue session and the device starts
- **THEN** the device handles each of them

### Requirement: One message at a time
A device endpoint SHALL handle one received message at a time, in the order the broker delivered them. It SHALL finish a message, including its immediate retries and any forwarding to the error queue, before it starts the next one.

#### Scenario: Sequential processing
- **WHEN** two messages arrive and the handler of the first one blocks
- **THEN** the handler of the second message is not invoked until the first one's processing has finished

### Requirement: Messages are acknowledged on receipt
A device endpoint SHALL acknowledge each message to the broker when the message arrives, before it is handled. If the device stops, crashes, reboots or loses power before a received message's processing is complete, the broker SHALL NOT redeliver that message. Messages that the broker is still holding in the device's session at that moment SHALL NOT be affected. The README SHALL describe this limit and how it differs from the .NET transport, which acknowledges after handling.

#### Scenario: Power lost during handling
- **WHEN** the device loses power while a handler is running, and the device starts again
- **THEN** that message is not delivered again

#### Scenario: Broker-held backlog survives a reboot
- **WHEN** messages are sent to the device's address while it is powered off, and the device then starts
- **THEN** the device receives those messages

### Requirement: Stopping a device endpoint
Stopping SHALL do the following, in order:
- Stop taking new messages from the intake.
- Let the message in progress finish.
- Process the messages that have already arrived, until they are done or a drain timeout passes.
- Disconnect from the broker in a way that keeps the session and its subscriptions.

The endpoint SHALL log how many received messages were left unprocessed and so are lost. A second stop SHALL do nothing. Sending, publishing, subscribing or unsubscribing after stop SHALL fail with an error.

#### Scenario: Stop keeps the session
- **WHEN** a device endpoint is stopped, a message is sent to its address, and the device starts again before the session expires
- **THEN** the device receives the message

#### Scenario: Stop waits for the handler
- **WHEN** a handler is running and stop is requested
- **THEN** stop returns only after the handler has returned

#### Scenario: Send after stop
- **WHEN** the application sends a message after the endpoint has stopped
- **THEN** the send fails with an error and nothing is published

### Requirement: Reconnecting after a lost connection
If the connection to the broker is lost while the endpoint is running, the endpoint SHALL reconnect without being restarted:
- Retry with a back-off that starts at one second and doubles up to 30 seconds.
- Log every failed attempt.
- Subscribe again to every topic.
- Resume processing.

The back-off SHALL go back to one second after a successful reconnect. This requirement does not apply to a connection that was lost because another client took over the session (see the next requirement).

#### Scenario: Broker restarts
- **WHEN** the broker is restarted while a device endpoint is running, and a message is sent to the device after the broker is back
- **THEN** the device reconnects and handles the message without being restarted

#### Scenario: Network drop
- **WHEN** the device's network connection is down for two minutes and then comes back
- **THEN** the device has logged its failed attempts, reconnects, and receives the messages the broker held for it

### Requirement: One device per endpoint name
Every device SHALL need its own endpoint name, and the README SHALL say so. If the broker disconnects the device because another client took over its session (MQTT 5 reason code `0x8E`), the device SHALL raise a critical error. The error SHALL:
- name the address
- explain that only one consumer per queue is supported
- say that every device needs its own endpoint name

This is the same behavior as the .NET transport. The device SHALL NOT reconnect after a takeover, so that the two clients do not take the session off each other indefinitely. From then on, sends and publishes SHALL fail with an error saying that the endpoint was displaced.

#### Scenario: Second device with the same name
- **WHEN** a device is running as `Gate_01` and a second device starts as `Gate_01`
- **THEN** the first device raises a critical error naming `Gate_01` and stops receiving, and the second device receives the messages sent to `Gate_01` from then on

#### Scenario: Displaced device does not reconnect
- **WHEN** a device has been displaced and a minute passes
- **THEN** it has not reconnected, and the other client's session has not been interrupted

### Requirement: Critical errors
The application SHALL be able to register a callback for critical errors that receives a description and the exception, if there is one. If no callback is registered, critical errors SHALL be logged at error level. A critical error SHALL NOT stop the endpoint by itself. Critical errors SHALL be raised for at least these cases:
- A message cannot be forwarded to the error queue.
- Another client took over the device's session.
- An unexpected failure of the endpoint's own processing loop.

#### Scenario: Error queue unreachable
- **WHEN** a message has exhausted its immediate retries and the broker is unreachable when the device tries to forward it to the error queue
- **THEN** the registered critical error callback is invoked with a description that names the message ID and the error queue

### Requirement: Maximum packet size
The device SHALL declare a maximum packet size to the broker when it connects. The size SHALL be configurable and SHALL default to 16384 bytes, so that the broker does not send the device messages larger than it can hold. Sending or publishing a message whose MQTT packet would be larger than the configured size SHALL fail with an error that names both sizes, before anything is published. The README SHALL state that the broker drops a larger message addressed to the device without telling the sender.

#### Scenario: Outgoing message too large
- **WHEN** the application sends a message whose packet would be larger than the configured maximum
- **THEN** the send fails with an error naming the packet size and the limit, and nothing is published

#### Scenario: Incoming message too large
- **WHEN** a .NET endpoint sends the device a message larger than the device's maximum packet size, followed by a small message
- **THEN** the device never receives the large message and handles the small one
