# Spec Delta

## Purpose

Defines how devices publish and subscribe to events so that they interoperate with .NET endpoints using `NServiceBus.Community.Mqtt`. It covers event topics, publishing along the type hierarchy, subscriptions, and processing exactly one of the copies an endpoint receives.

## ADDED Requirements

### Requirement: Same event topics as the .NET transport
The topic of an event type SHALL be derived the same way the .NET transport derives it for a non-generic type. The topic is `events/` followed by the type's namespace-qualified name. The characters `+`, `/`, `#`, `` ` ``, `[`, `]`, `,`, space and `*` are replaced with `.`. A device and a .NET endpoint SHALL therefore use the same topic for the same event type.

#### Scenario: Nested type
- **WHEN** the topic of the nested event type `Sales.Container+Inner` is derived
- **THEN** it is `events/Sales.Container.Inner`

#### Scenario: .NET subscriber receives a device event
- **WHEN** a .NET endpoint is subscribed to `Contracts.ValveOpened` and a device publishes `Contracts.ValveOpened`
- **THEN** the .NET endpoint's handler is invoked once

### Requirement: Publishing along the type hierarchy
Publishing an event SHALL publish one copy for each type in its hierarchy:
- the event's own type
- its base classes, nearest first
- the interfaces it implements, in ordinal order of their full names

`object`, `System` types and the message marker interfaces SHALL be left out. Every copy SHALL carry the same message ID and headers. Publishing SHALL return after the broker has acknowledged every copy. The first copy that fails SHALL stop the publish, with an error that names its topic.

#### Scenario: .NET subscriber to an interface
- **WHEN** a device publishes `Contracts.ValveOpened`, which implements `Contracts.IAlarm`, and a .NET endpoint is subscribed only to `Contracts.IAlarm`
- **THEN** the .NET endpoint handles the event once

#### Scenario: .NET subscriber to a base class
- **WHEN** a device publishes `Contracts.ValveOpened`, which derives from `Contracts.ValveEvent`, and a .NET endpoint is subscribed only to `Contracts.ValveEvent`
- **THEN** the .NET endpoint handles the event once

### Requirement: Subscribe and unsubscribe
Subscribing a device to an event type SHALL add a subscription to the event's topic in the device's own queue session. The application SHALL be able to subscribe and unsubscribe before and after the endpoint starts. While the endpoint is running, both SHALL return after the broker has confirmed them. The device SHALL apply its subscriptions again when it starts and after every reconnect. After the device unsubscribes from an event type, it SHALL NOT receive further events of that type. Its other subscriptions SHALL be unaffected.

#### Scenario: Subscribe at runtime
- **WHEN** a running device subscribes to `Contracts.PriceChanged` and a .NET endpoint then publishes `Contracts.PriceChanged`
- **THEN** the device handles the event

#### Scenario: Unsubscribe
- **WHEN** a device unsubscribes from `Contracts.PriceChanged` and a .NET endpoint then publishes it
- **THEN** the device does not receive it, while another subscribed endpoint still does

#### Scenario: Subscriptions survive a reconnect
- **WHEN** the device's connection drops and is restored
- **THEN** events published after the reconnect are delivered for every type the device subscribed to

### Requirement: Handled events are subscribed automatically
When it starts, a device endpoint SHALL subscribe to every event type that has a registered handler. An event type is a type that implements the event marker.

#### Scenario: Handler without an explicit subscription
- **WHEN** a device registers a handler for the event `Contracts.PriceChanged`, starts, and a .NET endpoint then publishes `Contracts.PriceChanged`
- **THEN** the device's handler is invoked

### Requirement: One copy of an event is processed
When a device receives an event copy on an event topic and the copy has a `NServiceBus.EnclosedMessageTypes` header, the device SHALL process the copy only if it is the designated copy. It SHALL acknowledge and drop the other copies. The designated copy is the one whose topic belongs to the first type in that header that the device is subscribed to. This SHALL be the same rule the .NET transport uses. A message without the header, or on a topic that is not an event topic, SHALL be processed as it is.

#### Scenario: Subscribed to a type and its base
- **WHEN** a device is subscribed to both `Contracts.ValveEvent` and `Contracts.ValveOpened`, and a .NET endpoint publishes `Contracts.ValveOpened`
- **THEN** the device processes the event once

#### Scenario: Two devices subscribed to two different interfaces
- **WHEN** an event implements `Contracts.IAlarm` and `Contracts.IAudited`, device A is subscribed to `IAlarm`, and device B is subscribed to `IAudited`
- **THEN** A and B each process the event once
