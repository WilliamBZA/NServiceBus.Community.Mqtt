# transport-test-suites Specification

## Purpose

Defines the test suites that prove the transport works and the bar they must meet. It uses the official NServiceBus transport and acceptance suites, plus focused unit tests, so conformance is checked rather than assumed.

## Requirements

### Requirement: Official suites are included unmodified
The repository SHALL include the NServiceBus transport test suite and acceptance test suite by referencing the official `NServiceBus.TransportTests.Sources` and `NServiceBus.AcceptanceTests.Sources` packages, at versions that match the NServiceBus version the transport depends on. The suite sources SHALL NOT be copied into the repository or edited. Transport-specific wiring (adapters, constraints, fixtures) and any additional MQTT-specific tests, for behavior the standard suites do not cover, SHALL live in separate files from the suite sources.

#### Scenario: Version alignment
- **WHEN** the NServiceBus dependency version is read from the production project
- **THEN** the Sources packages and the acceptance testing package use that same version

#### Scenario: Suite sources are untouched
- **WHEN** the test projects are inspected
- **THEN** the suite test sources exist only as package content, and the repository files in those projects are adapters, constraints, fixtures, helpers and MQTT-specific tests

### Requirement: MQTT-specific behavior has its own tests
Behavior required by the specs but not covered by the standard suites SHALL be covered by additional broker-backed tests. This includes: runtime concurrency changes, reconnect after connection loss, purge on startup, poison payloads, delivery between initialization and start, retention for declared sending addresses, wildcard address rejection at startup, and takeover reporting when a second consumer uses the same queue.

#### Scenario: Reconnect is verified
- **WHEN** the connection between an endpoint and the broker is cut and then restored during a test
- **THEN** a test asserts that the endpoint resumes receiving without being restarted

#### Scenario: Takeover is verified
- **WHEN** a second consumer starts on an address that already has a live consumer
- **THEN** a test asserts that the first consumer reports a critical error naming the address and does not reconnect

### Requirement: Declared capabilities drive skips
Acceptance test constraints SHALL report native publish/subscribe as supported, and delayed delivery, DTC, cross-queue transactions as unsupported, in agreement with the transport's declared capabilities. They SHALL report purge-on-startup and outbox support according to what is implemented and the persistence used. Tests SHALL be skipped only through the suites' own capability gates and transaction-mode checks, which correspond to capabilities the transport declares unsupported.

#### Scenario: Unsupported capability is skipped, not failed
- **WHEN** the acceptance suite reaches a test that needs delayed delivery
- **THEN** the test is reported as ignored through the suite's capability gate and is not counted as a failure

### Requirement: Suites pass on Mosquitto 2.x
The transport tests and acceptance tests SHALL complete with zero failing tests against a Mosquitto 2.x broker. Tests SHALL NOT be edited to make a run pass. They SHALL NOT be ignored or filtered out except through the suites' own capability gates or an approved exclusion.

#### Scenario: Green run on Mosquitto
- **WHEN** all test projects are run, with the broker-backed projects starting Mosquitto 2.x themselves
- **THEN** every project reports zero failed tests, and the only ignored or excluded tests are capability-gated or recorded approved exclusions

#### Scenario: Failure on Mosquitto
- **WHEN** a test fails against Mosquitto
- **THEN** the change is not complete until the failure is fixed or an approved exclusion is recorded

### Requirement: Approved exclusions
Scale-out of one input queue across several instances is not a supported capability, because the transport targets IoT devices. Tests whose purpose is several concurrent instances of one endpoint sharing its input queue, starting with `When_publishing_to_scaled_out_subscribers`, SHALL be pre-approved exclusions. Every exclusion SHALL be recorded in `src/KNOWN-EXCLUSIONS.md` with the test name, the limitation and the approval. Every exclusion SHALL be applied through a test filter in the test project's run settings, so suite sources stay untouched. Any other exclusion SHALL need the user's explicit approval before it is added, and SHALL be recorded the same way. The filter and the record SHALL list the same tests.

#### Scenario: Scale-out test is excluded and recorded
- **WHEN** the acceptance run is configured
- **THEN** `When_publishing_to_scaled_out_subscribers` is excluded by the run-settings filter and has an entry in `src/KNOWN-EXCLUSIONS.md` naming the limitation and the approval

#### Scenario: Other failing tests are not silently excluded
- **WHEN** a test other than a pre-approved scale-out test fails because of an MQTT limitation
- **THEN** it is not added to the filter until the user has approved it

#### Scenario: Filter and record agree
- **WHEN** the run-settings filter and `src/KNOWN-EXCLUSIONS.md` are compared
- **THEN** every filtered test has a record, and every record has a filter entry

### Requirement: Repeatable runs on a shared broker
Tests SHALL use unique, test-scoped queue and topic names and SHALL clean up the broker state they create, so consecutive runs against the same broker pass without manual cleanup, and leftovers from an earlier failed run do not break a later run.

#### Scenario: Consecutive runs
- **WHEN** the full suite is run twice in a row against the same broker instance
- **THEN** both runs pass

#### Scenario: Leftover state from a crashed run
- **WHEN** a previous run against a long-lived broker (one selected through the environment variables) was killed mid-test and left sessions and queued messages behind
- **THEN** a subsequent run still passes

### Requirement: Fast failure when the broker cannot be started or reached
Broker-backed test projects SHALL check, once before running tests, that the broker is reachable with an MQTT connection. If a container is needed and the Docker engine is unavailable or the broker does not become ready, they SHALL fail quickly with a message that says Docker is required and names the environment variables that select an existing broker instead. If the environment variables select an existing broker and it cannot be reached, they SHALL fail quickly with a message that names the host and port and both variables. In neither case SHALL every test time out separately.

#### Scenario: Docker is not running
- **WHEN** the variables are unset and the Docker engine is not running
- **THEN** the run fails within seconds with a message that Docker is required and that `MqttTransport_Server` and `MqttTransport_Port` can select an existing broker instead

#### Scenario: Existing broker not running
- **WHEN** `MqttTransport_Server` and `MqttTransport_Port` are set and nothing listens on that address
- **THEN** the run fails within seconds with a message naming the host, port and both variable names

#### Scenario: Default needs only Docker
- **WHEN** the variables are unset and the Docker engine is running
- **THEN** the tests start their own Mosquitto container and run normally

### Requirement: Unit tests for broker-independent logic
The `.Tests` project SHALL cover, without a broker: queue address mapping and rejection of wildcard and `$` addresses, event topic derivation for plain, nested and generic types, wire-format encoding and decoding including unicode headers and binary bodies, configuration validation, and the public API approval. They SHALL run in under a minute.

#### Scenario: Unit tests need no broker
- **WHEN** the `.Tests` project is run with no broker available
- **THEN** it builds and passes

#### Scenario: Topic derivation for a nested generic type
- **WHEN** the topic is derived for a nested generic event type
- **THEN** the result contains no MQTT wildcard characters and is identical on every call
