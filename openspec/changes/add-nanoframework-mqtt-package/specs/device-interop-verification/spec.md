# Spec Delta

## Purpose

Defines how the repository proves that devices and .NET endpoints interoperate. Device code cannot reach a live broker in CI, so the proof uses device unit tests, a shared set of golden wire payloads that both sides check, and a manual end-to-end check on a real board.

## ADDED Requirements

### Requirement: Device unit tests run without a broker or hardware
The device package SHALL have unit tests that run on the nanoCLR virtual device, with no broker and no hardware. A test double SHALL replace the broker connection. The tests SHALL cover:
- address and client ID mapping and validation
- envelope encoding and decoding
- outgoing headers and routing
- type resolution and handler invocation
- dispatch from handlers after success
- immediate retries and forwarding to the error queue
- messages that skip retries
- event topics and the type hierarchy
- the one-copy rule
- reply and saga headers
- reconnect back-off and session takeover handling
- stop

#### Scenario: No broker available
- **WHEN** the device unit tests are run on a machine with no MQTT broker and no device attached
- **THEN** they build, run on the nanoCLR virtual device, and pass

### Requirement: Golden wire payloads are checked on both sides
The repository SHALL hold one set of golden wire payloads, together with the sample message contracts they use. Both are kept as shared source, compiled into the device unit tests and into the .NET unit tests. Each payload has an origin, and both sides check it:

| Payload origin | .NET unit tests | Device unit tests |
|---|---|---|
| .NET | Produce it with the .NET transport's encoding and NServiceBus's JSON serializer, and check that the result matches | Decode it to the expected headers and typed message |
| Device | Decode it with the .NET transport and deserialize its body with NServiceBus's serializer, and check the expected values | Produce it with the device's encoding from the sample inputs, and check that the result matches |

The samples SHALL cover at least:
- a command
- a reply with saga headers
- an event with a base class and an interface
- a failed message with failure headers
- header keys and values with non-ASCII and escaped characters
- an empty body
- every supported body member type

The .NET unit tests SHALL also check two things against NServiceBus 10's own definitions, wherever NServiceBus exposes them: the failure header names, and the formats of `NServiceBus.TimeSent` and `NServiceBus.TimeOfFailure`.

#### Scenario: The .NET side changes its format
- **WHEN** a change makes the .NET transport encode a sample differently from its golden payload
- **THEN** a .NET unit test fails

#### Scenario: The device side changes its format
- **WHEN** a change makes the device encode a sample differently from its golden payload
- **THEN** a device unit test fails

### Requirement: Interop host and manual board test
The repository SHALL include a .NET console app, the interop host, that works with a broker and a device running the sample device app. It SHALL run the checks below and report each one as passed or failed. It SHALL exit with a non-zero code if any check fails. The checks are:
- A .NET command is handled by the device, which replies, and the reply is correlated to the command.
- A device event reaches a .NET subscriber.
- A .NET event reaches the device through a subscription to its base type.
- A message the device always fails ends up in the error queue, with failure headers, after the configured retries.
- A device message with a time to be received expires, once time to be received is available.

The README SHALL describe the procedure: start a broker, configure and flash the sample to a board, then run the interop host. This test SHALL be run by hand, not in CI. CI SHALL still build the interop host and the sample device app.

#### Scenario: Board test passes
- **WHEN** a contributor follows the README with a supported board, a broker and the interop host
- **THEN** the interop host reports every check as passed and exits with code 0

#### Scenario: Interop failure is visible
- **WHEN** the device does not reply within the interop host's timeout
- **THEN** the interop host reports the request/reply check as failed and exits with a non-zero code
