# NServiceBus.Community.Mqtt

An [NServiceBus](https://docs.particular.net/nservicebus/) transport for [MQTT](https://mqtt.org/) brokers, aimed at IoT scenarios where devices and services exchange NServiceBus messages through a broker such as Mosquitto.

The package is `NServiceBus.Community.Mqtt`. It targets .NET 10 and NServiceBus 10.

The repository also holds `NServiceBus.Community.NanoframeworkMQTT`, a small package that lets a device running [.NET nanoFramework](https://nanoframework.net/) (an ESP32, for example) be an endpoint on the same wire format. See [Devices](#devices-net-nanoframework).

## Installation

```
dotnet add package NServiceBus.Community.Mqtt
```

## Configuration

```csharp
using NServiceBus;

var endpointConfiguration = new EndpointConfiguration("Sales");

var transport = new MqttTransport("localhost", 1883);
endpointConfiguration.UseTransport(transport);

// The transport has no delayed delivery, and NServiceBus refuses to start an endpoint that retries with a delay on such a transport.
endpointConfiguration.Recoverability().Delayed(settings => settings.NumberOfRetries(0));

// Creates the endpoint's queue on the broker when the endpoint is set up, so that messages sent before it first starts are kept.
endpointConfiguration.EnableInstallers();
```

`MqttTransport` lives in the `NServiceBus` namespace, so the default `using NServiceBus;` is enough. What `EnableInstallers()` does for this transport is described under [Declared queues and holder sessions](#declared-queues-and-holder-sessions).

## How receiving works

NServiceBus needs queues, and MQTT has topics, so the transport builds a queue out of an MQTT 5 persistent session.

| NServiceBus | MQTT |
|---|---|
| An endpoint's input queue `Sales` | The topic `Sales`, subscribed to by the client `nsb.Sales` with `CleanStart=false`, QoS 1 |
| An address with `_` in it, such as `Sales_Billing` | The topic `Sales/Billing` (the client ID is `nsb.Sales/Billing`) |
| A message sent to the queue | A QoS 1 publish to the queue's topic |
| Messages waiting while the endpoint is offline | Messages the broker holds for the offline session |

### Requirements on the broker

- **MQTT 5.** The transport needs session expiry and the DISCONNECT reason codes.
- **Persistent sessions**, which the broker must keep across client disconnects. In Mosquitto that is `persistence true`.
- **A queue cap you can live with.** Brokers cap how many messages they keep for an offline session. Mosquitto's default is 1000, and it silently drops the newest messages beyond that: the sender is not told, because the publish itself succeeds. `src/mosquitto/mosquitto.conf` raises it to 100000. Set it to what your offline endpoints need.

### One consumer per queue

Every queue has exactly one consumer. **Each device or instance needs its own endpoint name.** Scale-out, where several instances of one endpoint share a queue and compete for its messages, is not supported: this transport is meant for IoT, where every device is its own endpoint.

If a second consumer starts on a queue that already has a live consumer, the broker lets the new one take the session over, and the displaced consumer is disconnected. The displaced endpoint raises a critical error:

> The consumer of queue 'Sales' was displaced by another consumer of the same queue. Only one consumer per queue is supported, so every instance needs its own endpoint name. This endpoint stops receiving from 'Sales'.

It does **not** reconnect. If it did, the two would take the session off each other for ever. The second consumer keeps receiving.

### Session expiry

The broker keeps a queue's session, with its subscriptions and the messages queued in it, for as long as the endpoint is offline, up to the session expiry. The default is **7 days**. After that the broker deletes the session and the messages in it.

```csharp
var transport = new MqttTransport("localhost", 1883)
{
    SessionExpiry = TimeSpan.FromDays(1)
};
```

The value is sent to the broker with every connection of the endpoint, so it must be between one second and the largest interval MQTT 5 can express.

### Acknowledgement and failures

Messages are delivered at least once (QoS 1), and the transport acknowledges a message to the broker only when the endpoint is done with it.

| Transaction mode | When the message is acknowledged |
|---|---|
| `ReceiveOnly` (default) | After the handler succeeds, or after recoverability reports the failure as handled. A message that was not acknowledged is delivered again when the endpoint reconnects or restarts. |
| `None` | Before the handler runs. A message is never delivered again, even if the endpoint stops in the middle of handling it. |

A message that cannot be decoded (anything that is not a message written by this transport) is logged at error level and discarded, so it cannot block the queue.

### Concurrency

The endpoint's maximum concurrency limits how many messages are handled at the same time, and can be changed while the endpoint runs. The broker is told to send at most `max(concurrency, 256)` unacknowledged messages at a time, so a backlog is not pulled into memory all at once. Raising the concurrency above that number at runtime has no effect before the endpoint restarts.

### Stopping and reconnecting

- **Stopping** waits for the messages being handled to finish. Handlers are only cancelled if the stop itself is cancelled, and a message cancelled that way is not acknowledged, so it is delivered again later. The session is kept.
- **A lost connection** is not fatal. The transport reconnects with a back-off of 1 second doubling up to 30 seconds, restores the endpoint's subscriptions, and logs every failed attempt. The broker delivers again whatever was not acknowledged.
- **Starting without a reachable broker** fails, with an error that names the host and the port.

### Purging on startup

If the endpoint is configured to purge its input queue on startup, the transport deletes the queue's session when it is set up, which discards the subscriptions and every queued message, and starts a new one. This happens before the endpoint's startup tasks run, so messages those tasks send to the endpoint's own queue are kept.

## Declared queues and holder sessions

A broker only keeps messages for a session that exists. So that a message sent to an address is kept even if nothing has ever received from it, the transport creates the sessions when the endpoint is set up, before it starts receiving. This happens when NServiceBus asks the transport to set up its infrastructure, which is when the endpoint is configured with `endpointConfiguration.EnableInstallers()`.

| Address | What the transport declares |
|---|---|
| The endpoint's input queue (and every other queue it receives from) | The queue's own session, `nsb.{queue}`, subscribed to the queue's topic. The endpoint resumes it when it starts. |
| An address the endpoint only sends to, usually `error` and `audit` | A **holder session**, `nsb.{address}.declared`, subscribed to the address's topic |

The first row is what makes a message sent to an endpoint that has not started yet, or is stopped, arrive when it does start. Without installers nothing is declared, and the queue's session is created by the first start of the endpoint: a message sent before that is not kept.

An address that no endpoint ever declared, and that has no subscriber at that moment, is best effort: the broker accepts the publish and then has nobody to give the message to, so it is discarded. The sender is not told.

### Holder sessions

A holder session is a different session from the one a consumer of the address uses (`nsb.{address}`). That is deliberate: declaring an address must never take a live consumer's session over. The consequence is that **a holder session is not read by any endpoint**. It only keeps a copy of everything sent to the address so that something can collect it later, such as a tool for failed messages. An endpoint that receives from the address uses its own session, which only holds the messages sent after that endpoint was first set up.

To collect what a holder session holds, connect as the holder with MQTT 5, a persistent session and QoS 1, and subscribe to the address's topic. Each message is the transport's JSON message (headers, and the body as base64). For the address `error`:

```
mosquitto_sub -V mqttv5 -c -i nsb.error.declared -x 600 -q 1 -t error -W 3
```

`-W 3` ends the command after three seconds without a message, and `mosquitto_sub` reports that as a timeout. Delivery is at least once, and a message that was delivered to this client is not delivered again. To throw everything the holder holds away, connect as the holder with a clean start (no `-c`) and disconnect:

```
mosquitto_sub -V mqttv5 -i nsb.error.declared -t error -E
```

For an address with a `_` in it, use the topic form: the address `Sales_Errors` is the topic `Sales/Errors`, and the holder is `nsb.Sales/Errors.declared`.

A holder session grows until it is collected, so it is bounded in two ways only:

- **Session expiry.** The broker deletes the session, and what is in it, `SessionExpiry` after the last client using it disconnected. Every endpoint that starts and declares the address connects and disconnects, which starts the countdown again.
- **The broker's queue cap** for one session. Mosquitto's default is 1000 and `src/mosquitto/mosquitto.conf` raises it to 100000. Messages beyond the cap are dropped without telling the sender.

Choose a `SessionExpiry` that matches how long you are willing to keep messages nobody collects, and collect or delete the holder sessions of `error` and `audit` if no tool does. If an endpoint in your system does receive from such an address, its messages are also held in the holder, where nothing reads them, so delete the holder (or drain it regularly) rather than let it fill up to the cap.

Endpoints that start at the same moment all declare the same holder sessions. The broker lets only one client at a time use a client ID, so the declarations take turns, and the transport repeats a declaration that was cut short, after a random pause.

## Publish/subscribe

Events use MQTT topics directly, so there is no subscription store and no publisher registration to configure.

- **The topic of an event** is `events/` followed by the event type's full, namespace-qualified name: `Sales.OrderPlaced` is published to `events/Sales.OrderPlaced`. A type with the same short name in another namespace has another topic. Characters that cannot appear in a topic name become `.`, so a nested type `Sales.Container+Inner` is `events/Sales.Container.Inner` and `Sales.Wrapper<string>` is `events/Sales.Wrapper.1.System.String`. The topic never contains an MQTT wildcard.
- **Subscribing** adds a plain subscription to that topic to the subscribing endpoint's own queue session. Every endpoint that subscribed to an event gets its own copy, and an endpoint that did not gets nothing. Subscribing and unsubscribing work before and after the endpoint starts. The broker keeps the subscriptions in the queue's session, and the transport applies them again after a reconnect.
- **Polymorphism.** A subscriber to a base class or an interface receives the events derived from it. To make that work, publishing an event sends one copy for each type in its hierarchy: the event's own type, its base classes and the interfaces it implements, without `object`, the `System` types and NServiceBus's `IMessage`, `IEvent` and `ICommand`. An endpoint subscribed to more than one of those types would receive more than one copy, so it processes exactly one of them: the one for the first type in the message's `NServiceBus.EnclosedMessageTypes` header that the endpoint is subscribed to. The other copies are acknowledged and dropped. A message without that header, for example one a device published to an event topic, is processed as it is.
- **Explicit topics.** `transport.SubscribeTo("sensors/gate")` subscribes every receiver of the endpoint to that topic at startup and after a reconnect, and the messages that arrive on it are processed like any other. They have to be in the transport's message format.
- **No scale-out.** Every subscribing endpoint has one queue and one consumer, as described above. Several instances with the same endpoint name displace each other, so each instance needs its own endpoint name, and each then gets its own copy of every event.

**Breaking change from 1.x:** 1.x published to `events/{ShortTypeName}`, for example `events/OrderPlaced`. 2.x uses the full name, so 1.x and 2.x endpoints do not exchange events. Upgrade the endpoints that publish and subscribe to each other's events together.

## Supported capabilities

This is the .NET transport, `NServiceBus.Community.Mqtt`. What the [device package](#devices-net-nanoframework) supports and does not support is described there.

| Capability | Supported |
|---|---|
| Native publish/subscribe | Yes |
| Transaction mode `ReceiveOnly` (default) | Yes |
| Transaction mode `None` | Yes |
| Transaction modes `SendsAtomicWithReceive` and `TransactionScope` | No |
| Delayed delivery | No, so delayed retries must be switched off (see [Configuration](#configuration)) |
| Time to be received (TTBR) | Yes, as the MQTT 5 message expiry interval (see below). A [device](#devices-net-nanoframework) cannot send it yet. |
| Scale-out of one endpoint across several instances sharing a queue | No, each queue has a single consumer |

Requesting an unsupported transaction mode fails at endpoint startup with the standard NServiceBus error.

### Time to be received

A message sent with a time to be received (`[TimeToBeReceived("00:00:30")]` on the message, or the equivalent convention) is published with that value as its MQTT 5 message expiry interval, and the broker does not deliver it after that time. That includes a message that is waiting in the session of an endpoint that is offline: it is dropped, and the endpoint never sees it. MQTT counts in whole seconds, so a fraction of a second is rounded up, and a value MQTT cannot express (`TimeSpan.MaxValue`, for example) means the message does not expire. The time is counted from when the broker accepted the message, so it covers the wait in the broker's queue but not the time the message then spends waiting inside the endpoint to be handled. Every copy of a published event carries the same time.

## Upgrading from 1.x to 2.0

2.0 is a breaking release. The package ID is the same, `NServiceBus.Community.Mqtt`.

1. **Platform.** 2.0 targets .NET 10 and NServiceBus 10 (10.2.9 or later, below 11), and nothing else: an application on .NET 8 or earlier, or on NServiceBus 8 or 9, cannot use it. Move your endpoints to NServiceBus 10 first (see the [NServiceBus upgrade guides](https://docs.particular.net/nservicebus/upgrades/)), then to 2.0. `MqttTransport.ToTransportAddress` is gone because NServiceBus 10 removed the member it overrode; NServiceBus resolves addresses through the transport infrastructure, and the `_` to `/` mapping is unchanged.
2. **Namespace.** The assembly was `NserviceBus.Mqtt` and is now `NServiceBus.Community.Mqtt`. Replace `using NserviceBus.Mqtt;` with `using NServiceBus;`. `MqttTransport` is the only public type: the message pump, dispatcher, subscription manager and the rest are internal now.
3. **The broker must support MQTT 5 and persistent sessions.** The transport connects with MQTT 5 and keeps each queue in a persistent session, so the broker has to keep sessions across disconnects. See [Requirements on the broker](#requirements-on-the-broker). 1.x connected without keeping a session, so nothing was kept for an endpoint that was offline.
4. **One consumer per queue.** In 1.x every instance of an endpoint received every message sent to it. Now a queue has one consumer, and a second instance on the same queue displaces the first, which reports a critical error. **Give every device or instance its own endpoint name.** See [One consumer per queue](#one-consumer-per-queue).
5. **Event topics changed.** 1.x published to `events/{ShortTypeName}`, 2.x to `events/{full type name}` and along the event's type hierarchy. A 1.x endpoint and a 2.x endpoint do not exchange events, so **upgrade the endpoints that publish and subscribe to each other's events together.** See [Publish/subscribe](#publishsubscribe).
6. **The message format is unchanged,** and so is the `_` to `/` translation of addresses. A 2.x endpoint reads a message sent by a 1.x endpoint.
7. **Review your settings.** `SessionExpiry` is new (7 days by default), and [`EnableInstallers()`](#declared-queues-and-holder-sessions) now decides whether queues are created before an endpoint first starts.

## Devices (.NET nanoFramework)

`NServiceBus.Community.NanoframeworkMQTT` is a class library for [.NET nanoFramework](https://nanoframework.net/), the .NET runtime for microcontrollers. It makes a device an NServiceBus endpoint of its own: the device has its own queue, receives commands, replies, publishes and subscribes to events, and retries a failing message and then moves it to the error queue, as a .NET endpoint does. On the wire it cannot be told apart from a .NET endpoint that uses this transport, so the two exchange messages directly through the broker.

It is not NServiceBus itself, which needs far more than a microcontroller has. It runs on stock nanoFramework firmware (no generics, no `async`), has a small synchronous API, and leaves out everything that needs a pipeline, persistence or a container. See [What the device package does not do](#what-the-device-package-does-not-do).

### What you need

- An ESP32 with Wi-Fi, running nanoFramework firmware. The reference board for the board test is a plain ESP32 development board.
- Firmware that matches the package versions below. The packages are pinned to exact versions, and the native assemblies in the firmware have to be the ones they were built against. `nanoff --target <your board> --update` installs the current stable firmware.
- A broker that supports MQTT 5 and persistent sessions, as for the [.NET transport](#requirements-on-the-broker).
- To build: Windows, and either Visual Studio with the nanoFramework extension or MSBuild with the nanoFramework build components. The device projects are `.nfproj` files, which `dotnet build` cannot build.

The device package depends on exactly these packages (NuGet also installs what `nanoFramework.M2Mqtt` needs, `System.Threading` and `System.IO.Streams`):

| Package | Version |
|---|---|
| `nanoFramework.CoreLibrary` | `1.17.11` |
| `nanoFramework.Logging` | `1.1.161` |
| `nanoFramework.M2Mqtt` | `5.1.226` |
| `nanoFramework.Runtime.Events` | `1.11.39` |
| `nanoFramework.Runtime.Native` | `1.7.11` |
| `nanoFramework.System.Collections` | `1.5.75` |
| `nanoFramework.System.Net` | `1.11.64` |
| `nanoFramework.System.Text` | `1.3.42` |

A unit test (`DeviceReadmeTests`) fails if this table and the dependencies in the package's `.nuspec` list different versions.

### Installing

The package is not published to nuget.org yet. The Windows CI job packs it and uploads it as the `device-package` artifact; add the folder that holds the `.nupkg` as a NuGet source and install it into your nanoFramework project:

```
nuget install NServiceBus.Community.NanoframeworkMQTT -Source <folder with the .nupkg>
```

### Configuring and starting an endpoint

The endpoint name is also the queue address, and it names the persistent MQTT 5 session that holds the messages sent to the device while it is offline. It maps to a topic the way a .NET endpoint's name does (`_` becomes `/`), so a .NET endpoint sends to `Gate_01` and the device receives it. Creating the configuration with a name that is empty, contains `+` or `#`, or starts with `$` fails with an error that names the name.

This is the sample app's code, from `Program.cs`:

```csharp
var configuration = new DeviceEndpointConfiguration(SampleSettings.EndpointName, SampleSettings.BrokerHost, SampleSettings.BrokerPort);
configuration.SessionExpiry = TimeSpan.FromHours(1);
configuration.OnCriticalError = OnCriticalError;
configuration.RouteToEndpoint(typeof(ValveEventAcknowledged), SampleSettings.HostEndpointName);
configuration.RegisterHandler(typeof(OpenValve), new OpenValveHandler());
configuration.RegisterHandler(typeof(ValveEvent), new ValveEventHandler());
configuration.RegisterHandler(typeof(AlwaysFails), new AlwaysFailsHandler());

var endpoint = DeviceEndpoint.Start(configuration);
```

`Start` connects, resumes the device's session, subscribes to its queue and to its events, waits for the broker to confirm, and only then returns. If the broker cannot be reached or refuses the connection, it throws an exception that names the host, the port and the reason, and leaves no connection or thread behind.

The other settings, with their defaults:

| Setting | Default | What it does |
|---|---|---|
| `UseCredentials(username, password)` | none | Sent when connecting. |
| `SessionExpiry` | 7 days | How long the broker keeps the queue's session, and the messages in it, while the device is offline. From one second up to 4294967295 seconds. Keep it modest on a device: see [No flow control](#acknowledged-on-receipt). |
| `ImmediateRetries` | 5 | How many times a failed message is handled again at once. `0` turns retries off. |
| `ErrorQueue` | `error` | Where failed messages go. |
| `MaximumPacketSize` | 16384 bytes | The largest MQTT packet the device declares it can receive, and the largest it sends. |
| `DispatchTimeout` | 10 seconds | How long a send, a publish, a subscribe or an unsubscribe waits for the broker. |
| `StopDrainTimeout` | 10 seconds | How long `Stop` keeps processing the messages that have already arrived. |
| `Logger` | the debug output | An `ILogger` from `nanoFramework.Logging`. |
| `OnCriticalError` | logs at error level | Called with a description and the exception, if there is one. A critical error does not stop the endpoint by itself. |

```csharp
configuration.UseCredentials("gate-01", "a-secret");
configuration.SessionExpiry = TimeSpan.FromHours(1);
configuration.ImmediateRetries = 3;
configuration.ErrorQueue = "error";
configuration.MaximumPacketSize = 8192;
configuration.DispatchTimeout = TimeSpan.FromSeconds(5);
```

The values are checked when they are set. A port outside 1-65535, an empty host, a negative number of retries, a session expiry outside its range and a timeout that is not positive all throw an exception that names the value.

**Set the clock first.** NServiceBus writes the time a message was sent, and the time it failed, into its headers. A device that has not been told the time starts in the past, so those headers would be wrong. The sample sets the clock with SNTP before it starts the endpoint (`SetClock` in `Program.cs`).

**Stopping.** `endpoint.Stop()` stops taking new messages from the intake, lets the message in progress finish (stop waits for the handler, because nanoFramework has no cancellation), processes the messages that have already arrived until they are done or `StopDrainTimeout` passes, logs how many were left, and disconnects in a way that keeps the session and its subscriptions. A second `Stop` does nothing, and sending, publishing, subscribing or unsubscribing after stop throws. Do not call `Stop` from a handler.

```csharp
endpoint.Stop();
```

**Reconnecting.** If the connection is lost, the endpoint reconnects by itself with a back-off of 1 second doubling up to 30 seconds, logs every failed attempt, subscribes again to everything, and starts the back-off again at 1 second after a success. Nothing is buffered while the device is offline: a send or publish fails at once with an error that names the destination, the host and the port.

### Acknowledged on receipt

`nanoFramework.M2Mqtt` acknowledges a message to the broker (the PUBACK of QoS 1) **when it arrives, before it is handled**. There is no way to change that. The device handles one message at a time, so a message waits in an in-memory intake while the one before it is handled. This differs from the [.NET transport](#acknowledgement-and-failures), which acknowledges after the handler has succeeded.

What that means: if the device loses power, crashes, reboots, is stopped past the drain timeout or is displaced while a message is being handled or is waiting in the intake, **that message is lost**; the broker does not deliver it again. Messages the broker is still holding in the device's session at that moment are not affected, so a backlog that built up while the device was off is delivered when it starts.

Related limits:

- **No flow control.** After a long offline period the broker sends the whole backlog at once, the device acknowledges all of it, and all of it lands in RAM. `MaximumPacketSize` bounds each message, and a modest `SessionExpiry` bounds the backlog. Mosquitto's cap on queued messages (`max_queued_messages`) applies to every session of the broker, so it is a trade-off with what your .NET endpoints need.
- **Losses are logged.** The endpoint logs a warning when it stops with messages left, and an error when it drops messages because it was displaced.

### One device per endpoint name

Every device needs its own endpoint name, as every instance of a .NET endpoint does. If another client takes over the device's session (the broker disconnects the device with MQTT 5 reason code `0x8E`), the device raises a critical error that names the address, says that only one consumer per queue is supported and that every device needs its own endpoint name, and **does not reconnect**: it would otherwise take the session back and the two would displace each other for ever. From then on its sends and publishes fail with an error that says the endpoint was displaced, and the messages it had received but not yet handled are dropped and counted in the log.

### Maximum packet size and the dispatch timeout

The device tells the broker the largest packet it can receive (`MaximumPacketSize`, 16384 bytes by default), so that the broker does not send it messages it cannot hold. **The broker drops a larger message addressed to the device without telling the sender**: the publish succeeds, and the device never sees the message. Raise the setting if your messages are larger, and mind the device's RAM.

The same limit applies to what the device sends. The size of the MQTT PUBLISH packet is computed exactly before anything is published, and a message that is too large fails with an error that names both sizes. For an event, every copy is checked before the first one is published.

A send or publish from outside a handler returns only after the broker has acknowledged every MQTT publish it made, and fails with an error that names the destination, the host and the port if the device is not connected, the broker rejects the publish or the broker does not answer within `DispatchTimeout`. **A send that timed out may still have reached the broker**, so the receiver can get it later: treat a timeout as "unknown", not as "not sent".

### Sharing contracts with .NET

Message types are matched by their namespace-qualified name, as NServiceBus does for types without an assembly. A device and a .NET endpoint therefore have to use the same namespace and class name for the same message, and the simplest way to guarantee that is to **share the source file** of the contracts. The marker interfaces `IMessage`, `ICommand` and `IEvent` are in the `NServiceBus` namespace on the device, too, so a contract file with `using NServiceBus;` compiles for .NET and for nanoFramework unchanged. The sample and the interop host share `src/Interop/InteropContracts.cs` this way. A contract looks like this:

```csharp
public class OpenValve : ICommand
{
    public string ValveId { get; set; }
    public int Percent { get; set; }
}
```

A message class needs a public parameterless constructor and public read/write properties. A device and a .NET endpoint exchange, unchanged within each type's precision, properties of these types:

- `string`, `bool`, `int`, `long` and `double`
- `DateTime`, in UTC
- enums
- nested classes of the same shape
- arrays of any of these

**Other types are not supported** (no `decimal`, `Guid`, `DateTimeOffset`, `TimeSpan`, `float`, `short`, `byte`, `char`, dictionaries or lists). A message with a member of another type fails with an error that names the member. Bodies are UTF-8 JSON, the `NServiceBus.ContentType` header is `application/json`, and a body the device writes is read by NServiceBus's System.Text.Json serializer with its default settings, and the other way round.

### Handling messages

Handlers are registered explicitly, one instance for each registration, and that instance is reused for every message. Processing is single-threaded, so it never runs concurrently. `IHandleMessages` is not generic, because nanoFramework has no generics yet: a handler casts the message. This is the sample's handler for `OpenValve`:

```csharp
public void Handle(object message, IMessageHandlerContext context)
{
    var command = (OpenValve)message;

    var status = new ValveStatusResponse();
    status.ValveId = command.ValveId;
    status.IsOpen = true;
    status.Percent = command.Percent;
    context.Reply(status);

    var opened = new ValveOpened();
    opened.ValveId = command.ValveId;
    opened.Percent = command.Percent;
    context.Publish(opened);
}
```

The device finds the type of a message from `NServiceBus.EnclosedMessageTypes`: it reads the namespace-qualified names (ignoring any assembly name), picks the first one that is registered on the device, and deserializes the body into that type. It then invokes every handler registered for that type, or for one of its base classes or interfaces, once each, in the order they were registered. So a device that only knows `ValveEvent` still handles a `ValveOpened` that derives from it. Register handlers for classes: the device cannot create an instance of an interface, so a message whose only registered match is an interface goes to the error queue.

The context exposes `MessageId`, `ReplyToAddress` and `MessageHeaders`, which is a copy for the attempt, so a change a handler makes does not reach the next attempt.

### Sending, routing and replying

A send goes to the destination in its options, or else to the endpoint its type is routed to with `RouteToEndpoint`. A send with neither fails before anything is dispatched, with an error that names the message type, and a destination is validated like an endpoint name. Sending or replying with an event fails, and so does publishing a type that is not an event.

```csharp
var command = new OpenValve();
command.ValveId = "V-1";
command.Percent = 75;

// to the endpoint the type is routed to
session.Send(command);

// or to an endpoint named in the options, with a header of your own
var options = new SendOptions();
options.Destination = "Plant_Backup";
options.SetHeader("my.header", "a value");
session.Send(command, options);
```

A **reply** goes to the incoming message's `NServiceBus.ReplyToAddress` with the intent `Reply`, carries the conversation and correlation headers, and, if the request came from a saga (`NServiceBus.OriginatingSagaId` and `NServiceBus.OriginatingSagaType`), carries them as `NServiceBus.SagaId` and `NServiceBus.SagaType`, so the saga that sent the request handles the reply. Replying to a message that has no reply-to address fails, and the failure counts as a handler failure.

Every message carries `NServiceBus.MessageId` (a new GUID), `MessageIntent`, `EnclosedMessageTypes` (the names without an assembly, of the type, its base classes and its interfaces), `ContentType`, `ConversationId`, `CorrelationId`, `ReplyToAddress` (the device's own address), `OriginatingEndpoint` and `TimeSent`. Sent from a handler, a message continues the incoming message's conversation and is related to it; sent from outside a handler, it starts a conversation and is correlated to itself.

**Messages sent from a handler are dispatched after the handlers succeed.** They are collected while the message is handled, dispatched in the order they were issued once every handler has completed, and discarded if a handler throws. If dispatching them fails (the broker rejects one, or the connection is down), that counts as a processing failure and the message is handled again as an immediate retry. **The messages that were already dispatched before the failure are dispatched again by the retry**, so a receiver can see a duplicate: make the receivers idempotent.

### Publishing and subscribing

Event topics are the ones the .NET transport uses: `events/` followed by the type's namespace-qualified name, with `+`, `/`, `#`, `` ` ``, `[`, `]`, `,`, space and `*` replaced by `.`. The nested event type `Sales.Container+Inner` is `events/Sales.Container.Inner`, and `Contracts.ValveOpened` is `events/Contracts.ValveOpened`.

Publishing an event makes one copy for each type in its hierarchy: its own type, its base classes (nearest first), and its interfaces (in ordinal order of their full names), without `object`, the `System` types and the message markers. Every copy has the same message ID and headers, so a .NET subscriber to the base class or to an interface receives it. Publishing returns after the broker has acknowledged every copy, and the first copy that fails stops it, with an error that names its topic.

```csharp
var opened = new ValveOpened();
opened.ValveId = "V-1";
opened.Percent = 100;

// one copy for ValveOpened, one for ValveEvent, and one for each interface
endpoint.Publish(opened);

// handled events are subscribed to when the endpoint starts, and others can be subscribed to at any time
endpoint.Subscribe(typeof(PriceChanged));
endpoint.Unsubscribe(typeof(PriceChanged));
```

**Subscriptions.** A device is subscribed, when it starts, to every event type that has a registered handler (auto-subscribe) and to the ones given to `configuration.Subscribe`. `Subscribe` and `Unsubscribe` also work while the endpoint runs, and return after the broker has confirmed. They are applied again after every reconnect. After `Unsubscribe`, the device receives no more events of that type, and its other subscriptions are unaffected. `configuration.Unsubscribe` before start keeps a handled event type from being subscribed to automatically.

**One copy of an event is processed.** A device subscribed to several types of one event's hierarchy receives several copies. It processes exactly one: the copy for the first type in `NServiceBus.EnclosedMessageTypes` that the device is subscribed to. The others are acknowledged and dropped. This is the rule the .NET transport uses.

### Recoverability

When handling a message fails (a handler throws, or the messages it sent cannot be dispatched), the device handles the message again at once, up to `ImmediateRetries` times (5 by default; `0` turns retries off). Every attempt starts from the original headers and a freshly deserialized body. **There are no delayed retries**: nanoFramework devices have no delayed delivery.

When the last attempt fails, the device sends the message to the error queue with its original body, headers and message ID, plus the failure headers NServiceBus 10 uses: `NServiceBus.FailedQ` (the device's address), `NServiceBus.TimeOfFailure`, `NServiceBus.ExceptionInfo.ExceptionType`, `NServiceBus.ExceptionInfo.Message`, `NServiceBus.ExceptionInfo.StackTrace`, `NServiceBus.ExceptionInfo.InnerExceptionType` when there is an inner exception, and `NServiceBus.ProcessingEndpoint`. (There is no `Source` or `HelpLink`: nanoFramework exceptions have neither.) The device then goes on with the next message. If forwarding fails, the device raises a critical error, waits with a back-off of 1 second doubling up to 30 seconds, and tries again, processing nothing else, until it succeeds or the endpoint stops. A message still waiting when the endpoint stops is lost, and the log says so with its message ID.

Some messages skip the retries because they can never succeed, and go to the error queue at once, with an exception of the type `NServiceBus.MessageDeserializationException` that describes the reason:

- a message with no `NServiceBus.EnclosedMessageTypes` header
- a message none of whose types is registered on the device
- a message whose `NServiceBus.ContentType` is present and is not JSON
- a message whose body cannot be deserialized into the type

A payload that is not a message at all (it is not the transport's JSON envelope) is logged at error level, with the topic it arrived on, and discarded. It does not go to the error queue.

**Declare the error queue from a .NET endpoint.** A device does not declare the `error` queue. The .NET transport declares it through installers, as the holder session `nsb.error.declared` (see [Declared queues and holder sessions](#declared-queues-and-holder-sessions)). So either some .NET endpoint of your system runs with `EnableInstallers()`, or you create the holder session yourself, for example with `mosquitto_sub -V mqttv5 -c -i nsb.error.declared -x 600 -q 1 -t error -W 1`. Without it, a failed message from a device is best effort: the broker has nobody to give it to and discards it.

### Time to be received

**Not available yet.** A device cannot send a message with a time to be received, so a device message never expires. It needs a `Publish` overload in `nanoFramework.M2Mqtt` that sets the MQTT 5 message expiry interval, which the package does not have yet. Messages that *.NET* sends to a device can have a time to be received, and the broker honors it.

### What the device package does not do

Sagas, the outbox and persistence; delayed delivery and delayed retries; audit forwarding; subscribing to raw topics; TLS and client certificates; devices that only send (a device always runs an endpoint with a queue); buffering messages while offline; concurrent processing of messages; message conventions other than the marker interfaces, dependency injection and pipeline extensibility; generic APIs such as `IHandleMessages<T>`; and ServiceControl, which only supports Particular's own transports.

## Running the tests

There are three .NET test projects under `src/`, and the device unit tests, which are described [below](#device-unit-tests):

| Project | What it covers | Needs a broker |
|---|---|---|
| `NServiceBus.Community.Mqtt.Tests` | Unit tests for address mapping, topic names, the wire format, validation and the public API approval | No |
| `NServiceBus.Community.Mqtt.TransportTests` | The standard NServiceBus transport test suite (`NServiceBus.TransportTests.Sources`), plus MQTT-specific tests | Yes |
| `NServiceBus.Community.Mqtt.AcceptanceTests` | The standard NServiceBus acceptance test suite (`NServiceBus.AcceptanceTests.Sources`) | Yes |

The suites are used as published, never copied or edited. The repository only holds the adapters, constraints and fixtures that wire them to this transport.

### What you need

- The .NET SDK that `global.json` asks for (10.x). The packages and test projects target `net10.0`, so no other .NET runtime is needed.
- A running Docker engine with Linux containers (on Windows, Docker Desktop in its default mode).

No broker needs to be installed. The transport and acceptance tests start their own [Mosquitto](https://mosquitto.org/) 2.x broker (`eclipse-mosquitto:2`) in a container, with `src/mosquitto/mosquitto.conf`, on a free port, and remove it when the run ends. The first run pulls the image, which takes a little longer.

```
dotnet test src/NServiceBus.Community.Mqtt.slnx
```

You can also run a single project, for example `dotnet test src/NServiceBus.Community.Mqtt.TransportTests`. The two broker-backed projects each start their own container, so they can run at the same time without seeing each other's sessions or queued messages.

If Docker is not running, the run fails within seconds with a message that says Docker is required and names the environment variables below, instead of timing out test by test.

### Using a broker you started yourself

Set `MqttTransport_Server` and `MqttTransport_Port` and the tests connect to that broker, and start no container. Both variables must be set. The tests check once, before running, that the broker accepts an MQTT 5 connection, and fail within seconds, naming the host, port and both variables, if it does not.

Start Mosquitto 2.x by hand with the repository's configuration. The configuration matters: Mosquitto 2.x refuses connections from outside the machine without a listener, the transport needs sessions to be kept across disconnects, and Mosquitto's default cap of 1000 queued messages per session is too low for the suites.

```
docker run -d --name mqtt-test-broker -p 1883:1883 -v "${PWD}/src/mosquitto/mosquitto.conf:/mosquitto/config/mosquitto.conf" eclipse-mosquitto:2
```

Then point the tests at it (PowerShell first, bash second):

```
$env:MqttTransport_Server = "127.0.0.1"; $env:MqttTransport_Port = "1883"
export MqttTransport_Server=127.0.0.1 MqttTransport_Port=1883
```

```
dotnet test src/NServiceBus.Community.Mqtt.slnx
```

Stop and remove the broker with `docker rm -f mqtt-test-broker`. The tests delete the broker sessions they use before and after each test, and use a short session expiry, so a broker you keep running does not need to be reset between runs, and a run you killed leaves nothing that breaks the next one.

### Device unit tests

The device package has unit tests that run on the nanoCLR virtual device, so they need no board, no broker and no Docker. They use an in-memory broker in place of the connection, and cover address and client ID mapping, the envelope codec and message bodies, outgoing headers and routing, type resolution and handlers, dispatch from handlers, immediate retries and the error queue, event topics and the type hierarchy, the one-copy rule, reply and saga headers, reconnect and takeover, stop, and the public API of the assembly. Golden wire payloads in `src/Interop/WirePayloads.cs` are checked from both sides: the .NET unit tests produce the payloads the .NET transport writes and read the ones the device writes, and the device tests do the reverse.

The nanoFramework solution is `src/NServiceBus.Community.NanoframeworkMQTT.sln`. It is apart from the .NET solution (`src/NServiceBus.Community.Mqtt.slnx`) because `dotnet build` cannot build `.nfproj` projects. It builds **only on Windows**, with MSBuild and the nanoFramework build components (Visual Studio with the nanoFramework extension has both), and holds the device package, its tests and the sample app. From a Developer PowerShell:

```
msbuild src\NServiceBus.Community.NanoframeworkMQTT.sln /t:restore /p:RestorePackagesConfig=true
msbuild src\NServiceBus.Community.NanoframeworkMQTT.sln /p:Configuration=Release
```

(`nuget restore src\NServiceBus.Community.NanoframeworkMQTT.sln` restores the same packages.) Pass `/p:Configuration=Release` explicitly: the output folder depends on it.

There are two test assemblies, `bin\Release\NFUnitTest.dll` and `bin\EndpointRelease\NFUnitTest.dll`, both under `src\NServiceBus.Community.NanoframeworkMQTT.Tests`. They share a name because the nanoFramework test launcher loads that name, and there are two because a nanoFramework assembly can hold only so many strings. Run them with `vstest.console.exe`, which Visual Studio installs, and the adapter in the `nanoFramework.TestFramework` package, which downloads the nanoCLR instance itself:

```
$adapter = "src\packages\nanoFramework.TestFramework.3.0.80\lib\net48"
$tests = "src\NServiceBus.Community.NanoframeworkMQTT.Tests"
vstest.console.exe "$tests\bin\Release\NFUnitTest.dll" /Settings:"$tests\nano.runsettings" /TestAdapterPath:$adapter
vstest.console.exe "$tests\bin\EndpointRelease\NFUnitTest.dll" /Settings:"$tests\nano.runsettings" /TestAdapterPath:$adapter
```

Or open the solution in Visual Studio and use Test Explorer. Both runs have to pass.

### The board test with the interop host

The device unit tests cannot reach a broker, so a manual test proves the last step on a real board. It is run by hand, not in CI (CI builds the sample app and the interop host). The reference board is an ESP32 with Wi-Fi. The interop host (`src/NServiceBus.Community.NanoframeworkMQTT.InteropHost`) is a .NET console app that runs an NServiceBus endpoint next to the board's, checks that they understand each other, and exits with a non-zero code if any check fails.

1. **Start Mosquitto with the repository's configuration**, so that it keeps sessions and accepts connections from the network:

   ```
   docker run -d --name mqtt-board-test -p 1883:1883 -v "${PWD}/src/mosquitto/mosquitto.conf:/mosquitto/config/mosquitto.conf" eclipse-mosquitto:2
   ```

2. **Edit `src/NServiceBus.Community.NanoframeworkMQTT.Sample/SampleSettings.cs`.** Set `WifiSsid` and `WifiPassword` to your network, and `BrokerHost` to the address of the machine that runs Mosquitto *as the board sees it* (its address on the network, not `localhost`). Leave `EndpointName` and `HostEndpointName` as they are. Do not commit your credentials.
3. **Flash the board.** Put firmware on it that matches the [package versions](#what-you-need) (`nanoff --target <your board> --update`), then deploy the sample: in Visual Studio, set the sample as the startup project and choose *Deploy*. (`nanoff` can also deploy the `.bin` image that the build writes next to the sample's `.exe`; see the [nanoff documentation](https://docs.nanoframework.net/content/nano-firmware-flasher/).) The board connects to Wi-Fi, sets its clock, starts the endpoint `NanoInterop_Device`, and writes `The endpoint 'NanoInterop_Device' is running. Free memory after start: ...` to the debug output.
4. **Run the interop host** against the same broker:

   ```
   $env:MqttTransport_Server = "127.0.0.1"; $env:MqttTransport_Port = "1883"
   dotnet run --project src/NServiceBus.Community.NanoframeworkMQTT.InteropHost
   ```

   `MqttTransport_Server` and `MqttTransport_Port` are the variables the test projects use, too. `InteropHost_TimeoutSeconds` (default 20) sets how long each check waits.

The host runs these checks, prints `PASS`, `FAIL` or `SKIP` for each, and exits with 0 only if none failed:

| Check | What it shows |
|---|---|
| A .NET command is handled by the device, which replies | A command routed to the device is handled, and the reply is correlated to the command (`NServiceBus.RelatedTo`) |
| A device event reaches a .NET subscriber | The device's `ValveOpened` is handled once by the .NET endpoint |
| A .NET event reaches the device through the base type | A `ValveOpened` published by .NET is handled by the device's `ValveEvent` handler, which answers with a command |
| A message the device always fails ends up in the error queue | After the configured immediate retries (the sample uses the default 5, so 6 attempts), the message is in `error`, read as the holder session `nsb.error.declared`, with the failure headers. Reading consumes what the holder session holds. |
| A device message with a time to be received expires | `SKIP`: time to be received is not available on the device yet |

If the board does not answer, the first check fails after the timeout and the host exits with a non-zero code. Stop the broker with `docker rm -f mqtt-board-test`.

**Board test record.** The procedure has not been carried out on a board yet. When it has, record here the date, the board, the firmware version, and the free memory that the sample logs after it starts.

### Known exclusions

Some acceptance tests cannot pass because this transport has one consumer per queue and no scale-out. They are filtered out by `src/NServiceBus.Community.Mqtt.AcceptanceTests/acceptance.runsettings`, which `dotnet test` and CI pick up automatically. Each one is recorded, with the reason and who approved it, in [`src/KNOWN-EXCLUSIONS.md`](src/KNOWN-EXCLUSIONS.md). A unit test fails if the filter and that file list different tests.

### Continuous integration

`.github/workflows/ci.yml` runs on pull requests and on pushes to `main`. It has two jobs.

- **Linux.** On an `ubuntu-latest` runner, which has Docker, it builds the .NET solution (`src/NServiceBus.Community.Mqtt.slnx`, which includes the interop host) and runs the three .NET test projects once. The test projects start Mosquitto themselves, so the job has no broker step.
- **Windows.** On a `windows-latest` runner it sets up the nanoFramework build components, restores and builds the nanoFramework solution (the device package, its tests and the sample app), runs the device unit tests on the nanoCLR virtual device, and packs the device package, which it uploads as the `device-package` artifact.

Both jobs upload their `.trx` results, and the workflow fails if any build, test or pack step fails.

## License

MIT, see [LICENSE](LICENSE).
