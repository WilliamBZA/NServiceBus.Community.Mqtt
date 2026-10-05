# Tasks

## 1. Tooling, scaffolding and the upstream request

- [x] 1.1 Check the current nanoFramework tooling on a Windows machine. Confirm three things: that the `.nfproj` project system builds with MSBuild and the nanoFramework build components, that tests run on the nanoCLR virtual device through VSTest, and whether `.slnx` and `InternalsVisibleTo` work. Record the answers in `design.md` (Open Questions). Verify: a throwaway test project runs one passing test on nanoCLR.
- [x] 1.2 Guard every group in `src/Directory.Build.props` with `Condition="'$(MSBuildProjectExtension)' == '.csproj'"`. Verify: `dotnet build src/NServiceBus.Community.Mqtt.slnx` succeeds and `dotnet test src/NServiceBus.Community.Mqtt.Tests` passes unchanged.
- [x] 1.3 Create `src/NServiceBus.Community.NanoframeworkMQTT.sln` with three projects: `NServiceBus.Community.NanoframeworkMQTT` (library, `packages.config` with exact pinned versions, `.nuspec` with ID `NServiceBus.Community.NanoframeworkMQTT` version `1.0.0`), `NServiceBus.Community.NanoframeworkMQTT.Tests` (`nanoFramework.TestFramework`) and `NServiceBus.Community.NanoframeworkMQTT.Sample` (ESP32 app). Verify: MSBuild builds the solution, one placeholder test passes on nanoCLR, and `nuget pack` produces `NServiceBus.Community.NanoframeworkMQTT.1.0.0.nupkg` with exact-version dependencies.
- [x] 1.4 Create `src/Interop/InteropContracts.cs` with the sample contracts, written in the shared C# subset (`#nullable disable`, no generics, records or LINQ). The contracts are:
  - `OpenValve` (command)
  - `ValveEvent` (base event) and `ValveOpened` (derives from it, implements `IAlarm`)
  - `IAlarm` and `IAudited`
  - `PriceChanged`
  - `ValveStatusRequest` and `ValveStatusResponse`
  - `AlwaysFails`
  - `AllMemberTypes` (one property of each supported member type)

  Link the file into the device tests, the sample and `NServiceBus.Community.Mqtt.Tests`. Verify: both solutions build without new warnings in `NServiceBus.Community.Mqtt.Tests`.
- [ ] 1.5 **The user opens this pull request, not the implementer. Do not open it or push to that repository.** The pull request on `nanoframework/nanoFramework.m2mqtt` adds a `Publish` overload taking `uint messageExpiryInterval` that sets `MqttMsgPublish.MessageExpiryInterval`, with a unit test. Stop at this task until the user confirms the PR is open, then record the link they give in `design.md` (D9). Verify: the user has supplied the PR link.
- [ ] 1.6 Add a `windows-latest` job to `.github/workflows/ci.yml`. It sets up the nanoFramework build components, MSBuild and NuGet, restores and builds the nanoFramework solution, runs the device tests on nanoCLR (uploading a `.trx`), and packs the `.nuspec` as an artifact. Leave the Linux job as it is. Verify: both jobs are green on a pull request.

## 2. Wire rules: addresses, client IDs, topics, hierarchy

- [x] 2.1 Create `src/Interop/MappingCases.cs` with cases for:
  - address → topic, and the rejected addresses
  - topic → client ID, including `~XX` escapes
  - type full name → event topic, including nested types
  - `EnclosedMessageTypes` parsing, including assembly-qualified and generic entries
  - the expected hierarchy of the sample contracts

  Move the .NET pump's `EnclosedMessageTypeNames` into an internal helper. This is a visibility change only, with no behavior change. Add a .NET unit test that runs every case against `MqttAddress`, `MqttClientId`, `EventTopic`, `EventTypeHierarchy` and that helper. Verify: `dotnet test src/NServiceBus.Community.Mqtt.Tests` passes.
- [x] 2.2 Implement the device's address mapping and validation, and its client ID sanitizing. Verify: device tests over the address and client ID `MappingCases` pass on nanoCLR.
- [x] 2.3 Implement the device's event topic derivation and type hierarchy:
  - the event's own type, then base classes nearest first, then interfaces in ordinal order
  - leave out `object`, the markers, and `System*` types (found by `FullName` prefix)

  Verify: device tests over the topic and hierarchy `MappingCases` pass, including `Sales.Container+Inner` → `events/Sales.Container.Inner`.
- [x] 2.4 Implement `EnclosedMessageTypes` parsing (split at the first comma outside brackets) and the one-copy rule as pure functions. Verify: device tests over the shared parsing cases, and the base+derived and two-interface cases of the one-copy rule, pass.

## 3. Wire codec and body serialization

- [x] 3.1 Create `src/Interop/WirePayloads.cs` with golden .NET-origin payloads and their expected values. The payloads are: a command, an event, a reply with `OriginatingSagaId` and `OriginatingSagaType`, headers with non-ASCII and escaped characters (including `+`), an empty body, and `AllMemberTypes`. Add .NET unit tests asserting that `WireFormat.Encode`, with bodies from NServiceBus's `SystemJsonSerializer`, produces each payload byte for byte. Verify: `dotnet test src/NServiceBus.Community.Mqtt.Tests` passes.
- [x] 3.2 Implement the device envelope reader. It must handle any property order, whitespace, unknown properties, every JSON escape including surrogate pairs, and base64 bodies, and raise a decode error for invalid input. Verify: device tests decode every .NET-origin golden payload to the expected IDs, headers and body bytes, and reject garbage, JSON `null`, and null headers or body.
- [x] 3.3 Implement the device envelope writer, which escapes only `"`, `\` and control characters and writes raw UTF-8 otherwise. Add device-origin golden payloads to `WirePayloads.cs`. Verify two things: the device tests assert byte-exact output, and the .NET tests decode every device-origin payload with `WireFormat.Decode` to the expected headers and body.
- [x] 3.4 Implement body serialization over `nanoFramework.Json`, wrapping it where its output or parsing differs from System.Text.Json for any supported member type. Verify two things: a device test deserializes the .NET-origin `AllMemberTypes` body to the expected values, and a .NET test deserializes the device-origin `AllMemberTypes` body with `SystemJsonSerializer` to the expected values.
- [x] 3.5 Implement the NServiceBus wire time format (`yyyy-MM-dd HH:mm:ss:ffffff Z`, UTC) and the `TimeSpan` constant format for the TTBR header. Verify two things: device tests produce the golden strings, and a .NET test checks those strings with `DateTimeOffsetHelper.ToDateTimeOffset` and `TimeSpan.ParseExact`.

## 4. Connection seam and endpoint lifecycle

- [x] 4.1 Define the internal `IMqttConnection` seam and the injectable clock, delay and ID generator. In the test project, write `FakeMqttConnection`, an in-memory broker with persistent sessions, QoS 1 subscriptions, takeover with reason `0x8E`, maximum packet size, and injectable connect, publish and subscribe failures. Verify: device tests for the fake itself pass (a session keeps messages while disconnected; a takeover disconnects the first client with `0x8E`).
- [x] 4.2 Implement `DeviceEndpointConfiguration` with its defaults and validation. Validation covers the endpoint name, host, port, session expiry range, immediate retries ≥ 0, maximum packet size, the timeouts and the credentials. Verify: device tests cover each invalid-input scenario in `device-endpoint` and the defaults (7 days, 5, `error`, 16384, 10 s).
- [x] 4.3 Implement start. It connects with client ID `nsb.{sanitized}`, a persistent session, session expiry, maximum packet size and credentials, subscribes to the queue topic at QoS 1, and waits for the SUBACK. Failures name the host, the port and the reason, and leave no threads behind. Verify: device tests with the fake cover a successful start delivering the backlog, an unreachable broker, rejected credentials and a rejected subscription.
- [x] 4.4 Implement the non-blocking intake queue, the single processing thread, and stop. Stop finishes the current message, drains until `StopDrainTimeout`, logs the leftover count and sends a normal DISCONNECT. A second stop does nothing, and operations after stop throw. Verify: device tests cover sequential processing, stop waiting for the handler, the session being kept after stop, and send after stop.
- [x] 4.5 Implement reconnect (back-off 1 s doubling to 30 s, reset on success, every failed attempt logged, subscriptions applied again) and takeover handling. On takeover the device raises a critical error naming the address, does not reconnect, drops and counts the intake, and fails sends with a "displaced" error. Verify: device tests with the fake and an injected delay cover the back-off sequence, resubscribing, and the two takeover scenarios.
- [x] 4.6 Implement the critical error callback, with error-level logging as the default. Verify: device tests show the callback is invoked for a takeover and for a failure of the processing loop, and that the endpoint keeps running afterwards.
- [ ] 4.7 Implement `M2MqttConnection` over `nanoFramework.M2Mqtt`:
  - the connect properties
  - publish-and-wait, with the pending-ID table under the same lock as the `MqttMsgPublished` handler
  - waits for SUBACK and UNSUBACK
  - capturing `ResonCode` in `ConnectionClosedRequest`
  - a `MessageReceived` callback that only enqueues

  Verify by hand on an ESP32 against Mosquitto with `src/mosquitto/mosquitto.conf`:
  - a message published with `mosquitto_pub` is received
  - publish-and-wait returns after the PUBACK
  - `mosquitto_sub -V mqttv5 -c -i nsb.<name>` causes a reported takeover

  Note the result in the PR.
- [x] 4.8 Add the README's device section, covering what the package is, installation, configuration (including credentials and session expiry), start and stop, reconnects, acknowledgement on receipt and what it loses, one device per endpoint name, maximum packet size, and setting the clock with SNTP. Verify: the README's configuration snippet matches the code in the sample app.

## 5. Sending, receiving and replying

- [x] 5.1 Implement the outgoing header builder: the standard headers, the conversation, correlation and related-to rules inside and outside a handler, and custom headers. Verify: device tests cover the "Sent from a handler" and "Sent outside a handler" scenarios, and the device-origin command golden payload still matches byte for byte.
- [x] 5.2 Implement routing and validation: explicit destination, type routes, the no-route error naming the type, destination validation, and rejecting a send or reply of an event and a publish of a non-event. Verify: device tests cover the four routing scenarios in `device-messaging`.
- [x] 5.3 Implement dispatch outside handlers: fail at once when not connected or displaced, the dispatch timeout, broker rejection, and the exact MQTT 5 PUBLISH size check before anything is published. Verify: device tests with the fake cover each failure mode, plus packet-size unit tests against hand-computed sizes for short and long topics and payloads around the varint boundaries.
- [x] 5.4 Implement the type registry and resolution. Resolution takes the first registered full name in `EnclosedMessageTypes`, ignoring the assembly part. The cached handler list per resolved type holds every registration the type is assignable to, in registration order. The handler context exposes the message ID, the reply-to address and a per-attempt copy of the headers. Verify: device tests cover the assembly-qualified, base-type-only and type-plus-interface scenarios.
- [x] 5.5 Implement batched dispatch from the handler context. Messages are collected, dispatched in order after all handlers succeed, discarded when a handler throws, and a dispatch failure counts as a processing failure. Verify: device tests cover "Handler sends, then throws" (dispatched exactly once) and "Dispatch failure is retried".
- [x] 5.6 Implement reply: it goes to `ReplyToAddress` with intent `Reply`, carries the correlation headers, maps `OriginatingSagaId`/`OriginatingSagaType` to `SagaId`/`SagaType`, and fails when there is no reply-to address. Add a device-origin reply golden payload. Verify: device tests pass, and the .NET test decodes the reply golden with the saga headers intact.
- [x] 5.7 Add the public API guard: a device test that compares the assembly's public types and members with an approved list in the test project. Verify: the test passes, and fails when a public member is added without updating the list.
- [x] 5.8 Extend the README's device section. Cover sharing contract source (marker interfaces in `NServiceBus`, same namespace and class names), handler registration, send, routing and reply, batched dispatch and its duplicate-on-retry note, the dispatch timeout and a send that timed out possibly still arriving, and the supported member types. Verify: every code snippet in the section compiles in the sample app.

## 6. Recoverability

- [x] 6.1 Implement the attempt loop with immediate retries. Each attempt clones the headers and deserializes the body again; there is no delay and no delayed retries. Verify: device tests cover "Succeeds on the third attempt", "Header change does not leak" and "Retries turned off".
- [x] 6.2 Implement forwarding to the error queue. The forwarded message has the original body, headers and message ID, plus `FailedQ`, `TimeOfFailure`, `ExceptionInfo.ExceptionType`, `ExceptionInfo.Message`, `ExceptionInfo.StackTrace`, `ExceptionInfo.InnerExceptionType` (when there is one) and `ProcessingEndpoint`. Add a device-origin failed-message golden payload. Verify two things: device tests cover "Always failing handler" and "Processing continues", and a .NET test checks the golden's header names against `FaultsHeaderKeys` and `Headers` and parses `TimeOfFailure` with `DateTimeOffsetHelper`.
- [x] 6.3 Implement retries of the forwarding itself: a critical error, a back-off of 1 s doubling to 30 s, then forwarding again until it succeeds or the endpoint stops, with the loss logged on stop. Verify: device tests with the fake cover "Broker comes back" and stopping during the outage (the loss is logged with the message ID).
- [x] 6.4 Implement the shortcuts:
  - Discard a payload that is not a message, logging it with the topic.
  - Send these straight to the error queue, with an exception whose full name is `NServiceBus.MessageDeserializationException`: a message with no `EnclosedMessageTypes`, with no registered type, with a non-JSON content type, or with a malformed body.

  Verify: device tests cover "Garbage payload", "Unknown message type" and "Malformed body".
- [x] 6.5 Add a README section on device recoverability: immediate retries and their setting, the error queue and its failure headers, why a .NET endpoint with installers (or the operator) must declare `error`, no delayed retries, and how undecodable and undeserializable messages are handled. Verify: the documented defaults match `DeviceEndpointConfiguration`.

## 7. Publish and subscribe

- [x] 7.1 Implement publish along the hierarchy. Every copy has the same message ID and headers, every copy's size is checked first, the publish returns after every copy is acknowledged, and the first failure stops it with an error naming the topic. Add a device-origin event golden payload. Verify: device tests cover the interface and base-class subscriber scenarios against the fake, and the .NET test decodes the event golden with `EnclosedMessageTypes` in hierarchy order.
- [x] 7.2 Implement `Subscribe` and `Unsubscribe` before and after start, with waits for SUBACK and UNSUBACK. Subscriptions are applied again at start and after a reconnect, and handled `IEvent` types are subscribed automatically at start. Verify: device tests cover subscribing at runtime, unsubscribing, surviving a reconnect, and a handler without an explicit subscription.
- [x] 7.3 Wire the one-copy rule into the receive pipeline, using the device's subscribed event types. Verify: device tests cover "Subscribed to a type and its base" (processed once) and "Two devices subscribed to two different interfaces".
- [x] 7.4 Add a README section on device publish/subscribe: event topics, copies along the hierarchy, auto-subscribe, explicit subscribe and unsubscribe, and processing one copy. Verify: the topic example matches a `MappingCases` entry.

## 8. Time to be received (waits for the M2Mqtt release from 1.5)

- [ ] 8.1 Once a `nanoFramework.M2Mqtt` release has the expiry overload, do the following:
  - Pin it.
  - Add `TimeToBeReceived` to `SendOptions`, `PublishOptions` and `ReplyOptions`.
  - Send the message expiry (rounded up, never zero, none beyond the MQTT range) and the `NServiceBus.TimeToBeReceived` header on every copy.
  - Add a device-origin golden payload with the TTBR header.
  - State in the README that TTBR is available.

  Verify: device tests cover rounding, the minimum and the out-of-range case, and the .NET test parses the golden header.
- [ ] 8.2 If that release is not available when groups 1–7 and 9 are done, apply the D9 fallback instead:
  - Ship without the option.
  - Move the "Time to be received" requirement from `specs/device-messaging/spec.md` into a new follow-up change.
  - State in the README that TTBR is not yet available.
  - Make the interop host report the TTBR check as skipped.

  Verify: `openspec validate add-nanoframework-mqtt-package --strict` and the follow-up change both validate.

## 9. Sample device app, interop host and the board test

- [x] 9.1 Build the sample device app. It connects to Wi-Fi, sets the clock with SNTP, and starts the endpoint `NanoInterop_Device` with the settings in `SampleSettings.cs` (placeholders). Its handlers:
  - `OpenValve`: reply, then publish `ValveOpened`
  - `ValveEvent`: send an acknowledgement command to the host (the base-type subscription)
  - `AlwaysFails`: always throw
  - a TTBR send, when 8.1 is done

  It logs free memory after start. Verify: the sample builds in the Windows CI job.
- [x] 9.2 Create `src/NServiceBus.Community.NanoframeworkMQTT.InteropHost`, a `net10.0` console app in the `.slnx` that links the shared contracts. It reads the broker from `MqttTransport_Server` and `MqttTransport_Port`, enables installers, and runs the checks in `device-interop-verification` with timeouts. It reads `error` as `nsb.error.declared` through MQTTnet, prints pass, fail or skipped for each check, and exits non-zero on any failure. Verify two things: the Linux CI job builds it, and running it against the Docker broker with no device reports the request/reply check as failed and exits non-zero.
- [ ] 9.3 Write the README's manual board procedure:
  1. Start Mosquitto with the repository's configuration.
  2. Edit `SampleSettings.cs`.
  3. Flash with `nanoff` or Visual Studio.
  4. Run the interop host.

  Name the reference board (ESP32 with Wi-Fi). Verify: carried out in 9.4.
- [ ] 9.4 Run the board test on an ESP32 by following the README word for word. Verify: the interop host reports every check passed (TTBR passed or skipped as in group 8) and exits 0. Record the date, board, firmware version and free memory after start in the README.

## 10. Repository documentation and integration

- [x] 10.1 Update the README's platform and testing sections:
  - the pinned nanoFramework package versions, and the requirement that the firmware matches them
  - the two solutions, and that the nanoFramework one builds only on Windows with MSBuild and the nanoFramework build components
  - running the device tests
  - the Windows CI job

  Add a .NET unit test that compares the `.nuspec`'s dependencies with the README's version table, in the style of `KnownExclusionsTests`. Verify: that test passes, and fails when one side changes.
- [ ] 10.2 Run the final integration check. Verify all of these:
  - Both CI jobs are green on the pull request.
  - Listing `src/` gives exactly the entries the `transport-project-structure` delta names.
  - The repository root holds no `.cs`, `.csproj` or `.nfproj`.
  - `openspec validate add-nanoframework-mqtt-package --strict` passes.
