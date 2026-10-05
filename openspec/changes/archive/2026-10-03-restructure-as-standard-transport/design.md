# Design

## Context

See `proposal.md` for motivation and `specs/` for the required behavior. This covers the *how*.

**Current state** (read from the code):
- One project with no tests.
- `MqttMessagePump` opens a new `IMqttClient` with a random client ID and `CleanStart=true`. It subscribes to the queue topic and to event topics with plain subscriptions.
- There is no reconnect logic.
- `MqttDispatcher` holds one client and publishes with QoS 1. Multicast operations go to `events/{Type.Name}`.
- Pump and dispatcher serialise connects through a static `SemaphoreSlim` that is not released in `finally`.
- `StopReceive` does not wait for in-flight work, and its `CancellationToken.Register` is disposed immediately, so cancellation never propagates.
- `ChangeConcurrency` throws, and `Unsubscribe` is a no-op.

**Constraints from the standard suites**, read from the 8.1.6 `*.Sources` packages:
- The Sources packages require `NUnit [3.13.3, 4.0.0)`. They ship `contentFiles` for net472, net6.0 and net7.0; a net8.0 project resolves the net7.0 set.
- The transport suite discovers its adapter by name: a non-namespaced class `Configure{TransportDefinitionTypeName}Infrastructure`. For `MqttTransport` that is `ConfigureMqttTransportInfrastructure`.
- Transport tests derive queue names deterministically from the test name and transaction mode (e.g. `ReceivingMessageReceiveOnly`). Broker state therefore **repeats across runs** and must be cleaned.
- The acceptance suite needs a `partial TestSuiteConstraints`. Its `CreateTransportConfiguration()` returns our `IConfigureEndpointTestExecution`, and its `CreatePersistenceConfiguration()` can return the suite-shipped `ConfigureEndpointAcceptanceTestingPersistence`. That persistence has no outbox.
- Acceptance tests use `Requires.*` gates, which map to the constraint flags.
- `When_publishing_to_scaled_out_subscribers` runs **two instances of the same endpoint** (via `MakeInstanceUniquelyAddressable`) and asserts that an event is handled **exactly once** per endpoint. That needs competing consumers, which this transport does not provide (see D4), so it is a recorded, approved exclusion.
- Many acceptance tests nest same-named event types (`MyEvent`) in different test classes, which forces full-name topics.

## Goals / Non-Goals

**Goals:**
- A repository that matches the standard transport shape.
- A transport that passes the unmodified standard transport and acceptance suites, apart from the recorded exclusions, on a real Mosquitto 2.x broker.
- Tests that run with the .NET SDK and a running Docker engine, and nothing else: the test projects start the broker themselves.
- A small, intentional public API protected by an approval test.

**Non-Goals:**
- Native delayed delivery, `SendsAtomicWithReceive` and `TransactionScope`. These stay declared unsupported. (TTBR was on this list at first and is supported: see D3.)
- Scale-out: competing consumers across several instances of one endpoint. The transport targets IoT devices, where each device is its own endpoint. This rules out shared subscriptions.
- Brokers other than Mosquitto 2.x as verified targets. Other MQTT 5 brokers may work but are not tested.
- An in-process MQTTnet broker for tests. It was the earlier default, dropped once Docker was available because it is a weaker stand-in (see D7).
- A command-line tool for queue management, benchmarks, strong naming, and Particular's internal packaging pipeline (`Particular.Packaging`, MinVer). The package keeps its explicit `<Version>`.
- NServiceBus 9 or later. This change stays on 8.1.6.
- Changing the JSON wire format of `MessageWrapper` or the `_` → `/` address translation.
- Publishing the 2.0.0 package.

## Decisions

### D1. Layout, solution format, shared props
```
.github/workflows/ci.yml
README.md  LICENSE  .gitignore  .gitattributes  global.json  nuget.config
src/
  NServiceBus.Community.Mqtt.slnx
  Directory.Build.props  .editorconfig
  mosquitto/mosquitto.conf  KNOWN-EXCLUSIONS.md      (the conf is mounted into the test container)
  NServiceBus.Community.Mqtt/            (Configuration/ Receiving/ Sending/ Subscriptions/ Infrastructure/)
  NServiceBus.Community.Mqtt.Tests/
  NServiceBus.Community.Mqtt.TransportTests/
  NServiceBus.Community.Mqtt.AcceptanceTests/
```
- **Mirrors `Particular/NServiceBus.RabbitMQ`**, the reference repo I inspected: `src/` with a `.slnx`, `Directory.Build.props` and `.editorconfig`, and separate `Tests`, `TransportTests` and `AcceptanceTests` projects. The RabbitMQ `CommandLine` and `Benchmarks` projects are non-goals here.
- **`.slnx` over `.sln`**: it is the current Particular format and the one this user already uses in another repo. It needs SDK 9.0.200 or later; the machine has 10.0.401. `global.json` pins the 10.x SDK with `rollForward: latestFeature`, and the projects still target **net8.0** (the 8.0 runtime is installed).
- **Shared props**: `Directory.Build.props` sets `TargetFramework`, `Nullable`, `ImplicitUsings`, `LangVersion`, `Particular.Analyzers` and `TreatWarningsAsErrors` for the production project. Test projects relax the warnings they cannot control (suite sources are not ours to edit).
- **Files are moved with `git mv`** so history follows them.

*Alternatives:* keep the flat layout (rejected: the point of the change); `.sln` (works everywhere but diverges from current practice).

### D2. Names and public API
- The production assembly, package ID and root folder are `NServiceBus.Community.Mqtt`. The test projects are suffixed `.Tests`, `.TransportTests` and `.AcceptanceTests`.
- **Public API**, in namespace `NServiceBus`:
  - `MqttTransport : TransportDefinition`, with `Server` and `Port`
  - `SubscribeTo(string topic)`
  - a new `SessionExpiry` setting (default 7 days; see D4)

  Everything else is `internal`, in `NServiceBus.Transport.Mqtt`.
- **Test access**: `InternalsVisibleTo("NServiceBus.Community.Mqtt.Tests")` and `...TransportTests` and `...AcceptanceTests` for the cleanup hook in D7.
- **Type name `MqttTransport`** is kept because it determines the adapter name `ConfigureMqttTransportInfrastructure`.
- **Constructor validation**: throws `ArgumentException` for an empty server and `ArgumentOutOfRangeException` for a bad port.
- **Address validation** runs in `ToTransportAddress`, so it applies to receive, send, error and audit addresses uniformly. It rejects `+`, `#` and a leading `$` with a message naming the address.
- **The obsolete public `ToTransportAddress`** on `MqttTransport` remains the obsolete override NServiceBus requires. It delegates to the same internal mapper.
- The approval test uses `PublicApiGenerator` and `Particular.Approvals`; the approved file is committed.

### D3. Declared capabilities
- Supported transaction modes: **`None` and `ReceiveOnly`**, default `ReceiveOnly`. `None` is cheap on MQTT (acknowledge on receipt). Supporting it lets the many `[TestCase(None)]` transport tests exercise the pump instead of being ignored.
- `SupportsPublishSubscribe = true`, `SupportsDelayedDelivery = false`, `SupportsTTBR = true`.
- **TTBR is the MQTT 5 message expiry interval.** The dispatcher sets it from the operation's `DiscardIfNotReceivedBefore`, in whole seconds rounded up and never zero, and sets nothing for a message without one or with a limit MQTT cannot express. The broker drops an expired message, including one queued for an offline session; this was checked on Mosquitto 2.1.2 (a 2 s message queued for an offline persistent session was gone after 5 s, while a 60 s one and one with no expiry were delivered). Every copy of a published event carries the same interval.
  - *Decided with the user on 2026-10-02, when the acceptance suite showed that six tests use TTBR and the 8.1.6 suite has no capability gate for it. The first design declared TTBR unsupported.*
- Acceptance `TestSuiteConstraints`: `SupportsNativePubSub = true`, `SupportsDelayedDelivery = false`, `SupportsDtc = false`, `SupportsCrossQueueTransactions = false`, `SupportsPurgeOnStartup = true`, and `SupportsOutbox = false`. The suite-shipped persistence has no outbox, so outbox tests skip through `Requires.OutboxPersistence`.

### D4. MQTT mapping: a queue is a durable single-consumer session (central decision)
NServiceBus needs queue semantics that MQTT lacks. Scale-out is not a requirement, because the transport targets IoT devices, so shared subscriptions are not needed. The mapping requires **MQTT 5**, for session expiry and for server DISCONNECT reason codes:
- Each receiver connects with `ProtocolVersion=V500`, `CleanStart=false`, `SessionExpiryInterval=SessionExpiry` and `ReceiveMaximum` set to the concurrency cap. It uses `ClientId = nsb.{queue}`, derived from the receive address, so it is stable across restarts.
- Its input queue topic and all its event topics are subscribed with **plain** QoS 1 subscriptions in that session. The persistent session retains messages while the endpoint is offline. One session per queue gives one consumer per queue. Each subscribing endpoint has its own session, so each gets its own copy of an event.
- **Queue declaration at `Initialize`**, when `HostSettings.SetupInfrastructure` is true. For each receiver, connect with its session, apply the subscriptions, and disconnect. Messages sent before `StartReceive` are then retained.
  - For sending-only addresses (error, audit) that are not a local receiver address, create a holder session `nsb.{address}.declared` with a plain QoS 1 subscription, so messages dispatched to those addresses are retained. The holder uses a different client ID from `nsb.{address}` on purpose: declaring must never take over a live consumer of that address.
  - Tooling that wants to drain the retained messages connects as `nsb.{address}.declared`. An endpoint receiving on the same address uses its own `nsb.{address}` session, so the holder's copy is not consumed there. It accumulates until `SessionExpiry` or the broker's per-session queue cap, whichever comes first.
- **Session takeover**: a second client connecting with the same `ClientId` displaces the live one. The displaced pump receives DISCONNECT reason `0x8E` (`SessionTakenOver`) and reports a critical error naming the queue and explaining that only one consumer per queue is supported, so each instance needs its own endpoint name. It does **not** reconnect. The reconnect loop (D5) is for connection loss only, which avoids two instances fighting over one session.
- **Purge on startup**: done when the transport is initialized, not when receiving starts. NServiceBus runs feature startup tasks between the two, and they can send to the endpoint's own queue (acceptance test `When_purging_queues`), so a purge at `StartReceive` would delete the messages they sent. The queue is declared with `CleanStart=true`, which discards its stored session, subscriptions and queued messages and creates a fresh session. When the host does not set up infrastructure, the transport only deletes the session (a clean start with a session expiry of zero), and the pump creates a new one when it starts.
  - *Found while running the acceptance suite (task 9.2). The first design purged on the pump's first connection.*
- **Wire format and the `_` → `/` mapping are unchanged**; only the subscribe side changes.

*Alternatives considered:*
- **Shared subscriptions** (`$share/{queue}/{topic}`) with persistent sessions and per-instance client IDs: supports scale-out, but depends on the broker queueing shared-group messages for offline sessions, and it needs a stable per-instance ID. Rejected because scale-out is not required.
- **Keep `CleanStart=true` with plain subscriptions** (current): no retention, so it cannot pass the suites.
- **Application-level queue** (a store in front of the broker): defeats the point of using MQTT.

Spike **S1–S6** in `tasks.md` (section 1) verifies these assumptions on Mosquitto 2.x before any transport code is rewritten. It is a decision gate: if persistent-session retention does not work on Mosquitto 2.x, implementation stops and the user is asked how to proceed. The spike first ran on an in-process MQTTnet broker only (results in `spike-notes.md`); those results are kept as a comparison but no longer gate anything.

### D5. Receive pump
- **Intake**: with manual acknowledgement, the MQTTnet `ApplicationMessageReceivedAsync` handler only enqueues the message into a bounded channel and returns, so the client's read loop and keep-alive are never blocked by handlers.
- **Workers**: a worker loop takes messages from the channel and runs them under a concurrency limiter. `ChangeConcurrency` adjusts the limiter's capacity at runtime. `PushRuntimeSettings.MaxConcurrency` seeds it in `Initialize`.
- **`ReceiveMaximum`** bounds unacknowledged messages, which gives broker-side backpressure on brokers that honour it. Mosquitto 2.x is expected to, and spike S6 checks it. The bounded intake channel is the pump's own protection either way.
- **Acknowledgement**:
  - `ReceiveOnly` acknowledges after `onMessage` succeeds or `onError` returns `Handled`.
  - `None` acknowledges when the message is received, before handling.
  - A message neither acknowledged nor failed (cancelled stop) is left for redelivery.
- **Recoverability loop**: keeps the failure count and a fresh `ContextBag` per attempt, but one `ErrorContext` extension bag. It decodes the payload again for each attempt so header mutations never leak into retries. This fixes the existing `catch ... when (!ex.IsCausedBy(...))` filter, which checked the wrong exception variable.
- **Poison messages**: undecodable payloads are logged and acknowledged (discarded).
- **Stop**:
  1. Stop reading from the channel.
  2. Await in-flight workers.
  3. If the stop token fires, cancel the processing token. Workers swallow the resulting cancellation without calling `onError` or acknowledging.
  4. Disconnect with the session retained.

  Unprocessed buffered messages stay unacknowledged.
- **Reconnect**: a supervised loop on `DisconnectedAsync` with exponential back-off (1 s doubling to 30 s) and the same `ClientId`. If the broker reports no session, subscriptions are re-applied. A disconnect caused by session takeover (D4) is terminal and is not retried. A startup connection failure throws an exception that names host and port.
- **The `ReceiveSettings.UsePublishSubscribe` flag** gates `Subscriptions`, which returns `null` for instance-specific receivers.
- **Shared connection helper**: the static semaphore is replaced by a per-pump `SemaphoreSlim` released in `finally`.
- **`Dispose`** disposes the client, token sources and channel.

### D6. Dispatcher, publish and polymorphic events
- **Dispatcher**:
  - A single client with ID `nsb.dispatch.{guid}` and `CleanStart=true`.
  - `PublishAsync` is awaited and its result code checked, so a rejected publish throws, and an unreachable broker throws after one inline reconnect attempt.
  - Operations in a batch are published in order, and failures are wrapped with the destination.
  - The cancellation token flows through.
  - `Shutdown` disconnects and disposes, and is idempotent.
- **Event topics**: `events/{encoded full name}`. The encoder replaces `+` (nested types), `/`, `#`, `` ` `` and `[`, `]`, `,`, spaces and `*` with `.`, so the topic is valid and deterministic. Assembly-qualified parts are dropped.
- **Polymorphism, publisher side**: a multicast operation carries only the concrete event type. The dispatcher publishes **one copy per type in its hierarchy** (concrete type, base classes, interfaces), excluding `object`, `System.*` and the NServiceBus marker types `IMessage`, `IEvent` and `ICommand`. Each copy is a separate publish to that type's topic.
- **Polymorphism, subscriber side**: an endpoint subscribed to several types of one event would receive several copies. A pump therefore **processes only the designated copy**.
  - The designated type is the first type in the `NServiceBus.EnclosedMessageTypes` header, which lists the concrete type first, that the endpoint is subscribed to.
  - A copy arriving on any other subscribed topic is acknowledged and dropped.
  - The rule is stateless: it needs only the received topic, the endpoint's subscribed types and the header.
  - Messages that lack the header (raw device traffic) skip the rule.
- **`Subscribe`**: adds a plain subscription to `events/{encoded name}` to the pump's subscription set.
- **`Unsubscribe`**: removes it, and also drops the type from the subscribed-type set that the designated-copy rule uses.
- **Re-subscription**: the set is kept in memory and re-applied after a reconnect with no session.

*Alternatives considered:*
- **Wildcard hierarchy in the topic** (`events/{Concrete}/{Base}/…`): MQTT's `#` is only allowed at the end, and multiple inheritance of interfaces cannot be expressed.
- **Message-ID dedupe cache** on the subscriber: stateful, and it would also suppress legitimate redeliveries after a failed attempt.

### D7. Test projects and broker state
- **Packages**: NUnit 3.14.x, `NUnit3TestAdapter`, `Microsoft.NET.Test.Sdk`, the two `*.Sources` packages at 8.1.6, `NServiceBus.AcceptanceTesting` 8.1.6, and `Testcontainers` for the two broker-backed projects. The version of `Testcontainers` is chosen when the fixture is built (task 5.1).
- **Adapters, in the global namespace**:
  - `ConfigureMqttTransportInfrastructure` builds `ReceiveSettings` and calls `Initialize`.
  - `ConfigureEndpointMqttTransport` calls `UseTransport(new MqttTransport(...))`.
  - `TestSuiteConstraints` is the partial class from D3. It returns `ConfigureEndpointMqttTransport` and `ConfigureEndpointAcceptanceTestingPersistence`.
- **Test broker**: a non-namespaced `[SetUpFixture]` in each broker-backed project decides which broker to use.
  - **Default (variables unset)**: it starts a Mosquitto 2.x container with Testcontainers (image `eclipse-mosquitto:2`, container port 1883 on a random free host port) and maps `src/mosquitto/mosquitto.conf` into it. The project links that file as copied content so the fixture can find it. It waits until the broker accepts an MQTT connection, exposes host and mapped port to the adapters, and disposes the container at teardown. Testcontainers' resource reaper removes the container if the run is killed.
  - **Existing broker (`MqttTransport_Server` and `MqttTransport_Port` set)**: it starts nothing and probes the broker once with a TCP and MQTT connection.
  - **Fast failure**: with no Docker engine, or when the container does not become ready, the fixture throws within seconds with a message that Docker is required and that the two variables select an existing broker. For an unreachable existing broker the message names host, port and both variables.
  - **One container per project**: the transport and acceptance projects each start their own, so running them in parallel cannot mix sessions, and a crashed run leaves no stale broker state. The container is also what makes session retention real: sessions persist inside Mosquitto (`persistence true`) for the life of the run.
  - **Why not an in-process MQTTnet broker**: it was the earlier default. Spike results (`spike-notes.md`) showed it ignores `ReceiveMaximum` and caps offline queues at 250 by default. It also rejects `$share`, so it was never a faithful stand-in for a real broker. With Docker available it adds a second broker to keep in step for little benefit, so it is not used.
- **Isolation and repeatability**:
  - Queue names repeat across tests within a run, and sessions persist on the broker. Stale sessions must therefore be **addressable and deleted**. This matters between tests in one run, and across runs when an existing broker is selected with the variables.
  - Client IDs are deterministic (`nsb.{queue}`), so every stale session has a known ID. Adapters also use a short `SessionExpiry` (minutes), so a crashed run leaves sessions only briefly.
  - The adapters clear broker state in `Cleanup` and also before `Configure`. They compute the known client IDs with the production client-ID factory (`InternalsVisibleTo`) for the endpoint name, its instance-specific addresses, `error` and the audit address. Clearing a session means connecting with its `ClientId`, `CleanStart=true`, and disconnecting.
- **Approved exclusions**: scale-out tests cannot pass without competing consumers (see D4).
  - The `.AcceptanceTests` project sets `RunSettingsFilePath` to a `.runsettings` file whose `TestCaseFilter` excludes them, so `dotnet test` and CI both honour it and suite sources stay untouched.
  - `src/KNOWN-EXCLUSIONS.md` records each test, the limitation and the approval. The first entry is `When_publishing_to_scaled_out_subscribers`.
  - Any other test whose purpose is several instances sharing one input queue gets the same treatment, discovered when the suite is run. Any exclusion for another reason needs user approval first.
- **MQTT-specific tests**: separate files in `.TransportTests`, derived from the suite's `NServiceBusTransportTest` base. They cover what the suites do not: runtime concurrency changes, reconnect (a small in-test TCP proxy that can cut and restore connections), purge on startup, poison payloads, delivery between initialization and start, declared-address retention, wildcard address rejection, and takeover reporting.
- **Unit tests** cover address mapping, topic encoding, wire-format round trips (unicode and binary), validation, and the API approval. They use no broker.
- **`When_...` sources are never edited.** Any suppression (analyzer warnings, nullable) is configured in the test project's props.

### D8. CI and real-broker runs
- **`.github/workflows/ci.yml`** runs on `pull_request` and on `push` to `main`:
  1. `actions/setup-dotnet` installs the 10.x SDK and the 8.0 runtime.
  2. `dotnet build`.
  3. `dotnet test` on the three projects, once. The runner is `ubuntu-latest`, which has Docker, so the fixtures start Mosquitto themselves and the workflow has no broker step.
  4. Collect `.trx` results.
- **`src/mosquitto/mosquitto.conf`** sets `listener 1883`, `allow_anonymous true`, `persistence true`, and a high `max_queued_messages` and `max_inflight_messages`. Mosquitto 2.x refuses non-local anonymous connections without a config, and the container is reached from outside, so the file is required. The same file serves anyone running Mosquitto by hand for the variables override.
- **Local runs**: `dotnet test` with a running Docker engine (Docker Desktop with Linux containers on Windows). The README documents this, the two environment variables for an existing broker, and how to run Mosquitto by hand with the committed config.

### D9. Version and wire compatibility
- `<Version>` becomes `2.0.0`.
- The JSON `MessageWrapper` payload is untouched, so a 2.x endpoint can still read a 1.x unicast message.
- Event topic names change (D6), session handling changes (D4), and MQTT 5 is required, so 1.x and 2.x endpoints are not mixed for events. Queues are single-consumer, so two instances sharing one queue is no longer valid. The README and release notes say so.

## Risks / Trade-offs

- **Persistent-session retention on Mosquitto is still unverified** → the spike has so far run only on an in-process MQTTnet broker, where S1, S2, S3 and S5 passed. Spike S1 to S6 run on Mosquitto 2.x before any transport code changes. **Gate**: if retention fails on Mosquitto, stop and ask the user.
- **Docker is required to run broker-backed tests** → the fixture fails within seconds with a message that says so and names the variables for an existing broker. The unit-test project needs no Docker. CI uses a Linux runner, where Docker is available. The first run on a machine pulls the Mosquitto image, which takes longer.
- **No scale-out**: two instances on one queue produce a takeover critical error, not load balancing → the error says what to do (one endpoint name per instance), the README says so, and the scale-out acceptance tests are recorded exclusions. If scale-out later becomes a requirement, shared subscriptions can be revisited, but they need a broker that supports them.
- **Holder sessions for error and audit addresses accumulate copies** when an endpoint also receives on that address with its own session → bounded by `SessionExpiry` and the broker's per-session queue cap, documented, and skipped for local receiver addresses.
- **N publishes per event** for hierarchies. Cost grows with the number of interfaces and base types → marker and system types are excluded. Measure in acceptance runs, and optimise only if a test or benchmark shows a problem.
- **MQTT 5 requirement** drops very old brokers and 3.1.1-only clients → documented as a breaking change.
- **Broker-state leakage between tests**, because queue names are deterministic and sessions persist → pre- and post-cleanup, deterministic client IDs so stale sessions are addressable, and a short `SessionExpiry` in tests. Each project already has its own container per run, so leakage between projects and between runs cannot occur on the default path. If leakage between tests in one run proves flaky, the fallback is a fresh container per test fixture.
- **Unknown MQTTnet 4.3.3.952 API details** (manual acknowledgement, receive maximum, reason-code handling) → spike S4 confirms them against the real package before the pump is rewritten.
- **More exclusions may turn up** when the acceptance suite first runs → only tests whose purpose is several instances sharing one queue are pre-approved. Anything else stops for the user's approval.
- **Change size** → tasks are phased. Sections 2 to 5 (structure, helpers and test harness) are independently reviewable before the transport rework in sections 6 to 9, which is the split point if the change must be broken up.

## Migration Plan

1. **Move and rename** with `git mv` and compile, with no behaviour change. This is the first mergeable checkpoint.
2. **Add the test projects and harness.** At this point the suites are expected to fail.
3. **Rework the transport** against the failing suites until they pass.
4. **Consumers** (a 1.x → 2.x upgrade guide in the README):
   - Replace `using NserviceBus.Mqtt;` with `using NServiceBus;`.
   - Use an MQTT 5 broker with persistent sessions enabled.
   - Give each device or instance its own endpoint name. Two instances on one queue displace each other.
   - Upgrade all endpoints that exchange events together.
5. **Rollback**: the change is a normal git change. 1.0.0 stays on NuGet unchanged, and consumers can stay on it.
