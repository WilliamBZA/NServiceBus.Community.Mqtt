# Proposal

## Why

This repository is a single flat project: eight `.cs` files and `NserviceBus.Mqtt.csproj` in the repo root, with no solution file, no tests, no shared build props and no CI. Every Particular and community NServiceBus transport instead uses a `src/` layout with a solution, shared build configuration, and three test projects: unit tests, transport tests and acceptance tests. The transport and acceptance suites come from the `NServiceBus.TransportTests.Sources` and `NServiceBus.AcceptanceTests.Sources` packages and run against a real broker.

Without those suites there is no evidence that the transport honours the NServiceBus transport seam contract. Reading the code, it doesn't:

- `StopReceive` returns immediately, and the stop token is unregistered before it can cancel in-flight handlers.
- `ChangeConcurrency` throws `NotImplementedException`, and `PushRuntimeSettings` is ignored.
- `Unsubscribe` is a no-op.
- Events are published to flat `events/{ShortTypeName}` topics, so polymorphic events and same-named types in different namespaces don't work.
- Nothing retains messages for an endpoint that is offline, or for the error and audit "queues".
- The pump and dispatcher never reconnect.

**Decisions made with the user while planning:**
- Do the full rename to NServiceBus conventions, including namespaces, as a breaking release.
- Treat the standard suites as the bar: fix the transport until they pass.
- Scale-out (competing consumers across instances of one endpoint) is **not** a requirement, because this transport targets IoT devices. Each queue has one consumer, and tests whose purpose is scale-out are recorded, approved exclusions.
- Tests run against a real Mosquitto 2.x broker. The test projects start it themselves in a Docker container, so the only prerequisite is a running Docker engine. This was decided on 2026-10-02, once Docker was installed, and replaces the earlier plan to default to an in-process MQTTnet broker.
- The transport supports time-to-be-received (TTBR), as the MQTT 5 message expiry interval. This was decided with the user on 2026-10-02, during implementation, because six acceptance tests use TTBR and the 8.1.6 suite has no gate to skip them. It replaces the original plan to declare TTBR unsupported.

## What Changes

- **Repository layout**: move to the standard shape.
  - A `src/` folder holds the solution, `Directory.Build.props`, `.editorconfig` and the projects.
  - The repo root keeps `README.md`, `LICENSE`, `global.json`, `nuget.config`, `.gitattributes`, `.gitignore` and `.github/workflows/`.
  - Add a `README.md` covering configuration, topology and running the tests.
- **Production project**: `NserviceBus.Mqtt.csproj` becomes `src/NServiceBus.Community.Mqtt/NServiceBus.Community.Mqtt.csproj`.
  - The PackageId stays `NServiceBus.Community.Mqtt`.
  - Sources are grouped into folders by concern (`Configuration/`, `Receiving/`, `Sending/`, `Subscriptions/`, `Infrastructure/`).
- **Test projects** (new):
  - `NServiceBus.Community.Mqtt.Tests`: unit tests for broker-independent logic and a public API approval test.
  - `NServiceBus.Community.Mqtt.TransportTests`: `NServiceBus.TransportTests.Sources` plus `ConfigureMqttTransportInfrastructure`.
  - `NServiceBus.Community.Mqtt.AcceptanceTests`: `NServiceBus.AcceptanceTests.Sources` plus `TestSuiteConstraints` and `ConfigureEndpointMqttTransport`.
- **Broker for tests**: each broker-backed test project starts its own Mosquitto 2.x container with Testcontainers, on a free host port, and removes it at the end of the run.
  - Setting the environment variables `MqttTransport_Server` and `MqttTransport_Port` points the tests at an existing broker instead, and no container is started.
  - Add a GitHub Actions workflow that builds and runs all three test projects once on a Linux runner. Docker is available there, and the fixture starts Mosquitto itself.
  - Add a Mosquitto configuration file, `src/mosquitto/mosquitto.conf`. The fixture mounts it into the container, and it shows the settings an existing broker needs.
- **Rename**: assembly `NServiceBus.Community.Mqtt`.
  - **BREAKING**: assembly identity changes from `NserviceBus.Mqtt`.
  - **BREAKING**: `MqttTransport` and its configuration surface move to the `NServiceBus` namespace; internals move to `NServiceBus.Transport.Mqtt`. The old `NserviceBus.Mqtt` namespace goes away.
- **Public API**: shrink to the transport definition and its settings.
  - **BREAKING**: `MqttMessagePump` and other implementation types become internal.
- **Transport fixes** needed to pass the suites:
  - Graceful stop and cancellation semantics.
  - Honouring and changing concurrency.
  - Correct `onError` handling and retry, with the wrong-variable exception filter fixed.
  - Reconnection.
  - Resource cleanup on shutdown.
  - Connection semaphores released in `finally`.
  - Real `Unsubscribe`.
  - Full-type-name, polymorphic event topics.
  - Durable single-consumer "queue" semantics for endpoint input, error and audit addresses. A second consumer on the same queue displaces the first, and the displaced endpoint reports a critical error naming the queue.
  - Declared capabilities (transaction modes, pub/sub, delayed delivery, TTBR) that match what is implemented.
  - **BREAKING**: event topic names change from `events/{ShortTypeName}` to a name derived from the full type name. 1.x and 2.x endpoints will not exchange events.
- **Version**: package version moves to `2.0.0`. Publishing is out of scope.
- **Unchanged**: the JSON `MessageWrapper` wire format and the `_` → `/` address translation stay as they are, so 1.x and 2.x endpoints keep interoperating where broker semantics allow.

**Expected broker-visible behaviour change (BREAKING).** Endpoint input queues become durable and single-consumer, where today every instance receives every message and nothing survives a disconnect. Brokers need persistent sessions enabled, and MQTT 5 is required. Running two instances of one endpoint on the same queue is no longer supported: the second displaces the first, and the first reports a critical error. Each device or instance needs its own endpoint name. The design confirms the mechanism in an up-front spike.

## Capabilities

### New Capabilities
- `transport-project-structure`: required repository, solution, project, naming and CI layout for the transport, and the broker-backed test environment.
- `transport-public-api`: namespaces, assembly identity, the public surface and the declared transport capabilities.
- `message-receiving`: receive pump behaviour: queue semantics, concurrency, acknowledgement, recoverability integration, stop and cancellation, reconnection.
- `message-dispatching`: send, publish and send-only behaviour, including headers, body, message ID, failure surfacing and durability to offline consumers.
- `publish-subscribe`: native pub/sub: subscribe, unsubscribe, polymorphic events, topic naming and per-endpoint delivery.
- `transport-test-suites`: the standard NServiceBus suites as the acceptance bar, how skips and approved exclusions work, which brokers the suites run against, and which unit tests must exist.

### Modified Capabilities
<!-- None: openspec/specs/ is empty, so there is no existing capability to modify. -->

## Impact

- **Code**: all existing `.cs` files move and are namespace-renamed. `MqttMessagePump`, `MqttDispatcher`, `MqttSubscriptionManager` and `MqttTransportInfrastructure` are substantially reworked.
- **Public API and compatibility**: the assembly, namespaces and public types change (see **BREAKING** above). Existing consumers must update `using` directives and package references, and re-test against their broker's persistence settings.
- **Dependencies**:
  - Existing: `MQTTnet` 4.3.3.952 and `NServiceBus` 8.1.6 stay.
  - New for tests: `NServiceBus.TransportTests.Sources` 8.1.6, `NServiceBus.AcceptanceTests.Sources` 8.1.6, `NServiceBus.AcceptanceTesting` 8.1.6, `NUnit` 3.x (the 8.1.6 Sources packages require `[3.13.3, 4.0.0)`), `NUnit3TestAdapter`, `Microsoft.NET.Test.Sdk`, `Particular.Analyzers`, `Testcontainers` (starts the Mosquitto container), and `PublicApiGenerator` with `Particular.Approvals` for the API approval test.
- **Systems**: broker-backed test runs need a running Docker engine with Linux containers, because the test projects start Mosquitto 2.x themselves. Without Docker, point `MqttTransport_Server` and `MqttTransport_Port` at an existing Mosquitto 2.x. CI uses a Linux runner, which has Docker.
- **Scope risk**: this is a large change. The task list is phased so that structure and test harness land first, then transport fixes. It can be split into two changes at that boundary if wanted.
