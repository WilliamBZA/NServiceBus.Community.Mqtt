# Design

## Context

See `proposal.md` for the motivation and `specs/` for the required behavior. This document covers the *how*.

**The wire contract the device has to match**, read from `src/NServiceBus.Community.Mqtt`:

| Concern | .NET implementation | Rule |
|---|---|---|
| Envelope | `WireFormat`, `MessageWrapper` | `{"Id":…,"Headers":{…},"Body":"<base64>"}` written with System.Text.Json defaults. Those defaults escape non-ASCII characters, and some ASCII ones such as `+`, as `\uXXXX`. |
| Addresses | `MqttAddress` | `_` becomes `/`. Rejects `+`, `#` and a leading `$`. |
| Client IDs | `MqttClientId` | `nsb.` plus the sanitized topic. Characters outside `A-Z a-z 0-9 . - _ /` are written as `~XX` per UTF-8 byte. |
| Event topics | `EventTopic` | `events/` plus the full name, with `+ / # \` [ ] , space *` replaced by `.`. |
| Event copies | `EventTypeHierarchy` | The event's own type, then base classes (nearest first), then interfaces sorted by ordinal full name. Leaves out `object`, `IMessage`, `IEvent`, `ICommand` and `System*` types. |
| Processing one copy | `MqttMessagePump.IsDesignatedCopy` | On an event topic with `EnclosedMessageTypes`, process only the copy whose topic belongs to the first subscribed type in that header. |
| Delivery | `MqttDispatcher`, `MqttConnectionSettings` | QoS 1. MQTT 5 session expiry. Message expiry for TTBR. |

**NServiceBus 10 type resolution**: `MessageMetadataRegistry.GetMessageMetadata(string)` falls back to matching the full names of known message types when `Type.GetType` fails. So a device can send `EnclosedMessageTypes` entries without an assembly part, and a .NET endpoint still resolves them.

**nanoFramework, checked in October 2026:**
- Generics exist only in preview firmware (PE format v2).
- There is no `async`/`Task` and no LINQ.
- `System.Type` has `FullName`, `BaseType`, `GetInterfaces()`, `IsInterface`, `IsInstanceOfType` and `GetType(string)`. It has no `Namespace` and no `IsAssignableFrom`.
- Projects are `.nfproj` files built with MSBuild and the nanoFramework build components, on Windows only.
- Unit tests run on the nanoCLR Win32 virtual device through VSTest. The virtual device has limited networking, so it can't reach a broker.

**nanoFramework.M2Mqtt, checked on `main`:**
- It supports MQTT 5. `Connect(clientId, username, password, cleanSession, keepAlive)` returns an `MqttReasonCode`. `SessionExpiryInterval`, `MaximumPacketSize` and `ReceiveMaximum` are client properties set before connecting.
- For an incoming QoS 1 message it sends PUBACK *before* raising `MqttMsgPublishReceived`. There is no manual acknowledgement.
- `Publish(topic, payload, contentType, userProperties, qos, retain)` returns the packet ID immediately. Completion is reported by `MqttMsgPublished(MessageId, IsPublished)`.
- `MqttMsgPublish` encodes `MessageExpiryInterval`, but no `Publish` overload lets the caller set it.
- All events are raised on one dispatch thread, with one exception. `ConnectionClosedRequest` is raised on the receive thread with the parsed DISCONNECT packet, which carries `ResonCode` (the library's spelling). It is raised before `ConnectionClosed`.
- There is no automatic reconnect. TLS and credentials are supported.

**Repository**: `src/Directory.Build.props` sets `net10.0`, `LangVersion` 14, nullable, implicit usings and `Particular.Analyzers` for *every* MSBuild project under `src/`, and a `.nfproj` would import it too. CI is one Ubuntu job.

## Goals / Non-Goals

**Goals:**
- On the wire, a .NET endpoint cannot tell a device endpoint from any other endpoint.
- Small, predictable memory use on the device: one processing thread, no assembly scanning, and a dedicated envelope codec.
- Every wire rule on the device is a port of the .NET rule. Both ports are checked against the same shared data, so a drift on either side fails a test.
- All device logic can be tested without hardware or a broker.

**Non-Goals:**
- Sharing transport source code between the .NET and device sides. The .NET code uses generics, spans and records, and rewriting it in a common subset would make it worse. Only message contracts and test data are shared.
- Any change to the .NET transport.
- Generic APIs such as `IHandleMessages<T>`. They can be added next to the non-generic ones once nanoFramework generics leave preview.
- Flow control for received messages. Acknowledging on receipt rules it out (see Risks).
- Declaring the error queue from a device (see D8).

## Decisions

### D1. Same repository, two solutions, guarded shared props

```
src/
  NServiceBus.Community.Mqtt.slnx                         (existing; + InteropHost)
  NServiceBus.Community.NanoframeworkMQTT.sln             (new)
  Directory.Build.props                                   (existing; applies to .csproj only)
  Interop/                                                (shared source, no project)
    InteropContracts.cs   sample message contracts
    WirePayloads.cs       golden payloads and their expected values
    MappingCases.cs       address / topic / client ID / event topic / hierarchy cases
  NServiceBus.Community.NanoframeworkMQTT/                (.nfproj, .nuspec, packages.config)
  NServiceBus.Community.NanoframeworkMQTT.Tests/          (.nfproj, nanoFramework.TestFramework)
  NServiceBus.Community.NanoframeworkMQTT.Sample/         (.nfproj, ESP32 app)
  NServiceBus.Community.NanoframeworkMQTT.InteropHost/    (.csproj, net10.0 console)
```

- **One repository**, so the wire contract lives in one place, and a change on either side breaks a test in the same pull request. *Rejected:* a separate repository, which would need versioned golden data and cross-repository CI.
- **A separate `.sln` for the nanoFramework projects.** `dotnet build` cannot build `.nfproj`, so keeping those projects out of the `.slnx` keeps the Linux build unchanged. A `.sln` is used because the nanoFramework tooling is known to accept it. Task 1.1 checks whether `.slnx` works too.
- **The shared props are guarded.** Every group in `Directory.Build.props` gets `Condition="'$(MSBuildProjectExtension)' == '.csproj'"`. *Rejected:* a nested `src/nanoFramework/` folder with its own props file, which breaks the "folder of the same name directly under `src/`" rule that keeps the repository easy to navigate.
- **The `src/Interop/` files are linked** with `<Compile Include="..\Interop\…" Link="…" />`:
  - The contracts go into the device tests, the sample, the .NET unit tests and the interop host.
  - The payloads and mapping cases go into the two test projects.

  They are written in the subset both compilers accept: `#nullable disable`, no generics, no records, no target-typed `new`, no LINQ, and `const` strings.
- **Packaging follows nanoFramework's own libraries**: a `.nuspec` packed with `nuget pack`. The package carries the `.dll`, `.pe`, `.pdbx` and `.xml` files, and its nanoFramework dependencies have exact versions (`[x.y.z]`). The version is `1.0.0`.

### D2. Language subset and platform APIs

- No generics, `async` or LINQ. Collections are `ArrayList` and `Hashtable`. Threading uses `Thread`, `lock` and `AutoResetEvent`/`ManualResetEvent`.
- Reflection is limited to `FullName`, `BaseType`, `GetInterfaces()` and `IsInterface`, plus what `nanoFramework.Json` needs to (de)serialize bodies.
- "Is assignable to" is a walk up the type hierarchy. "Is a `System` type" is a prefix check on `FullName`, because there is no `Namespace`.
- Logging goes through `nanoFramework.Logging`'s `ILogger`. The default is its debug logger, so output shows up while debugging and costs nothing otherwise.

### D3. Public API: small, synchronous, in the `NServiceBus` namespace

```csharp
namespace NServiceBus
{
    public interface IMessage { }
    public interface ICommand : IMessage { }
    public interface IEvent : IMessage { }

    public interface IHandleMessages { void Handle(object message, IMessageHandlerContext context); }

    public interface IMessageSession
    {
        void Send(object message);       void Send(object message, SendOptions options);
        void Publish(object message);    void Publish(object message, PublishOptions options);
        void Subscribe(Type eventType);  void Unsubscribe(Type eventType);
    }

    public interface IMessageHandlerContext
    {
        string MessageId { get; }  string ReplyToAddress { get; }  Hashtable MessageHeaders { get; }
        void Send(object message);     void Send(object message, SendOptions options);
        void Publish(object message);  void Publish(object message, PublishOptions options);
        void Reply(object message);    void Reply(object message, ReplyOptions options);
    }

    public class SendOptions    { public string Destination { get; set; } public void SetHeader(string key, string value); }
    public class PublishOptions { public void SetHeader(string key, string value); }
    public class ReplyOptions   { public void SetHeader(string key, string value); }
    // + TimeSpan TimeToBeReceived on all three, once D9 lands

    public delegate void CriticalErrorCallback(string description, Exception exception);

    public class DeviceEndpointConfiguration
    {
        public DeviceEndpointConfiguration(string endpointName, string server, int port = 1883);
        public void UseCredentials(string username, string password);
        public TimeSpan SessionExpiry { get; set; }      // 7 days
        public int ImmediateRetries { get; set; }        // 5
        public string ErrorQueue { get; set; }           // "error"
        public int MaximumPacketSize { get; set; }       // 16384
        public TimeSpan DispatchTimeout { get; set; }    // 10 s
        public TimeSpan StopDrainTimeout { get; set; }   // 10 s
        public ILogger Logger { get; set; }
        public CriticalErrorCallback OnCriticalError { get; set; }
        public void RegisterHandler(Type messageType, IHandleMessages handler);
        public void RouteToEndpoint(Type messageType, string destination);
    }

    public sealed class DeviceEndpoint : IMessageSession
    {
        public static DeviceEndpoint Start(DeviceEndpointConfiguration configuration);
        public void Stop();
    }
}
```

- **The marker interfaces are in `NServiceBus`.** A contract file with `using NServiceBus;` and `: IEvent` compiles unchanged for .NET and for nanoFramework. That is what makes shared contract source possible. On the wire a type is identified only by its namespace-qualified name, so both sides must use the same namespace and class name. The README recommends sharing the source, and the sample does.
- **`IHandleMessages` is non-generic** and borrows the familiar NServiceBus name. The handler casts `message`. A generic `IHandleMessages<T>` can be added next to it later.
- **Handlers are registered explicitly**, as one instance per registration that is reused for every message. Processing is single-threaded, so the instance never runs concurrently. *Rejected:* scanning assemblies for handlers. It is slow and memory-hungry on a microcontroller, and nanoFramework's reflection lacks the APIs it needs.
- **The API is synchronous**, because nanoFramework has no `Task`.
- **The public surface is guarded by a device unit test** that compares the assembly's public types and members with an approved list. This plays the role of the .NET side's approval test, because `PublicApiGenerator` does not run on nanoFramework.

### D4. Internals and threads

- **`IMqttConnection` is the internal seam.** It offers `Connect`, `Subscribe(topics)`, `Unsubscribe`, `Publish(topic, payload, expiry)` (which blocks until PUBACK or timeout) and `Disconnect`. It raises `MessageReceived(topic, payload)` and `ConnectionLost(takenOver)`.
  - `M2MqttConnection` adapts `nanoFramework.M2Mqtt`.
  - The tests provide `FakeMqttConnection`, an in-memory broker with sessions, subscriptions, takeover and injectable failures.
- **Threads.** M2Mqtt runs its own threads: receive, keep-alive, inflight and dispatch. The package adds one processing thread, plus a reconnect thread that is started only when needed.
- **The receive callback never blocks.** It runs on M2Mqtt's single dispatch thread, and it only appends to the intake queue and signals the processing thread. The PUBACK events that a handler's own sends wait for are raised on that same thread, so blocking it would deadlock.
- **Publish-and-wait has no race.** `Publish` is called, and its returned packet ID recorded, inside the same lock that the `MqttMsgPublished` handler takes. A confirmation that arrives early therefore waits for the registration. The caller then waits on a per-publish event, up to `DispatchTimeout`.
- **One dispatch lock** serializes publishes from application threads and from the processing thread.
- **Takeover detection.** The `ConnectionClosedRequest` handler runs on the receive thread and only records the reason code. When `ConnectionClosed` follows on the dispatch thread, a reason of `0x8E` puts the endpoint in the displaced state and raises a critical error. Any other reason starts the reconnect thread, unless the endpoint is stopping.

### D5. Wire codec

- **The envelope has a hand-written UTF-8 JSON reader and writer**, for exactly `{"Id":string|null,"Headers":{string:string},"Body":base64}`.
  - The reader accepts any property order, whitespace and unknown properties, and every JSON escape including surrogate pairs. System.Text.Json escapes non-ASCII characters and `+` by default, so the reader must handle them.
  - The writer escapes `"`, `\` and control characters and writes everything else as raw UTF-8, which System.Text.Json reads.
  - *Rejected:* `nanoFramework.Json` for the envelope. Its treatment of `byte[]` and of a string-to-string map would have to match System.Text.Json exactly on every message. A dedicated codec is both safer and lighter.
- **Bodies use `nanoFramework.Json`** (`JsonConvert.SerializeObject`, `DeserializeObject(string, Type)`). The package is responsible for making the member types in the spec round-trip, and wraps `nanoFramework.Json` where it differs from System.Text.Json (for example `DateTime` precision or enums). The golden payloads find such gaps in task group 3, before the endpoint is built on top.
- **Header formats.** `NServiceBus.TimeSent` and `NServiceBus.TimeOfFailure` use NServiceBus's wire format, `yyyy-MM-dd HH:mm:ss:ffffff Z` in UTC, formatted by hand because nanoFramework's `DateTime` formatting is limited. The time to be received header uses `TimeSpan`'s constant format (`c`).
- **Ports of the .NET rules**: address mapping, client ID sanitizing, event topics, hierarchy order and exclusions, parsing `EnclosedMessageTypes` (split at the first comma outside brackets), and the one-copy rule. `src/Interop/MappingCases.cs` holds the cases, and both the device tests and the .NET unit tests run them against their own implementation.

### D6. Receive pipeline

The processing thread handles one message at a time:

1. **Decode the envelope.** If that fails, log an error naming the topic and discard the payload.
2. **Apply the one-copy rule.** Drop a copy that is not the designated one.
3. **Resolve the type.** Check the content type, then pick the first name in `EnclosedMessageTypes` that is registered. If that fails, forward the message to the error queue at once, with an internal exception whose full name is `NServiceBus.MessageDeserializationException`. That is the name NServiceBus uses, so error tooling groups these failures the same way.
4. **Run the attempt loop**, for 1 + `ImmediateRetries` attempts. Each attempt:
   - Clones the headers.
   - Deserializes the body. If that fails, the message goes to the error queue at once.
   - Creates a context that collects outgoing messages.
   - Invokes the cached handler list for the resolved type: every registration whose type the resolved type is assignable to, in registration order.
   - Dispatches the collected messages.

   Any exception moves on to the next attempt.
5. **Forward to the error queue** when the attempts are exhausted. The forwarded message has the original body and headers plus the failure headers:
   - `FailedQ`
   - `TimeOfFailure`
   - `ExceptionInfo.ExceptionType`, `ExceptionInfo.Message` and `ExceptionInfo.StackTrace`
   - `ExceptionInfo.InnerExceptionType`, when there is an inner exception
   - `ProcessingEndpoint`

   nanoFramework has no `Source` or `HelpLink`, so those headers are left out. If forwarding fails, the device raises a critical error, waits with a back-off of 1 s doubling to 30 s, and tries again, until forwarding succeeds or the endpoint stops.

Handlers cannot be cancelled, because nanoFramework has no `CancellationToken`. Stop waits for them.

### D7. Dispatch

- The outgoing headers are set as the specs list them. The message ID is `Guid.NewGuid().ToString()`.
- Sends inside a handler are collected and dispatched after the handlers succeed. Sends outside a handler are dispatched at once.
- **Packet size is checked before anything is published.** The size of an MQTT 5 PUBLISH is computed exactly: fixed header, remaining-length varint, topic, packet ID, properties and payload. Every copy of a publish is checked before the first one is sent, just as the .NET dispatcher validates every address first.
- A device that is not connected, or that has been displaced, fails a send at once. Sends use QoS 1 without retain. The MQTT content type property is not set, because the envelope header carries the content type.

### D8. Connection lifecycle

- **Connect** with:
  - client ID `nsb.{sanitized}`
  - `cleanSession=false`
  - the configured `SessionExpiryInterval` and `MaximumPacketSize`
  - keep-alive of 60 s
  - the credentials, if set

  A CONNACK other than success fails with the host, the port and the reason code.
- **Subscribe** to the queue topic and the event topics at QoS 1, and wait for the SUBACK. A granted code of `0x80` or more fails, naming the topic. At start, the event topics include every handled `IEvent` type (auto-subscribe).
- **Reconnect** with a back-off of 1 s doubling to 30 s, then subscribe again and reset the back-off. This mirrors the .NET pump.
- **Displaced.** The device does not reconnect. Messages that are still in the intake are dropped, and their count is logged. They were already acknowledged, so this is the documented acknowledge-on-receipt loss. Sends fail with a "displaced" error. Unlike the .NET transport, sends stop working too, because the device has a single connection.
- **Stop.**
  1. Set the stopping flag.
  2. Let the processing thread finish its current message and drain the intake until it is empty or `StopDrainTimeout` passes.
  3. Log the number of messages left over.
  4. Send a normal DISCONNECT, which keeps the session.
  5. Join the threads.
- **The error queue is not declared by devices.** The .NET transport declares `error` through installers, as the holder session `nsb.error.declared`. Doing the same on a device would need a second client ID connection at every start, plus the .NET side's handling of concurrent declarations. The README says that some .NET endpoint must run with installers, or the operator must create the holder session. Otherwise failed messages from devices are best effort.

### D9. Time to be received waits for an upstream change

- **Upstream pull request** to `nanoframework/nanoFramework.m2mqtt`: a `Publish` overload that also takes `uint messageExpiryInterval` and sets `MqttMsgPublish.MessageExpiryInterval`. It is small, additive, and follows the pattern of the existing overloads. The user opens this pull request themselves. Implementation does not open it, and pauses at task 1.5 until the user supplies the link.
- **Until a release includes it**, the `TimeToBeReceived` option is not added to the API. It is left out entirely, rather than added and made to throw. If the release is not out when everything else is done:
  - The package ships without TTBR.
  - The "Time to be received" requirement in `device-messaging` moves to a follow-up change.
  - The interop host reports its TTBR check as skipped.
- *Rejected:* vendoring a patched M2Mqtt, because the fork would have to be maintained. Also rejected: setting the property through reflection, because nanoFramework's reflection cannot reliably reach M2Mqtt's internals.

### D10. Verification

- **Device unit tests** use `nanoFramework.TestFramework` on the nanoCLR virtual device through VSTest. `FakeMqttConnection` drives the endpoint end to end, in process. Internal seams for the clock, the delays and the ID generator make the back-off tests run without sleeping and make the device-origin payloads byte-exact.
- **Golden payloads** live in `src/Interop/WirePayloads.cs`, as constant JSON strings with the expected header and value tables. Both origins are checked on both sides:
  - **.NET origin.** The .NET test asserts that `WireFormat.Encode` and NServiceBus's `SystemJsonSerializer` produce the golden string byte for byte, since System.Text.Json output is deterministic. The device tests decode that exact escaped text.
  - **Device origin.** The device test asserts byte-exact output. The .NET test decodes the payload with `WireFormat.Decode`, deserializes the body with the NServiceBus serializer, and checks the header names against `FaultsHeaderKeys`, `Headers` and `DateTimeOffsetHelper`.
- **The interop host** is a .NET 10 console app. It runs NServiceBus endpoints on `MqttTransport` against a broker selected with `MqttTransport_Server` and `MqttTransport_Port`, enables installers so that `error` is declared, and runs the checks one after another with timeouts. It reads the error queue as the holder session through MQTTnet, and its exit code reports the result. CI builds it but does not run it.
- **The sample device app** targets an ESP32 with Wi-Fi, the reference board. The Wi-Fi and broker settings are placeholders in `SampleSettings.cs`, which the README tells you to edit before flashing.

## Risks / Trade-offs

- **[Acknowledging on receipt]** A message in progress, waiting in the intake, or dropped when the device is displaced, is lost on reboot, crash or stop. → Mitigations:
  - The README documents it.
  - Stop drains the intake.
  - In steady state the intake stays short.
  - A later change can contribute manual acknowledgement upstream.
- **[No flow control]** After a long offline period the broker pushes the whole backlog at once, M2Mqtt acknowledges all of it, and it all lands in RAM. → Mitigations:
  - `MaximumPacketSize` bounds each message.
  - The README advises a modest `SessionExpiry` for devices.
  - The README notes that Mosquitto's queue caps are broker-wide, so they are a trade-off with the .NET endpoints' needs.
  - The device logs a warning when the intake grows past a threshold.
- **[Body JSON differences between `nanoFramework.Json` and System.Text.Json]** for dates, enums or doubles. → Every supported type has a golden payload, the package wraps the serializer where needed, and this is done early (task group 3).
- **[The upstream TTBR pull request is not released in time]** → The D9 fallback.
- **[nanoFramework tooling changes]**, such as the project system, SDK-style projects or the VSTest adapter. → Task 1.1 checks the current tooling, CI uses the official nanoFramework build action, and versions are pinned.
- **[Memory on small boards]** → One processing thread, no scanning, one buffer per envelope. The manual board test records the free memory after start, and the README states it.
- **[The device clock is not set]** → `TimeSent` and `TimeOfFailure` are wrong. The README says to set the clock with SNTP before starting. NServiceBus only uses those values for information.
- **[NServiceBus header drift]** → The .NET contract tests use NServiceBus's own constants, so an NServiceBus upgrade runs them again.
- **[Firmware and package version mismatch on a device]** → Exact versions are pinned and listed in the README.
- **[Different contracts with the same namespace-qualified name]** → This is how NServiceBus behaves too. The README says to share the contract source.

## Migration Plan

The change is additive, and no existing consumer is affected. The order of work:
1. The user raises the upstream M2Mqtt pull request early, since it runs in parallel. Work pauses at task 1.5 until they have.
2. Merge everything else, with TTBR deferred if the upstream release isn't out.
3. Produce the 1.0.0 package as a CI artifact. Publishing to nuget.org is out of scope.

To roll back, remove the nanoFramework projects, `src/Interop/`, the interop host and the Windows CI job. The .NET transport is untouched.

## Open Questions

- ~~Does the nanoFramework tooling accept `.slnx`?~~ **Answered (task 1.1, 2026-10-03):** MSBuild from Visual Studio 2026 builds a `.slnx` that holds `.nfproj` projects, but only when each entry carries the project type, for example `<Project Path="X/X.nfproj" Type="11A8DD76-328B-46DF-9F39-F559912D0360" />`. Without `Type`, MSBuild fails with "ProjectType '' not found". The decision stays at `.sln`, because that is the format the nanoFramework tooling and its GitHub build action document.
- Which exact nanoFramework package versions to pin. They are chosen at implementation time from the latest stable set. **Latest stable on 2026-10-03:** `nanoFramework.CoreLibrary` 1.17.12 (pinned at 1.17.11, see below), `nanoFramework.TestFramework` 3.0.80, `nanoFramework.M2Mqtt` 5.1.226, `nanoFramework.Json` 2.2.213, `nanoFramework.System.Collections` 1.5.75, `nanoFramework.System.Text` 1.3.42, `nanoFramework.Logging` 1.1.161, `nanoFramework.Runtime.Events` 1.11.39 (the 2.0.1 release is the generics line). Everything newer is the 2.0 preview line, which needs preview firmware. Stable `nanoFramework.M2Mqtt` 5.1.226 already exposes the MQTT 5 surface the design relies on (`MqttReasonCode`, `SessionExpiryInterval`, `MaximumPacketSize`, `MessageExpiryInterval`, `ConnectionClosedRequest`). It also depends on `nanoFramework.System.Net` 1.11.64 and `nanoFramework.Runtime.Native` 1.7.11, and the `.nuspec` must list those.
- ~~Does `InternalsVisibleTo` work for nanoFramework test projects?~~ **Answered (task 1.1):** yes. A library with `[assembly: InternalsVisibleTo("NFUnitTest")]` compiles for the test project, and the test calls an `internal` method on nanoCLR. The link-the-sources fallback is not needed. The attribute names the test assembly, which must be called `NFUnitTest` (next point).
- **Tooling facts found in task 1.1** (Visual Studio 2026 Community with the nanoFramework extension, nanoCLR CLI 1.1.311, nanoCLR 1.18.0.18):
  - `.nfproj` projects build with `MSBuild.exe` from Visual Studio (`MSBuild\Current\Bin`). `dotnet build` and `dotnet test` do not work on them (`MSB4057: target "VSTest" does not exist`).
  - NuGet packages come from `packages.config`, restored with `nuget.exe restore` into a `packages` folder. The project references the DLLs by `HintPath`.
  - Tests run with `vstest.console.exe <test dll> /Settings:nano.runsettings /TestAdapterPath:<packages>\nanoFramework.TestFramework.<ver>\lib\net48`. The adapter downloads the nanoCLR Win32 instance itself.
  - **The test assembly must be named `NFUnitTest`** (`<AssemblyName>`), because the test launcher loads it by that name. With any other name the launcher dies with `System.ArgumentException` in `Assembly.Load`. The project and folder can keep the name `NServiceBus.Community.NanoframeworkMQTT.Tests`.
  - nanoCLR 1.18.0.18, the newest instance, runs tests built against `nanoFramework.CoreLibrary` 1.17.12, so `CLRVersion` need not be pinned.
  - Build output goes to `bin\Release` when no configuration is given and to `bin\Debug` for a solution build, so scripts should pass `/p:Configuration=Release` explicitly.
  - **`nanoFramework.CoreLibrary` is pinned to 1.17.11, not 1.17.12.** Every other stable package was built against 1.17.11, and referencing 1.17.12 gives an MSB3276 version-conflict warning in every project. The set above is otherwise unchanged.
- **nanoFramework string behavior found in task 2.2** (checked on nanoCLR 1.18.0.18, with a probe test):
  - A string is stored as UTF-8. For characters in the basic plane, `Length` and the indexer behave as in .NET. For a character outside the basic plane (an emoji, for example) `Length` is 2, but the indexer returns the high surrogate for **both** positions, so the low surrogate is lost. Copying such a string char by char (a `StringBuilder` loop, `new string(char[])`) corrupts it.
  - `Encoding.UTF8.GetBytes(string)` and `Encoding.UTF8.GetString(byte[], int, int)` round-trip the string correctly, including characters outside the basic plane. **All device code that handles text on the wire therefore works on UTF-8 bytes, never on chars.** This applies to the address mapping, the client ID sanitizer, and above all the envelope reader and writer (task group 3), which must decode JSON `😅` escapes into UTF-8 bytes and write raw UTF-8 bytes back out.
  - A string literal with an escaped surrogate pair (`"😀"`) is compiled into two replacement characters. A literal character outside the basic plane, written as itself in a UTF-8 source file, survives. Shared source in `src/Interop/` therefore writes such characters as themselves. Test code that needs a wire string with a surrogate pair builds it from UTF-8 bytes.
  - The compiler reads a unicode escape for a line or paragraph separator (` `, ` `) as a real line break, even inside a literal or a comment. Use numeric codes.
  - `string.Replace`, `char.IsWhiteSpace` and `Assert.Fail` do not exist. `StringBuilder` is in `nanoFramework.System.Text`, `Encoding.UTF8` is in `mscorlib`. `typeof(Outer.Inner).FullName` is `Outer+Inner`, as in .NET.
- **nanoFramework time behavior found in task 3.5** (nanoCLR 1.18.0.18): `new DateTime(ticks)` throws `ArgumentOutOfRangeException` for `DateTime.MaxValue` (9999-12-31), so the golden wire times stop at 2024-02-29. `TimeSpan` covers the whole `long` range, including the minimum value, which the constant format writes from an unsigned magnitude.
- **Findings while building the endpoint, tests, sample and interop host** (groups 4-9; nanoCLR 1.18.0.18, Visual Studio 2026):
  - **Message bodies do not use `nanoFramework.Json`.** Task 3.4 found that it does not read a null `int[]`, reads a `DateTime` fraction wrongly, ignores an offset, does not escape control characters and loses digits of some doubles, so the package has its own reader and writer (`JsonBodyReader`, `JsonBodyWriter`, on the same hardened scanner as the envelope reader). Nothing in the package uses `nanoFramework.Json` any more, so it is no longer a dependency of the package, and the README lists the eight packages that remain. This supersedes the sentence in D5 and the dependency list in the proposal.
  - **A nanoFramework assembly can hold only so many strings.** The metadata processor stops with "String table overflow in assembly 'NFUnitTest'. Can't use so many strings" once the unit tests grow past about 140 tests. The device unit tests are therefore **two assemblies**, both named `NFUnitTest` because the launcher loads that name: `NServiceBus.Community.NanoframeworkMQTT.Tests.nfproj` (wire codec, bodies, mapping, public API; `bin\Release`) and `NServiceBus.Community.NanoframeworkMQTT.Tests.Endpoint.nfproj` (the endpoint against the in-memory broker; `bin\EndpointRelease`). They share the folder, `packages.config` and `nano.runsettings`, so the folder listing of `src/` is unchanged, and `WirePayloads.cs` is split into two partial files (`WirePayloads.cs` and `WireCases.cs`) so that the endpoint tests link only the golden messages. CI and the README run both assemblies.
  - **The test adapter finds the project two folders above the assembly.** With the second assembly in `bin\Endpoint\Release` the adapter reports "No test is available"; in `bin\EndpointRelease` it finds the tests. That is why the output folder is not nested.
  - **Restoring needs no `nuget.exe`.** `msbuild <solution> /t:restore /p:RestorePackagesConfig=true` restores `packages.config` projects; `nuget restore` does the same (CI uses it).
  - **The sample's Wi-Fi helper.** `nanoFramework.System.Net` 1.11.64 has a `NetworkHelper` of its own that only uses a network configuration stored on the device, and the `nanoFramework.NetWorkHelper` 1.3.3 package (the one that takes an SSID and a password) has a type of the same name. The sample references the package under an extern alias. The package was built against older versions of its dependencies, so building the sample reports MSB3276 and CS1702 warnings.
  - **Address spelling in headers.** A .NET endpoint writes its own address as the topic (`Sales/Billing`) in `ReplyToAddress` and `FailedQ`, while a device writes its endpoint name (`Sales_Billing`). Both map to the same topic, so replies and returns to the sender work; the interop host compares the two after the `_` to `/` mapping.
  - **Interfaces as handler types.** A device cannot create an instance of an interface, so a message whose first registered match is an interface goes to the error queue as a deserialization failure. A subscription to an interface still works with a handler registered for a class that implements it.
  - **The interop host** uses the generic host (`AddNServiceBusEndpoint`), because self-hosting is obsolete in NServiceBus 10, and was run against a Mosquitto in Docker with a .NET stand-in for the device: all checks pass, and with no device attached the request/reply check fails and the exit code is 1.
