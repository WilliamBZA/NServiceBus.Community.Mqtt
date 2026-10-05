# Proposal

## Why

`NServiceBus.Community.Mqtt` lets NServiceBus endpoints exchange messages through an MQTT broker, but it needs .NET 10 and the full NServiceBus pipeline, which a microcontroller can't run. A device on .NET nanoFramework (an ESP32, for example) can only take part today by hand-writing the transport's JSON envelope, headers and topics. A small nanoFramework package that speaks the same wire contract lets a device be an NServiceBus endpoint in its own right: it receives commands, replies, publishes and subscribes to events, and retries failures the way NServiceBus does.

## What Changes

- A new nanoFramework class library and NuGet package, `NServiceBus.Community.NanoframeworkMQTT`. This is the requested name with its typo fixed. It runs on stock nanoFramework firmware, without generics (still a firmware preview) and without async. It connects with `nanoFramework.M2Mqtt` (MQTT 5) and serializes message bodies with `nanoFramework.Json`.
- **A device is an endpoint.** It has its own endpoint name and its own queue, using the same persistent MQTT 5 session layout as the .NET transport (`nsb.{name}`, topic from the `_` → `/` mapping). .NET endpoints route commands to a device the same way they route to any other endpoint.
- **Wire compatibility with `NServiceBus.Community.Mqtt` 2.x.** The package uses the same JSON envelope, the standard NServiceBus headers, the same queue and event topics, one event copy per type in the hierarchy, and the same rule for picking the one copy to process. Message types match by namespace-qualified name, so contracts can be shared as source files.
- **Messaging.**
  - Send, to an explicit destination or through type routes.
  - Receive, with handlers registered explicitly (no assembly scanning).
  - Reply, including the headers that route a reply back to a saga.
  - Publish along the event's type hierarchy.
  - Subscribe and unsubscribe, plus automatic subscription to handled events.
  - Messages sent from a handler are dispatched only after the handlers succeed, as in NServiceBus.
- **Recoverability.**
  - Immediate retries: 5 by default, configurable.
  - After the last retry, the message goes to the error queue with the standard NServiceBus failure headers.
  - No delayed retries.
  - Payloads that can't be decoded are logged and discarded.
  - A message whose type is unknown or whose body can't be deserialized goes straight to the error queue, without retries.
- **Time to be received** on device sends, as the MQTT 5 message expiry. This is **gated** on a small contribution to `nanoFramework.M2Mqtt`: a `Publish` overload that takes the expiry interval. The rest of the package doesn't wait for it.
- **Connection.**
  - Settings: host, port, optional username and password, session expiry, and maximum packet size.
  - A lost connection is re-established with back-off.
  - Sends wait for the broker's acknowledgement and throw on failure or timeout. Nothing is buffered offline.
- **Limit of stock M2Mqtt, documented and accepted.** A message is acknowledged to the broker when it arrives, before it is handled. A message that is in progress or waiting when the device reboots, crashes or stops is lost.
- **One device per endpoint name**, as with the .NET transport. If another client takes over the device's session, the device raises a critical error and does not reconnect. M2Mqtt exposes the broker's DISCONNECT reason code, so the takeover is detected exactly.
- **Repository.**
  - New nanoFramework projects under `src/`: the library, unit tests that run on the nanoCLR virtual device, and a sample device app.
  - A separate nanoFramework solution.
  - `src/Interop/`, shared source for the sample contracts and the golden wire payloads.
  - A guard in `src/Directory.Build.props` so the nanoFramework projects don't inherit the .NET 10 settings.
  - A Windows CI job for the nanoFramework projects.
  - Wire-contract tests on the .NET side.
  - A .NET interop host console app for a documented manual test on a real board.
  - A README section for the device package.
- The .NET transport's behavior, public API and wire format do not change.

## Capabilities

### New Capabilities

- `device-endpoint`: configuring and running a device endpoint. Covers the name and queue, broker connection and credentials, session expiry, start and stop, reconnect, critical errors, message size limit, and the documented acknowledgement and takeover limits.
- `device-messaging`: wire compatibility with the .NET transport, standard headers, send and routing, receiving and handler invocation, message type resolution, reply, batched dispatch from handlers, and time to be received.
- `device-recoverability`: immediate retries, forwarding to the error queue with failure headers, and handling of undecodable payloads, unknown types and undeserializable bodies.
- `device-publish-subscribe`: event topics, publishing along the type hierarchy, subscribe, unsubscribe and auto-subscribe, the one-copy-per-endpoint rule, and polymorphic handler invocation.
- `device-interop-verification`: how interoperability is proven. Covers device unit tests on the nanoCLR virtual device, golden wire payloads checked by both the device tests and the .NET tests, and the interop host with the manual board procedure.

### Modified Capabilities

- `transport-project-structure`: the repository layout, project naming, shared build configuration, CI and README requirements expand to include the nanoFramework projects, the second solution, the `src/Interop/` shared sources and the interop host. The supported-platform requirement is narrowed to the .NET transport and its .NET projects.

## Impact

- **New code**:
  - `src/NServiceBus.Community.NanoframeworkMQTT/` (`.nfproj`, `.nuspec`)
  - `src/NServiceBus.Community.NanoframeworkMQTT.Tests/` (nanoFramework unit tests)
  - `src/NServiceBus.Community.NanoframeworkMQTT.Sample/` (device app)
  - `src/NServiceBus.Community.NanoframeworkMQTT.sln`
  - `src/Interop/` (shared sources)
  - `src/NServiceBus.Community.NanoframeworkMQTT.InteropHost/` (.NET 10 console, in the existing `.slnx`)
- **Existing files**:
  - `src/Directory.Build.props`: guard on the `.csproj` extension.
  - `src/NServiceBus.Community.Mqtt.slnx`: adds the interop host.
  - `src/NServiceBus.Community.Mqtt.Tests`: wire-contract tests, with links to the `src/Interop/` files.
  - `src/NServiceBus.Community.Mqtt`: the pump's `EnclosedMessageTypeNames` parser moves into an internal helper so that the shared mapping cases can test it. Behavior does not change.
  - `.github/workflows/ci.yml`: new Windows job.
  - `README.md`.
- **Dependencies (device package)**: `nanoFramework.CoreLibrary`, `nanoFramework.M2Mqtt`, `nanoFramework.Json`, `nanoFramework.System.Collections`, `nanoFramework.System.Text`, `nanoFramework.Logging`, `nanoFramework.Runtime.Events`. Versions are pinned to one nanoFramework release set, and the device firmware must match it.
- **External**: a pull request to `nanoframework/nanoFramework.m2mqtt` that adds message expiry to `Publish`. TTBR waits for a release that includes it.
- **Build tooling**: the nanoFramework projects build only on Windows, with MSBuild and the nanoFramework build components. `dotnet build` of the existing `.slnx` on Linux is unaffected.
- **Consumers of `NServiceBus.Community.Mqtt`**: no change. The .NET transport only receives messages from devices, and those use its existing wire format.
- **Out of scope**:
  - Sagas, outbox and persistence.
  - Delayed delivery and delayed retries.
  - Audit forwarding.
  - Raw topic subscriptions.
  - TLS and client certificates.
  - Send-only devices and offline send buffering.
  - Concurrent processing.
  - Unobtrusive message conventions, dependency injection and pipeline extensibility.
  - ServiceControl integration (ServiceControl only supports Particular's own transports).
  - Publishing the package to nuget.org.
