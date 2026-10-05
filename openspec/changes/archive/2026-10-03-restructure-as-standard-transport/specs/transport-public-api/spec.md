# Spec Delta

## Purpose

Defines the package's public contract: assembly and namespace identity, the public surface, configuration validation, queue address mapping, and the NServiceBus capabilities the transport declares. These must match what the transport actually does.

## ADDED Requirements

### Requirement: Namespace and assembly identity
The transport definition and its configuration surface SHALL be in the `NServiceBus` namespace, so they are discoverable with the default NServiceBus `using`. Implementation types SHALL be in `NServiceBus.Transport.Mqtt`. The assembly SHALL be `NServiceBus.Community.Mqtt`. The `NserviceBus.Mqtt` namespace SHALL NOT exist in the package.

#### Scenario: Configuring an endpoint with the default usings
- **WHEN** an endpoint configuration file contains only `using NServiceBus;`
- **THEN** it can construct the MQTT transport and pass it to `UseTransport`

#### Scenario: Old namespace removed
- **WHEN** the built assembly is inspected
- **THEN** it contains no types in the `NserviceBus.Mqtt` namespace

### Requirement: Minimal public surface
The public API SHALL be limited to the transport definition and the settings needed to configure it. The message pump, dispatcher, subscription manager, infrastructure and wire-format types SHALL NOT be public. Public API changes SHALL be caught by an approval test, so they are always an explicit decision.

#### Scenario: Implementation types are not exposed
- **WHEN** the public API of the assembly is enumerated
- **THEN** it lists the transport definition and its settings and no receive, send or subscription implementation types

#### Scenario: Accidental API change fails the build
- **WHEN** a change adds or alters a public member without updating the approved API file
- **THEN** the unit tests fail

### Requirement: Configuration validation
The transport SHALL reject an empty or whitespace server name and a port outside 1–65535 when it is constructed, by throwing an argument exception that names the invalid argument.

#### Scenario: Missing server
- **WHEN** the transport is constructed with a null, empty or whitespace server
- **THEN** an `ArgumentException` (or `ArgumentNullException` for null) naming the server parameter is thrown

#### Scenario: Invalid port
- **WHEN** the transport is constructed with port `0` or `70000`
- **THEN** an `ArgumentOutOfRangeException` naming the port parameter is thrown

### Requirement: Queue address mapping
The transport SHALL map each NServiceBus queue address to a single MQTT topic by replacing `_` with `/`. The same mapping SHALL be used for receiving, sending, and the error and audit addresses. The transport SHALL reject, with a clear error naming the offending address, any address that contains the MQTT wildcard characters `+` or `#`, or that begins with `$`.

#### Scenario: Same address on both sides
- **WHEN** one endpoint sends to the address `Sales_Billing` and another endpoint receives on `Sales_Billing`
- **THEN** both map to the topic `Sales/Billing` and the message is delivered

#### Scenario: Wildcard in an address
- **WHEN** an endpoint is configured with an input address containing `#` or `+`
- **THEN** startup fails with an error that names the address and explains the restriction, and no wildcard subscription is created

### Requirement: Truthful capability declaration
The transport SHALL declare native publish/subscribe and time-to-be-received as supported, and delayed delivery as unsupported. It SHALL declare the transaction modes `None` and `ReceiveOnly` as supported, with `ReceiveOnly` as the default. `SendsAtomicWithReceive` and `TransactionScope` SHALL be reported as unsupported, so requesting them fails at endpoint startup through the standard NServiceBus mechanism. Every declared capability SHALL be backed by behavior covered by the standard test suites.

#### Scenario: Unsupported transaction mode requested
- **WHEN** an endpoint is configured with `TransportTransactionMode.TransactionScope`
- **THEN** endpoint startup fails with the standard unsupported-transaction-mode error

#### Scenario: Default transaction mode
- **WHEN** the transport is constructed and no transaction mode is set
- **THEN** the transaction mode is `ReceiveOnly`
