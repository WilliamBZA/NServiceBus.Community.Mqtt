# Spec Delta

## Purpose

Defines native publish/subscribe over MQTT: how events map to topics, polymorphic delivery, subscribe and unsubscribe, and explicit topic subscriptions. It lets NServiceBus endpoints exchange events without a separate subscription store.

## ADDED Requirements

### Requirement: Events are delivered to subscribers
When an event is published, every endpoint that has subscribed to that event type SHALL receive it. Endpoints that have not subscribed SHALL NOT receive it.

#### Scenario: Single subscriber
- **WHEN** an endpoint subscribed to `OrderPlaced` and another endpoint publishes `OrderPlaced`
- **THEN** the subscriber handles the event

#### Scenario: Each subscribing endpoint gets its own copy
- **WHEN** two different endpoints have subscribed to `OrderPlaced` and one `OrderPlaced` is published
- **THEN** each of the two endpoints handles it once

#### Scenario: Non-subscriber is untouched
- **WHEN** an event is published and an endpoint has not subscribed to its type
- **THEN** that endpoint receives nothing

### Requirement: Event topics are collision-free and valid
The topic for an event type SHALL be derived from the type's full, namespace-qualified name, so types with the same short name in different namespaces do not share a topic. The derived topic SHALL be valid for both publish and subscribe even when the type is nested or generic, and SHALL be stable across process restarts.

#### Scenario: Same short name in two namespaces
- **WHEN** `Sales.OrderPlaced` and `Shipping.OrderPlaced` are different event types and a subscriber is subscribed only to `Sales.OrderPlaced`
- **THEN** publishing `Shipping.OrderPlaced` does not reach that subscriber

#### Scenario: Nested event type
- **WHEN** an event type is declared inside another class (its full name contains `+`)
- **THEN** it can be published and subscribed to without a topic error

### Requirement: Polymorphic delivery
A subscriber to a base class or interface SHALL receive events published as any type that derives from or implements it. If the published type matches several of an endpoint's subscribed types, the endpoint SHALL process the event once, not once per matching type.

#### Scenario: Subscribe to base type
- **WHEN** an endpoint subscribes to `BaseEvent` and a publisher publishes `DerivedEvent : BaseEvent`
- **THEN** the endpoint handles the event

#### Scenario: Two matching subscriptions, one delivery
- **WHEN** an endpoint subscribes to both `BaseEvent` and `DerivedEvent` and `DerivedEvent` is published
- **THEN** the event is delivered to the endpoint once

#### Scenario: Event implementing two unrelated interfaces
- **WHEN** an event implements `IFoo` and `IBar`, endpoint A subscribes to `IFoo` and endpoint B subscribes to `IBar`
- **THEN** both A and B each receive the event once

### Requirement: Unsubscribe stops delivery
After an endpoint unsubscribes from an event type, it SHALL NOT receive further events of that type. Other endpoints' subscriptions SHALL be unaffected. Messages already delivered or being processed are not recalled.

#### Scenario: Unsubscribe
- **WHEN** an endpoint unsubscribes from `OrderPlaced` and `OrderPlaced` is then published
- **THEN** that endpoint does not receive it, while another subscribed endpoint still does

### Requirement: Subscription timing and reconnects
Subscribing SHALL work both before and after the endpoint starts receiving. All of the endpoint's subscriptions SHALL be restored after a broker reconnect.

#### Scenario: Subscribe after start
- **WHEN** the endpoint is already receiving and it subscribes to a new event type
- **THEN** events of that type published afterwards are delivered

#### Scenario: Subscriptions survive reconnect
- **WHEN** the broker connection drops and is restored
- **THEN** events published after reconnection are still delivered for every previously subscribed type

### Requirement: Explicit topic subscriptions
Topics registered through the transport's explicit subscription setting SHALL be subscribed at startup for every receiver, and restored after reconnect. Messages arriving on them SHALL be processed under the same rules as any other received message.

#### Scenario: Extra topic
- **WHEN** the transport is configured to subscribe to the topic `sensors/gate` and a valid message is published to it
- **THEN** the endpoint receives and processes the message
